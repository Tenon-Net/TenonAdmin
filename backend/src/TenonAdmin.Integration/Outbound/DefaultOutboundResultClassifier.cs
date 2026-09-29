using System.Globalization;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundResultClassifier"/> 默认实现(实现契约 §6.3 表):
/// <list type="table">
/// <item><term>2xx(202 除外)</term><description><see cref="OutboundOutcome.Succeeded"/></description></item>
/// <item><term>202</term><description><see cref="OutboundOutcome.Accepted"/>(受理 ≠ 成功)</description></item>
/// <item><term>401、403</term><description><see cref="OutboundOutcome.AuthenticationFailed"/></description></item>
/// <item><term>429</term><description><see cref="OutboundOutcome.NotSent"/>(暂时性):对方明确没处理,可稍后重发;带 <c>Retry-After</c> 时按它退避</description></item>
/// <item><term>408、503、其他 5xx</term><description><see cref="OutboundOutcome.Unknown"/>:远端可能已执行;503 的 <c>Retry-After</c>
/// 只作为具备安全恢复能力时的退避提示,不能证明请求未执行
/// (网关在上游可能已执行后也会回 503)</description></item>
/// <item><term>3xx、其他 4xx</term><description><see cref="OutboundOutcome.Rejected"/>(重定向不跟随,按拒绝处理)</description></item>
/// </list>
/// 业务层面的受理/拒绝(如 200 响应体里的失败码)由适配器覆写分类识别。
/// </summary>
public class DefaultOutboundResultClassifier(TimeProvider time) : IOutboundResultClassifier
{
    /// <inheritdoc />
    public virtual OutboundClassification Classify(OutboundHttpResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var status = result.StatusCode;
        var reason = "http_" + status.ToString(CultureInfo.InvariantCulture);
        var retryAfter = status is 429 or 503 ? RetryAfterOf(result) : null;
        return status switch
        {
            202 => new(OutboundOutcome.Accepted, reason),
            >= 200 and < 300 => new(OutboundOutcome.Succeeded, reason),
            401 or 403 => new(OutboundOutcome.AuthenticationFailed, reason),
            408 => new(OutboundOutcome.Unknown, reason),
            429 => new(OutboundOutcome.NotSent, reason, retryAfter, Transient: true),
            >= 300 and < 500 => new(OutboundOutcome.Rejected, reason),
            _ => new(OutboundOutcome.Unknown, reason, retryAfter),
        };
    }

    /// <summary>读取 <c>Retry-After</c>(秒数或 HTTP 日期);无法解析返回 null,已过去的日期按 0 处理。</summary>
    protected virtual TimeSpan? RetryAfterOf(OutboundHttpResult result) =>
        result.Headers.TryGetValue("Retry-After", out var value) ? ParseRetryAfter(value, time.GetUtcNow()) : null;

    /// <summary>解析 <c>Retry-After</c> 头的值。</summary>
    public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParseExact(text, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at > now ? at - now : TimeSpan.Zero;
        return null;
    }
}
