using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundHttpInvoker"/> 默认实现(实现契约 §6)。一次调用的步骤:
/// <list type="number">
/// <item>按名取目标;路径相对基础地址解析且不得越出;地址静态校验(字面 IP 立即判定,域名在建连时逐个复检解析结果);</item>
/// <item>带上调用 Id(<c>X-Request-Id</c>)、幂等标识与附加请求头;写操作带 <c>Connection: close</c> 并走不入池的客户端;</item>
/// <item>施加凭据(适配器可按调用覆盖);凭据缺失则不发送;</item>
/// <item>在「调用超时」与「调用方取消」两个信号下发送(为投递发起的调用超时不超过租约减安全余量),响应体按上限读入内存;</item>
/// <item>分类(适配器可按调用覆盖),写一条不含正文与请求头的调用记录。</item>
/// </list>
/// <b>不做任何自动重试</b>;异常一律转成分类结果返回。步骤均为 virtual,可继承覆写。
/// </summary>
public class OutboundHttpInvoker(
    IOutboundTargetRegistry targets,
    OutboundHttpClientFactory clients,
    IOutboundAuthenticator authenticator,
    IOutboundResultClassifier classifier,
    IOutboundLogService logs,
    IntegrationOptions options,
    TimeProvider time,
    ILogger<OutboundHttpInvoker> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IOutboundHttpInvoker
{
    /// <summary>携带调用 Id 的请求头。</summary>
    public const string RequestIdHeader = "X-Request-Id";

    /// <summary>幂等标识最大长度。</summary>
    public const int MaxIdempotencyKeyLength = 128;

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        RequestIdHeader, "Host", "Connection", "Content-Length", "Transfer-Encoding", "Upgrade", "TE", "Trailer",
    };

    /// <inheritdoc />
    public virtual async Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Path);

        var callId = NewCallId();
        var started = time.GetTimestamp();
        var traceId = ResolveTraceId();
        var target = targets.Find(request.Target);

        OutboundResponse response;
        var url = "";
        if (target is null)
        {
            response = Failure(request.Target, callId, started, new(OutboundOutcome.NotSent, "target_unknown"),
                $"未配置的出站目标 {request.Target}。");
        }
        else
        {
            ValidateHeaders(request, target);
            var uri = ResolveUri(target, request.Path);
            url = uri.GetLeftPart(UriPartial.Path);
            response = await ExecuteAsync(request, target, uri, callId, started, cancellationToken);
        }

        logger.LogDebug("出站调用 {Target} {Method} {Url} → {Outcome} {StatusCode} {ElapsedMs}ms CallId={CallId}",
            response.Target, request.Method.Method, url, response.Outcome, response.StatusCode, (long)response.Elapsed.TotalMilliseconds, callId);
        await RecordAsync(request, url, response, traceId);
        return response;
    }

    /// <summary>执行校验、认证、发送与分类(不含调用记录)。</summary>
    protected virtual async Task<OutboundResponse> ExecuteAsync(
        OutboundRequest request, OutboundTarget target, Uri uri, string callId, long started, CancellationToken cancellationToken)
    {
        try
        {
            OutboundAddressGuard.Validate(uri, target.TrustedCidrs);
        }
        catch (OutboundTargetBlockedException)
        {
            return Failure(target.Name, callId, started, new(OutboundOutcome.NotSent, "target_blocked"), "目标地址被安全策略拒绝。");
        }

        var write = OutboundHttpClientFactory.IsWrite(request.Method);
        var message = BuildMessage(request, target, uri, callId, write);
        try
        {
            try
            {
                if (request.Authenticate is { } authenticate)
                    await authenticate(message, target, cancellationToken);
                else
                    await authenticator.ApplyAsync(message, target, cancellationToken);
            }
            catch (OutboundCredentialUnavailableException ex)
            {
                return Failure(target.Name, callId, started, new(OutboundOutcome.NotSent, ex.Reason),
                    ex.Reason == OutboundCredentialUnavailableException.MissingReason ? "目标未配置凭据。" : "目标凭据无效。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure(target.Name, callId, started, new(OutboundOutcome.Cancelled, "cancelled"), "调用方已取消(请求未发出)。");
            }
            catch (Exception ex)
            {
                // 适配器自定义认证(签名、换取令牌)失败:请求未发出。摘要只记异常类型,不记消息(自定义代码的消息可能夹带秘密)
                logger.LogWarning("出站调用的认证步骤失败,请求未发出。Target={Target} CallId={CallId} ExceptionType={ExceptionType}",
                    target.Name, callId, ex.GetType().Name);
                return Failure(target.Name, callId, started,
                    new(OutboundOutcome.NotSent, "auth_setup_failed", Transient: ex is HttpRequestException or TimeoutException),
                    ex.GetType().Name);
            }

            var timeout = ResolveTimeout(request, target);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await clients.GetClient(target, write)
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            }
            catch (Exception ex) when (ex is not (ArgumentException or InvalidOperationException))
            {
                var classification = ClassifyException(ex, cancellationToken.IsCancellationRequested, timeoutCts.IsCancellationRequested);
                return Failure(target.Name, callId, started, classification, Describe(classification, timeout));
            }

            using (httpResponse)
            {
                var status = (int)httpResponse.StatusCode;
                string? body;
                bool truncated;
                try
                {
                    (body, truncated) = await ReadBodyAsync(httpResponse, options.Outbound.MaxResponseBytes, timeoutCts.Token);
                }
                catch (Exception ex) when (ex is not (ArgumentException or InvalidOperationException))
                {
                    // 已收到状态行,但响应体没读完:远端的最终判定不完整,按结果未知处理(调用方取消仍记为取消)
                    var incomplete = cancellationToken.IsCancellationRequested
                        ? new OutboundClassification(OutboundOutcome.Cancelled, "cancelled")
                        : new OutboundClassification(OutboundOutcome.Unknown, timeoutCts.IsCancellationRequested ? "timeout" : "response_incomplete");
                    return Failure(target.Name, callId, started, incomplete, Describe(incomplete, timeout), status);
                }

                var headers = CollectHeaders(httpResponse);
                var result = new OutboundHttpResult(status, body, headers);
                OutboundClassification verdict;
                try
                {
                    verdict = request.Classify?.Invoke(result) ?? classifier.Classify(result);
                }
                catch (Exception ex)
                {
                    logger.LogError("出站结果分类失败,按结果未知处理。Target={Target} CallId={CallId} ExceptionType={ExceptionType}",
                        target.Name, callId, ex.GetType().Name);
                    verdict = new OutboundClassification(OutboundOutcome.Unknown, "classifier_failed");
                }

                return new OutboundResponse
                {
                    CallId = callId,
                    Target = target.Name,
                    Outcome = verdict.Outcome,
                    Transient = verdict.Transient,
                    Reason = verdict.Reason,
                    RetryAfter = verdict.RetryAfter,
                    StatusCode = status,
                    Body = body,
                    BodyTruncated = truncated,
                    Headers = headers,
                    Elapsed = time.GetElapsedTime(started),
                };
            }
        }
        finally
        {
            // 请求体归调用方所有(缓冲型内容可以原样再发一次);只释放框架创建的请求消息
            message.Content = null;
            message.Dispose();
        }
    }

    /// <summary>
    /// 把传输层异常映射成分类:调用方取消 → 取消;目标被拦截、DNS/连接/TLS 失败、建连超时 → 未发出;
    /// 调用超时、连接中断、响应不完整及其他 → 结果未知(远端可能已执行)。
    /// </summary>
    protected virtual OutboundClassification ClassifyException(Exception exception, bool callerCancelled, bool timedOut)
    {
        if (callerCancelled) return new(OutboundOutcome.Cancelled, "cancelled");
        if (Find<OutboundTargetBlockedException>(exception) is not null) return new(OutboundOutcome.NotSent, "target_blocked");
        if (timedOut) return new(OutboundOutcome.Unknown, "timeout");
        // SocketsHttpHandler.ConnectTimeout 到期:OperationCanceledException + 内层 TimeoutException,调用双方的令牌都未触发
        if (exception is OperationCanceledException && Find<TimeoutException>(exception) is not null)
            return new(OutboundOutcome.NotSent, "connect_timeout", Transient: true);
        return Find<HttpRequestException>(exception)?.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => new(OutboundOutcome.NotSent, "dns_failed", Transient: true),
            HttpRequestError.ConnectionError => new(OutboundOutcome.NotSent, "connection_failed", Transient: true),
            HttpRequestError.SecureConnectionError => new(OutboundOutcome.NotSent, "tls_failed", Transient: true),
            HttpRequestError.ResponseEnded => new(OutboundOutcome.Unknown, "response_ended"),
            HttpRequestError.InvalidResponse => new(OutboundOutcome.Unknown, "invalid_response"),
            _ => new(OutboundOutcome.Unknown, "transport_error"),
        };
    }

    /// <summary>
    /// 本次调用的超时:按调用覆盖优先,否则用目标配置。为可靠投递发起的调用(带投递记录 Id)不得超过租约减安全余量——
    /// 否则租约先到期,中断恢复会与仍在进行的调用重叠;超出即截断并告警。
    /// </summary>
    protected virtual TimeSpan ResolveTimeout(OutboundRequest request, OutboundTarget target)
    {
        var timeout = request.Timeout ?? target.Timeout;
        if (request.DeliveryId is null) return timeout;
        var cap = TimeSpan.FromSeconds(options.Delivery.LeaseSeconds - IntegrationDeliveryOptions.LeaseSafetyMarginSeconds);
        if (timeout <= cap) return timeout;
        logger.LogWarning("为投递发起的出站调用超时 {Timeout} 超过租约的安全上限 {Cap},已截断到该上限。Target={Target} DeliveryId={DeliveryId}",
            timeout, cap, target.Name, request.DeliveryId);
        return cap;
    }

    /// <summary>
    /// 把相对路径解析到目标基础地址下。开头的 <c>/</c> 视为相对基础地址;绝对地址或解析后越出基础地址(如 <c>../</c>)
    /// 属于调用方编程错误,抛 <see cref="ArgumentException"/>。
    /// </summary>
    protected virtual Uri ResolveUri(OutboundTarget target, string path)
    {
        var relative = path.TrimStart('/');
        Uri resolved;
        try
        {
            resolved = new Uri(target.BaseUri, new Uri(relative, UriKind.Relative));
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException($"出站路径必须是相对目标基础地址的路径:{path}", nameof(path), ex);
        }
        if (!resolved.AbsoluteUri.StartsWith(target.BaseUri.AbsoluteUri, StringComparison.Ordinal))
            throw new ArgumentException($"出站路径越出了目标基础地址:{path}", nameof(path));
        return resolved;
    }

    /// <summary>校验附加请求头与幂等标识(编程错误抛 <see cref="ArgumentException"/>,在任何网络动作之前)。</summary>
    protected virtual void ValidateHeaders(OutboundRequest request, OutboundTarget target)
    {
        if (request.IdempotencyKey is { } key
            && (key.Length is 0 or > MaxIdempotencyKeyLength || !OutboundHeaders.IsValidValue(key)))
            throw new ArgumentException($"幂等标识须为 1–{MaxIdempotencyKeyLength} 位可见 ASCII 字符。", nameof(request));
        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
        {
            if (!OutboundHeaders.IsValidName(name) || !OutboundHeaders.IsValidValue(value))
                throw new ArgumentException($"出站请求头 {name} 的名称或值不合法(只允许可见 ASCII)。", nameof(request));
            if (ReservedHeaders.Contains(name) || string.Equals(name, target.IdempotencyHeader, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"出站请求头 {name} 由框架管理,不能自行设置(幂等标识请用 IdempotencyKey)。", nameof(request));
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && request.Content is null)
                throw new ArgumentException($"没有请求体时不能设置 {name}。", nameof(request));
        }
    }

    /// <summary>构造请求消息:调用 Id、幂等标识、附加请求头;写操作带 <c>Connection: close</c>。</summary>
    protected virtual HttpRequestMessage BuildMessage(OutboundRequest request, OutboundTarget target, Uri uri, string callId, bool write)
    {
        var message = new HttpRequestMessage(request.Method, uri) { Content = request.Content };
        message.Headers.TryAddWithoutValidation(RequestIdHeader, callId);
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
            message.Headers.TryAddWithoutValidation(target.IdempotencyHeader, request.IdempotencyKey);
        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
        {
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                message.Content!.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                message.Headers.Remove(name);
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }
        if (write) message.Headers.ConnectionClose = true;
        return message;
    }

    /// <summary>按上限读取响应体;超出部分丢弃并标记截断。按 <c>Content-Type</c> 的字符集解码,缺省 UTF-8。</summary>
    protected virtual async Task<(string? Body, bool Truncated)> ReadBodyAsync(
        HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            var room = maxBytes - (int)buffer.Length;
            if (read > room)
            {
                buffer.Write(chunk, 0, room);
                truncated = true;
                break;
            }
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) return (null, truncated);
        return (EncodingOf(response.Content.Headers.ContentType?.CharSet).GetString(buffer.GetBuffer(), 0, (int)buffer.Length), truncated);
    }

    /// <summary>写一条调用记录(不含正文与请求头;写失败只告警)。</summary>
    protected virtual Task RecordAsync(OutboundRequest request, string url, OutboundResponse response, string? traceId) =>
        logs.RecordAsync(new OutboundLogEntry
        {
            Target = response.Target,
            Operation = request.Operation,
            HttpMethod = request.Method.Method,
            Url = url,
            StatusCode = response.StatusCode,
            Outcome = response.Outcome,
            Reason = response.Reason,
            ErrorSummary = response.ErrorSummary,
            ElapsedMs = (long)response.Elapsed.TotalMilliseconds,
            CallId = response.CallId,
            TraceId = traceId,
            DeliveryId = request.DeliveryId,
            AttemptNo = request.AttemptNo,
        }, CancellationToken.None);

    /// <summary>生成调用 Id(32 位十六进制)。</summary>
    protected virtual string NewCallId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 调用记录的追踪标识:在请求里发起的调用用 ASP.NET 请求标识(与开放调用记录、<c>X-Trace-Id</c>、内核异常日志同一个值,
    /// 一次开放请求和它触发的出站调用能串起来);后台任务等没有请求上下文时退回 W3C Activity 的 TraceId。
    /// </summary>
    protected virtual string? ResolveTraceId() =>
        httpContextAccessor?.HttpContext?.TraceIdentifier ?? Activity.Current?.TraceId.ToHexString();

    private OutboundResponse Failure(string target, string callId, long started, OutboundClassification classification, string? summary, int? status = null) => new()
    {
        CallId = callId,
        Target = target,
        Outcome = classification.Outcome,
        Transient = classification.Transient,
        Reason = classification.Reason,
        RetryAfter = classification.RetryAfter,
        StatusCode = status,
        ErrorSummary = IntegrationText.Truncate(summary, 500),
        Elapsed = time.GetElapsedTime(started),
    };

    private static string Describe(OutboundClassification classification, TimeSpan timeout) => classification.Reason switch
    {
        "cancelled" => "调用方已取消。",
        "timeout" => $"超过 {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} 秒未完成。",
        "response_incomplete" => "响应未完整接收。",
        _ => "出站传输失败。",
    };

    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers) headers[name] = string.Join(", ", values);
        foreach (var (name, values) in response.Content.Headers) headers[name] = string.Join(", ", values);
        return headers;
    }

    private static Encoding EncodingOf(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charset.Trim('"', ' '));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException) yield return e;
    }

    private static T? Find<T>(Exception exception) where T : Exception => Chain(exception).OfType<T>().FirstOrDefault();
}
