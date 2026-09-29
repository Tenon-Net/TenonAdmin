using System.Text.Json;
using TenonAdmin.Integration;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>投递载荷:只有业务数据,没有任何凭据。</summary>
public sealed record DemoTicketPayload(string Title, decimal Amount);

/// <summary>
/// 合作方工单协议的共用部分:响应体里的 <c>status</c> 区分「已完成 / 处理中 / 失败」——
/// 对方对 202 与去重回放都可能用 2xx 返回处理中,适配器据此识别受理,不能把受理记成成功。
/// </summary>
public abstract class DemoPartnerAdapterBase(OutboundAdapterServices services) : DeliveryAdapterBase(services)
{
    protected override string TargetName => "partner";

    /// <summary>对方的创建路径。</summary>
    protected abstract string CreatePath { get; }

    public override async Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync(CreatePath, context.ReadPayload<DemoTicketPayload>(), context.SendOptions, cancellationToken);
        return DeliverySendResult.From(response, ReadJson<PartnerTicketResult>(response)?.TicketNo);
    }

    public override async Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        var response = await GetAsync($"tickets/by-key/{Uri.EscapeDataString(context.DeliveryKey)}", context.QueryOptions, cancellationToken);
        if (response.StatusCode == 404) return new DeliveryQueryResult(RemoteDeliveryState.NotFound, HttpStatus: 404, CallId: response.CallId);
        if (response.StatusCode is not (>= 200 and < 300))
            return new DeliveryQueryResult(RemoteDeliveryState.Unknown, Reason: response.Reason, HttpStatus: response.StatusCode, CallId: response.CallId);
        var ticket = ReadJson<PartnerTicketResult>(response);
        var state = ticket?.Status switch
        {
            "done" => RemoteDeliveryState.Succeeded,
            "pending" => RemoteDeliveryState.Accepted,
            "failed" or "rejected" => RemoteDeliveryState.Failed,
            _ => RemoteDeliveryState.Unknown,
        };
        return new DeliveryQueryResult(state, ticket?.TicketNo, ticket?.Status, response.StatusCode, response.CallId);
    }

    protected override OutboundClassification? Classify(OutboundHttpResult result)
    {
        if (result.StatusCode is < 200 or >= 300 || string.IsNullOrEmpty(result.Body)) return null;
        try
        {
            using var document = JsonDocument.Parse(result.Body);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("status", out var status))
                return status.GetString() switch
                {
                    "pending" => new OutboundClassification(OutboundOutcome.Accepted, "partner_pending"),
                    "failed" or "rejected" => new OutboundClassification(OutboundOutcome.Rejected, "partner_rejected"),
                    _ => null,
                };
        }
        catch (JsonException)
        {
            // 非 JSON 响应体按默认分类
        }
        return null;
    }
}

/// <summary>对方按 <c>Idempotency-Key</c> 去重、且能按键查询:结果未知时可用同一标识安全重发。</summary>
public class DemoPartnerDeliveryAdapter(OutboundAdapterServices services) : DemoPartnerAdapterBase(services)
{
    public const string AdapterName = "demo-partner";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets";

    public override bool SupportsIdempotency => true;

    public override bool SupportsQuery => true;
}

/// <summary>对方按 <c>Idempotency-Key</c> 去重、但不能查询:结果未知时只能以同一标识重发,次数或时限用尽即耗尽。</summary>
public class DemoPartnerDedupeOnlyAdapter(OutboundAdapterServices services) : DemoPartnerAdapterBase(services)
{
    public const string AdapterName = "demo-partner-dedupeonly";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets";

    public override bool SupportsIdempotency => true;
}

/// <summary>对方不去重、但能按键查询:结果未知时先查询再决定,绝不盲目重发。</summary>
public class DemoPartnerQueryOnlyAdapter(OutboundAdapterServices services) : DemoPartnerAdapterBase(services)
{
    public const string AdapterName = "demo-partner-queryonly";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets-nodedupe";

    public override bool SupportsQuery => true;
}

/// <summary>对方既不去重也不能查询:结果未知只能交给人工核对。</summary>
public class DemoPartnerNoDedupeAdapter(OutboundAdapterServices services) : DemoPartnerAdapterBase(services)
{
    public const string AdapterName = "demo-partner-nodedupe";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets-nodedupe";
}
