using System.Text.Json;
using TenonAdmin.Integration;

namespace TenonApp.Integrations;

/// <summary>投递载荷:只放业务数据,不放任何凭据(凭据在每次投递时由框架从凭据来源取得)。</summary>
public sealed record SampleDocPayload(string Title);

/// <summary>对方返回的处理状态。【按对方协议填写】</summary>
public sealed record PartnerTicketStatus(string? TicketNo, string? Status);

/// <summary>对方的错误答复。【按对方协议填写】</summary>
public sealed record PartnerError(string? Error);

/// <summary>
/// 可靠投递适配器示例:把新建的示例文档同步到对方系统。投递器负责领取、租约、退避重试、中断恢复和人工操作,
/// 适配器只描述对方协议,需要填写的是标了【按对方协议填写】的几处。
/// 发送时框架已经把投递标识作为幂等标识放进 <see cref="DeliveryContext.SendOptions"/>,所有重试与人工操作都沿用它。
/// </summary>
public class SampleDocSyncAdapter(OutboundAdapterServices services) : DeliveryAdapterBase(services)
{
    /// <summary>适配器名(入队时引用;已入队的记录靠它找到适配器,改名前先处理完存量记录)。</summary>
    public const string AdapterName = "sample-doc-sync";

    /// <inheritdoc />
    public override string Name => AdapterName;

    /// <summary>出站目标名,对应配置 <c>TenonAdmin:Integration:Outbound:Targets:partner</c>。</summary>
    protected override string TargetName => "partner";

    /// <summary>
    /// 【按对方协议填写】这里是 true,只因为演练用的本地模拟对方确实按幂等标识去重(入门演练依赖这一点)。
    /// 接真实对方前先核实对方协议:只有对方确实按幂等标识去重才能返回 true——声明错了,调用结果未知时框架会用同一标识重发,
    /// 对方就会重复执行业务;不确定就改成 false,结果未知的记录会进入「待核对」交人工处理。
    /// </summary>
    public override bool SupportsIdempotency => true;

    /// <summary>
    /// 【按对方协议填写】同上:本地模拟对方能按幂等标识查询处理结果,所以是 true。真实对方确实能查询时才返回 true
    /// 并按对方协议实现 <see cref="QueryAsync"/>;不确定就改成 false。
    /// </summary>
    public override bool SupportsQuery => true;

    /// <inheritdoc />
    public override async Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        // 【按对方协议填写】路径与请求体(这里载荷原样作为 JSON 请求体:{"title": ...})
        var response = await PostJsonAsync("tickets", context.ReadPayload<SampleDocPayload>(), context.SendOptions, cancellationToken);
        return DeliverySendResult.From(response, ReadJson<PartnerTicketStatus>(response)?.TicketNo);
    }

    /// <inheritdoc />
    public override async Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        // 【按对方协议填写】按幂等标识查询,并把对方状态翻译成框架的四种结论
        var response = await GetAsync($"tickets/by-key/{Uri.EscapeDataString(context.DeliveryKey)}", context.QueryOptions, cancellationToken);
        // 【按对方协议填写】只有对方明确答复「没有这条记录」才算 NotFound(本地模拟对方:404 + {"error":"not_found"})。
        // 框架据此认定可以安全重发,所以网关 404、路径写错等其他 404 与非 2xx 一样按查询失败处理
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

    /// <summary>
    /// 【按对方协议填写】2xx 不一定是完成:对方用响应体 <c>status</c> 表达「处理中 / 被拒」时在这里识别,
    /// 受理必须识别成 <see cref="OutboundOutcome.Accepted"/>,不能记成成功。返回 null 表示按默认规则分类。
    /// </summary>
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
            // 非 JSON 响应体按默认规则分类
        }
        return null;
    }
}
