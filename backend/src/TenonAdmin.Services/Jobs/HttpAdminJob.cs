using System.Text;
using System.Text.Json;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 内置 HTTP 任务处理器(HandlerKind=Http,scheduling-ledger §7.1)——属性包模式:
/// <c>url</c>(必)、<c>method</c>(默认 GET)、<c>headers</c>(JSON 对象串)、<c>body</c>、
/// <c>contentType</c>(默认 application/json)、<c>timeoutSeconds</c>(默认走任务 TimeoutSeconds)、
/// <c>successStatuses</c>(默认 2xx,可 "200,204,302")。
/// <para>响应状态不符 → 本次 Failed(响应体截断进记录);围栏(47009)在入库与执行两处都拦。
/// <b>永不落请求头</b>——header 常含密钥(§13-1)。</para>
/// </summary>
public class HttpAdminJob(JobHttpClient http, AdminJobsOptions options) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        var url = Require(context.Properties, "url");
        JobHttpFence.ValidateUrl(url, options.Http);   // 执行时复检:入库后改 DNS/改配置不留窗口

        var method = new HttpMethod((Get(context.Properties, "method") ?? "GET").ToUpperInvariant());
        using var request = new HttpRequestMessage(method, url);

        var body = Get(context.Properties, "body");
        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, Get(context.Properties, "contentType") ?? "application/json");

        ApplyHeaders(request, Get(context.Properties, "headers"));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = int.TryParse(Get(context.Properties, "timeoutSeconds"), out var t) && t > 0 ? t : 0;
        if (timeoutSeconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 属性包级超时:算普通失败(可重试),与外层超时(Timeout,不重试)区分
            throw new TimeoutException($"HTTP 请求超时({timeoutSeconds}s):{url}");
        }

        using (response)
        {
            var excerpt = await ReadCappedAsync(response, options.Http.MaxResponseLogBytes, cts.Token);
            // 只落 scheme+host+path:原始 url 里的 userinfo 与查询串常含凭据,而执行记录的读权限弱于任务编辑权
            var safeUrl = new Uri(url).GetLeftPart(UriPartial.Path);
            context.Log?.Invoke($"HTTP {(int)response.StatusCode} {method} {safeUrl}" + (excerpt.Length > 0 ? $"\n{excerpt}" : ""));
            var expect = Get(context.Properties, "successStatuses");
            if (!IsSuccess(response, expect))
                throw new HttpRequestException($"HTTP 状态不符合预期:{(int)response.StatusCode}(期望 {(string.IsNullOrWhiteSpace(expect) ? "2xx" : expect)})");
        }
    }

    private static string Require(IReadOnlyDictionary<string, string?> props, string key)
    {
        var value = Get(props, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new AdminException(ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = key }, $"属性包缺少必填键:{key}");
        return value!;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> props, string key) =>
        props.TryGetValue(key, out var v) ? v : null;

    private static void ApplyHeaders(HttpRequestMessage request, string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson)) return;
        Dictionary<string, string?>? headers;
        try { headers = JsonSerializer.Deserialize<Dictionary<string, string?>>(headersJson!); }
        catch (JsonException ex)
        {
            throw new AdminException(ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = "headers" }, $"headers 不是合法 JSON 对象:{ex.Message}");
        }
        foreach (var (name, value) in headers ?? [])
        {
            JobHttpFence.ValidateHeader(name, value);   // 拦 CRLF 走私(入库时也拦过一遍,这里防绕过前端直改库)
            if (request.Headers.TryAddWithoutValidation(name, value)) continue;
            request.Content?.Headers.TryAddWithoutValidation(name, value);   // Content-Type 等内容头落到 Content 上
        }
    }

    private static bool IsSuccess(HttpResponseMessage response, string? successStatuses)
    {
        if (string.IsNullOrWhiteSpace(successStatuses) || successStatuses!.Trim().Equals("2xx", StringComparison.OrdinalIgnoreCase))
            return response.IsSuccessStatusCode;
        return successStatuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => int.TryParse(s, out var code) && code == (int)response.StatusCode);
    }

    /// <summary>
    /// 读响应体开头若干字节做摘要。<b>必须净化控制字符</b>:目标返回二进制(含 <c>\0</c>)时,
    /// PostgreSQL 的 text 列不接受 NUL(22021),写执行记录会抛 → 记录永远闭合不了 →
    /// SerialSkip 判据恒真 → 该任务再也不会被触发(指一个吐 \0 的 URL 就能永久瘫掉一个任务)。
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
