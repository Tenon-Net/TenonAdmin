using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Integration;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>对方返回的工单结果。</summary>
public sealed record PartnerTicketResult(string? TicketNo, string? Status);

/// <summary>
/// 消费者适配器示例:对方要求「HMAC 签名」认证,并用 HTTP 200 + <c>{"status":"rejected"}</c> 表达业务拒绝。
/// 只覆写认证与结果识别两步,地址安全、超时、追踪、记录全部复用框架(不复制任何 HTTP 基础代码)。
/// </summary>
public class DemoPartnerClient(OutboundAdapterServices services, TimeProvider time) : OutboundAdapterBase(services)
{
    public const string Target = "partner-signed";

    protected override string TargetName => Target;

    /// <summary>创建工单(写操作:幂等标识随请求发出,底层绝不重发)。</summary>
    public Task<OutboundResponse> CreateTicketAsync(string title, decimal amount, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        PostJsonAsync("tickets", new { title, amount }, new OutboundCallOptions { Operation = "ticket.create", IdempotencyKey = idempotencyKey }, cancellationToken);

    /// <summary>按幂等标识查询对方结果。</summary>
    public async Task<PartnerTicketResult?> FindByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync($"tickets/by-key/{Uri.EscapeDataString(idempotencyKey)}", new OutboundCallOptions { Operation = "ticket.query" }, cancellationToken);
        return response.Outcome == OutboundOutcome.Succeeded ? ReadJson<PartnerTicketResult>(response) : null;
    }

    /// <summary>对方的签名协议:<c>hex(HMACSHA256(secret, "{METHOD}\n{path}\n{timestamp}"))</c>。</summary>
    protected override async ValueTask AuthenticateAsync(HttpRequestMessage request, OutboundTarget target, CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(target, cancellationToken);
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var data = Encoding.UTF8.GetBytes($"{request.Method.Method.ToUpperInvariant()}\n{request.RequestUri!.AbsolutePath}\n{timestamp}");
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(credential.Secret), data));
        request.Headers.TryAddWithoutValidation("X-Partner-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Partner-Signature", signature);
    }

    /// <summary>对方用 200 表达业务拒绝:识别出来,不能记成成功。</summary>
    protected override OutboundClassification? Classify(OutboundHttpResult result) =>
        result.StatusCode == 200 && result.Body?.Contains("\"status\":\"rejected\"", StringComparison.Ordinal) == true
            ? new OutboundClassification(OutboundOutcome.Rejected, "business_rejected")
            : null;
}
