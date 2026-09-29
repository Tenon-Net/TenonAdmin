using System.Text.Json;
using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>投递载荷:只有业务数据,没有任何凭据(凭据在每次投递时由框架从凭据来源取得)。</summary>
public sealed record PartnerTicketPayload(string Title, decimal Amount);

/// <summary>对方返回的工单状态。</summary>
public sealed record PartnerTicketStatus(string? TicketNo, string? Status);

/// <summary>对方的错误答复。</summary>
public sealed record PartnerError(string? Error);

/// <summary>
/// 合作方工单协议的共用部分:对方用响应体 <c>status</c> 表达「完成 / 处理中 / 失败」,2xx 也可能是处理中——
/// 在这里识别受理,不能把受理记成成功。
/// </summary>
public abstract class PartnerTicketAdapterBase(OutboundAdapterServices services) : DeliveryAdapterBase(services)
{
    protected override string TargetName => "partner";

    /// <summary>对方的建单路径。</summary>
    protected abstract string CreatePath { get; }

    public override async Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        // SendOptions 已带上幂等标识(= 投递标识)与投递记录关联,按目标配置的 Idempotency-Key 头发给对方
        var response = await PostJsonAsync(CreatePath, context.ReadPayload<PartnerTicketPayload>(), context.SendOptions, cancellationToken);
        return DeliverySendResult.From(response, ReadJson<PartnerTicketStatus>(response)?.TicketNo);
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

/// <summary>
/// 现代接口:对方按 <c>Idempotency-Key</c> 去重,并能按它查询结果。结果未知时框架可用同一标识安全重发,或先查询再决定;
/// 受理后按间隔轮询最终结果。
/// </summary>
public class PartnerTicketSyncAdapter(OutboundAdapterServices services) : PartnerTicketAdapterBase(services)
{
    public const string AdapterName = "partner-ticket-sync";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets";

    /// <summary>只有对方确实按标识去重时才能声明——声明错了会让框架在结果未知时重发出重复业务。</summary>
    public override bool SupportsIdempotency => true;

    public override bool SupportsQuery => true;

    public override async Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        var response = await GetAsync($"tickets/by-key/{Uri.EscapeDataString(context.DeliveryKey)}", context.QueryOptions, cancellationToken);
        // 只有对方明确答复「没有这条记录」(404 + {"error":"not_found"})才算 NotFound:框架据此认定可以安全重发,
        // 网关 404、路径写错等其他 404 一律按查询失败处理
        if (response.StatusCode == 404 && ReadJson<PartnerError>(response)?.Error == "not_found")
            return new DeliveryQueryResult(RemoteDeliveryState.NotFound, HttpStatus: 404, CallId: response.CallId);
        if (response.StatusCode is not (>= 200 and < 300))
            return new DeliveryQueryResult(RemoteDeliveryState.Unknown, Reason: response.Reason, HttpStatus: response.StatusCode, CallId: response.CallId);
        var ticket = ReadJson<PartnerTicketStatus>(response);
        var state = ticket?.Status switch
        {
            "done" => RemoteDeliveryState.Succeeded,
            "pending" => RemoteDeliveryState.Accepted,
            "failed" or "rejected" => RemoteDeliveryState.Failed,
            _ => RemoteDeliveryState.Unknown,
        };
        return new DeliveryQueryResult(state, ticket?.TicketNo, ticket?.Status, response.StatusCode, response.CallId);
    }
}

/// <summary>
/// 旧接口:对方既不去重也不能查询。结果未知(超时、连接中断、对方 5xx)时框架<b>不会</b>重发,记录进入「待核对」,
/// 由管理员在对方系统里核实后确认已成功或确认未执行。
/// </summary>
public class LegacyTicketSyncAdapter(OutboundAdapterServices services) : PartnerTicketAdapterBase(services)
{
    public const string AdapterName = "partner-ticket-legacy";

    public override string Name => AdapterName;

    protected override string CreatePath => "tickets-nodedupe";
}
