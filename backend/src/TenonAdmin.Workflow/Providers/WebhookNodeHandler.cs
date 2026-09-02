using System.Text;
using System.Text.Json;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>
/// Webhook 节点 handler(M3a-1 Task 8)——首个真实 <see cref="IWorkflowNodeHandler"/> 实现,把节点执行结果
/// 落到一次真实 HTTP 调用上。
/// <para>
/// <b>HttpClient/SSRF 围栏零新造</b>(D1):直接复用内核既有的 <see cref="JobHttpClient"/>/
/// <see cref="JobHttpFence"/>(配置源 <c>TenonAdmin:Jobs:Http</c>)。本包不引 <c>Microsoft.Extensions.Http</c>,
/// 不写第二份 URL/Header 校验或 CIDR 黑名单——安全代码抄第二遍就是第二个会各自腐化的实现。
/// </para>
/// <para>
/// 分类规则(状态码 → 结果类型 / <c>Retry-After</c> 读法 / 异常分类 / <c>ManualFallback</c> 开关)见语义契约
/// 「Webhook 分类规则」小节(Task 8 D2/D3 定案),<c>.loop/wf-m3a1.md</c>。<b>贯穿唯一判据</b>:「过一会儿原样
/// 再发一次同一个请求,有没有可能成功?」有 → <see cref="WfNodeExecutionResultType.RetryableFailure"/>;
/// 没有 → <see cref="WfNodeExecutionResultType.TerminalFailure"/>(或按节点配置转
/// <see cref="WfNodeExecutionResultType.ManualFallback"/>)。
/// </para>
/// <para>
/// <b>两种 <see cref="OperationCanceledException"/> 必须区分</b>(陷阱 1):外部 <c>cancellationToken</c> 已取消
/// (宿主停机/dispatcher 取消)→ 原样抛出,不产生任何结果;外部 ct 未取消(= 本 handler 自己的 HTTP 超时,
/// .NET 里表现为 <see cref="TaskCanceledException"/>,是 OCE 的子类)→ <see cref="WfNodeExecutionResultType.RetryableFailure"/>。
/// 判据逐字照抄仓内先例 <see cref="HttpAdminJob"/>:<c>catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)</c>。
/// </para>
/// <para>
/// <see cref="JobHttpFence.ValidateUrl"/>/<see cref="JobHttpFence.ValidateHeader"/> 抛出的 <see cref="AdminException"/>
/// 在开 socket 之前发生(配置缺陷,重试不可能修好),<b>必须捕获并映射成
/// <see cref="WfNodeExecutionResultType.TerminalFailure"/>,不许逸出</b>——逸出会让 tx2 从未开始、execution 行
/// 停 <see cref="WfNodeExecutionStatus.Running"/> 持租约、过期被重领再抛,无限活锁且 attempt 表一行都没有。
/// </para>
/// <para>
/// 不写 <c>catch (Exception)</c> 兜底:未预料的异常原样逸出,不悄悄归成一种业务结果(§6 R5,本 Task 生产侧
/// 无 worker,活锁不可达;真建 worker 那一轮再定兜底策略)。
/// </para>
/// <para>类不 <c>sealed</c>、分类/组请求/读响应各自 <c>virtual</c>——消费者继承覆写单步而不是复制整个类,
/// 再前置注册子类即可胜出(<see cref="WfNodeExecutionDispatcher.ResolveHandler"/> 用 <c>FirstOrDefault</c>,
/// 注册顺序 = 生效顺序,陷阱 11)。</para>
/// </summary>
public class WebhookNodeHandler(HttpClient client, AdminJobsOptions jobs, TimeProvider time) : IWorkflowNodeHandler
{
    private const string DefaultMethod = "POST";

    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"];

    public WfNodeType NodeType => WfNodeType.Webhook;

    public virtual async Task<WfNodeExecutionResult> ExecuteAsync(
        WfNodeExecutionContext context, CancellationToken cancellationToken)
    {
        // 外部已取消时原样抛出,连请求都不组——避免一次白跑的 BuildRequest/SendAsync(D2 表 B)。
        cancellationToken.ThrowIfCancellationRequested();

        var props = context.NodeProps;
        var onFailure = props?.WebhookOnFailure ?? WfWebhookFailureAction.Fail;

        HttpRequestMessage request;
        try
        {
            request = BuildRequest(context, props);
        }
        catch (AdminException ex)
        {
            // 原始码放进 Summary 供排障(D2 表 B):47009/47011 之类的内核码,对外仍统一报 48030。
            return ApplyFailureAction(
                WfNodeExecutionResult.TerminalFailure(
                    WorkflowErrorCode.WebhookConfigInvalid, $"{(int)ex.Code} {ex.Message}"),
                onFailure);
        }

        using (request)
        {
            var timeout = ResolveTimeout(props?.WebhookTimeoutSeconds, context.DeadlineAtUtc, time.GetUtcNow());
            // JobHttpClient.Client.Timeout 是 InfiniteTimeSpan(陷阱 3)——超时必须自己用 CancelAfter 实现,
            // 不能指望 HttpClient 帮忙,否则 webhook 永不超时,租约到期后被第二个 worker 领走。
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ApplyFailureAction(
                    WfNodeExecutionResult.RetryableFailure(
                        WorkflowErrorCode.WebhookTransportFailed,
                        $"Webhook 请求超时({timeout.TotalSeconds:0}s):{request.RequestUri}"),
                    onFailure);
            }
            catch (HttpRequestException ex)
            {
                return ApplyFailureAction(
                    WfNodeExecutionResult.RetryableFailure(WorkflowErrorCode.WebhookTransportFailed, ex.Message),
                    onFailure);
            }

            using (response)
            {
                var excerpt = await ReadCappedAsync(response, jobs.Http.MaxResponseLogBytes, cts.Token);
                return ApplyFailureAction(ClassifyStatus(response, excerpt), onFailure);
            }
        }
    }

    /// <summary>
    /// 组请求:URL/方法/Header 全部校验通过后才构造 <see cref="HttpRequestMessage"/>;任何一步不合规抛
    /// <see cref="AdminException"/>,由 <see cref="ExecuteAsync"/> 统一捕获映射(本方法不接触网络)。
    /// </summary>
    protected virtual HttpRequestMessage BuildRequest(WfNodeExecutionContext context, WfNodeProps? props)
    {
        var url = props?.WebhookUrl ?? "";
        JobHttpFence.ValidateUrl(url, jobs.Http);

        // 先按白名单拒方法名(字符串比较),再构造 HttpMethod——含空格等非法 token 传给 new HttpMethod(..)
        // 会抛未捕获的 FormatException,顺序颠倒会让"方法非法"从可控的配置错误变成失控的系统异常。
        var methodName = (props?.WebhookMethod ?? DefaultMethod).Trim().ToUpperInvariant();
        if (Array.IndexOf(AllowedMethods, methodName) < 0)
        {
            throw new AdminException(ErrorCode.JobPropsInvalid,
                new Dictionary<string, object?> { ["key"] = "method" }, $"不支持的 Webhook 方法:{methodName}");
        }
        var method = new HttpMethod(methodName);

        var request = new HttpRequestMessage(method, url);
        if (method != HttpMethod.Get && method != HttpMethod.Head)
            request.Content = new StringContent(BuildRequestBody(context), Encoding.UTF8, "application/json");

        if (props?.WebhookHeaders is { Count: > 0 } headers)
        {
            foreach (var (name, value) in headers)
            {
                JobHttpFence.ValidateHeader(name, value);   // 拦 CRLF/控制字符走私
                if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    // Host 决定连上之后落到哪个 vhost(IP 围栏管的是连到哪台机,不拒等于围栏漏一格);
                    // Content-Length 由 TryAddWithoutValidation 写出与实际 body 不符的长度会造成请求走私。
                    throw new AdminException(ErrorCode.JobPropsInvalid,
                        new Dictionary<string, object?> { ["key"] = "headers" }, $"不允许自定义该请求头:{name}");
                }
                if (!request.Headers.TryAddWithoutValidation(name, value))
                    request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    /// <summary>
    /// 外呼请求体(D5):引擎生成的最小字段集,<b>刻意不含 <see cref="WfNodeExecutionContext.VariablesJson"/></b>
    /// ——handler 输入/输出是 PII 与密钥泄漏面最大的一处,消费方要正文用 <see cref="WfNodeExecutionContext.ExecutionKey"/>
    /// 回查(理由同 <c>WorkflowEngine.BuildExecutionOutboxPayload</c> 的 D6)。走 <see cref="WfModelJson.Options"/>,
    /// 不另起一份配置。
    /// </summary>
    protected virtual string BuildRequestBody(WfNodeExecutionContext context) =>
        JsonSerializer.Serialize(
            new
            {
                executionKey = context.ExecutionKey,
                instanceId = context.InstanceId,
                tokenId = context.TokenId,
                nodeVisitId = context.NodeVisitId,
                nodeId = context.NodeId,
                definitionVersionId = context.DefinitionVersionId,
                businessKey = context.BusinessKey,
                attempt = context.Attempt,
            },
            WfModelJson.Options);

    /// <summary>
    /// 按 HTTP 状态码分类(D2 表 A,★ 本 Task 头号交付物)。<c>Retry-After</c> 只在判为
    /// <see cref="WfNodeExecutionResultType.RetryableFailure"/> 时读(408/423/425/429/5xx 除 501)。
    /// </summary>
    protected virtual WfNodeExecutionResult ClassifyStatus(HttpResponseMessage response, string excerpt)
    {
        var summary = WfNodeExecutionAttemptStore.Truncate(excerpt);
        var status = (int)response.StatusCode;

        if (status is >= 200 and <= 299)
            return WfNodeExecutionResult.Succeeded(outputJson: excerpt, summary: summary);

        if (IsRetryableStatus(status))
        {
            return WfNodeExecutionResult.RetryableFailure(
                WorkflowErrorCode.WebhookRequestFailed, summary, ReadRetryAfter(response));
        }

        return WfNodeExecutionResult.TerminalFailure(WorkflowErrorCode.WebhookRequestFailed, summary);
    }

    /// <summary>
    /// 唯一判据的状态码落地:408/423/425/429 与除 501 外的 5xx 可重试;3xx(不跟随重定向)、其余 4xx、501
    /// 终态。501 是 5xx 里唯一的例外,是定案不是遗漏(D2)。
    /// </summary>
    protected virtual bool IsRetryableStatus(int status) => status switch
    {
        408 or 423 or 425 or 429 => true,
        501 => false,
        >= 500 and <= 599 => true,
        _ => false,
    };

    /// <summary>
    /// <c>Retry-After</c> 同时支持 delta-seconds 与 HTTP-date,取 <c>Delta ?? (Date - now)</c>;结果
    /// <c>&lt;= 0</c> 或解析不出 ⇒ <c>null</c>(让引擎走自己的指数退避)。<c>Date</c> 分支用注入的
    /// <see cref="TimeProvider"/>,不用 <see cref="DateTimeOffset.UtcNow"/>(陷阱 14)。
    /// </summary>
    protected virtual TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        var delta = header.Delta ?? (header.Date is { } date ? date - time.GetUtcNow() : null);
        return delta is { } d && d > TimeSpan.Zero ? d : null;
    }

    /// <summary>
    /// 超时上限(D5):<c>min(clamp(configured ?? 30, 1, 120), deadlineAtUtc - nowUtc)</c>,下限 1s。
    /// 上限的作用是不让一次 HTTP 调用活得比租约长;下限保证不传出一个会让 <c>CancelAfter</c> 抛
    /// <see cref="ArgumentOutOfRangeException"/> 的非正值。<c>public static</c>:纯函数,唯一能不起
    /// HTTP/DB 验证的部分。
    /// </summary>
    public static TimeSpan ResolveTimeout(int? configuredSeconds, DateTimeOffset deadlineAtUtc, DateTimeOffset nowUtc)
    {
        var clamped = Math.Clamp(configuredSeconds ?? 30, 1, 120);
        var seconds = Math.Min(clamped, (deadlineAtUtc - nowUtc).TotalSeconds);
        return TimeSpan.FromSeconds(Math.Max(seconds, 1));
    }

    /// <summary>
    /// 节点配置开关(D3):<c>props.webhookOnFailure = manual</c> 时,把本 handler 原本要返回的每一个
    /// <see cref="WfNodeExecutionResultType.TerminalFailure"/> 一律改吐
    /// <see cref="WfNodeExecutionResultType.ManualFallback"/>(错误码与摘要照旧)。<b>一条规则、无子情形</b>
    /// ——覆盖全部 <c>TerminalFailure</c>(含配置缺陷),不做「4xx 给人、配置错不给人」的二级分叉;不覆盖
    /// <see cref="WfNodeExecutionResultType.RetryableFailure"/>(仍是重试,不是人工)。
    /// </summary>
    private static WfNodeExecutionResult ApplyFailureAction(WfNodeExecutionResult result, WfWebhookFailureAction action)
    {
        if (action != WfWebhookFailureAction.Manual || result.Type != WfNodeExecutionResultType.TerminalFailure)
            return result;
        return WfNodeExecutionResult.ManualFallback(result.ErrorCode, result.Summary);
    }

    /// <summary>
    /// 读响应体开头若干字节做摘要,照抄 <see cref="HttpAdminJob"/> 的 <c>ReadCappedAsync</c>——不能
    /// <c>ReadAsStreamAsync()</c> 整体读(一个吐 2GB 的端点会把宿主打 OOM),且必须净化控制字符
    /// (含 NUL,否则 PostgreSQL 的 text 列不接受它,写 attempt 摘要会抛)。
    /// </summary>
    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0) return "";
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var read = 0;
        while (read < maxBytes)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, maxBytes - read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return string.Concat(text.Select(c => c is '\t' or '\r' or '\n' || (c >= 0x20 && c != 0x7F) ? c : '.'));
    }
}
