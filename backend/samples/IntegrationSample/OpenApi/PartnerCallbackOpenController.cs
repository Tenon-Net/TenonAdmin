using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>合作方回调:告知某次投递的最终结果。</summary>
/// <param name="DeliveryKey">投递标识(即当初发给对方的幂等标识)</param>
/// <param name="Status">最终状态:<c>done</c> 完成;<c>failed</c>/<c>rejected</c> 失败</param>
/// <param name="TicketNo">对方单号(可空)</param>
public sealed record PartnerTicketCallback(string DeliveryKey, string Status, string? TicketNo);

/// <summary>回调处理结果。</summary>
public sealed record PartnerTicketCallbackResult(string Outcome);

/// <summary>
/// 异步受理的回调确认示例:合作方以自己的接入凭据调用本端点(与其他开放接口一样默认拒绝、须授予),
/// 业务代码把对方状态翻译成成功/失败交给 <see cref="IDeliveryConfirmationService"/>。
/// <para>投递标识来自对方,不能直接信任:先反解出工单并按本应用的合作方范围核实(看不到的工单与不存在同样答复),
/// 再声明本端点负责的适配器与业务键——别的应用拿到标识也确认不了不属于自己的投递。</para>
/// </summary>
[ApiController]
[Route("api/open/v1/partner-callbacks")]
[OpenApi]
[OpenApiDataScope(PartnerScopePolicy.ScopeKey)]
public class PartnerCallbackOpenController(IDeliveryConfirmationService confirmations, IPartnerTicketService tickets, IOpenAppContext app)
    : ControllerBase
{
    [HttpPost("ticket-status")]
    [EndpointSummary("接收示例工单的处理结果")]
    public async Task<Result<PartnerTicketCallbackResult>> TicketStatus(PartnerTicketCallback input, CancellationToken cancellationToken)
    {
        bool succeeded = input.Status switch
        {
            "done" => true,
            "failed" or "rejected" => false,
            _ => throw new AdminException((ErrorCode)SampleErrors.CallbackStatusInvalid,
                new Dictionary<string, object?> { ["status"] = input.Status }, "未知的回调状态。"),
        };
        var ticket = PartnerTicketService.TryParseTicketId(input.DeliveryKey, out var ticketId)
            ? await tickets.Query().Where(t => t.Id == ticketId).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync(cancellationToken)
            : null;
        var outcome = ticket is null
            ? DeliveryConfirmOutcome.NotFound
            : await confirmations.ConfirmAsync(new DeliveryConfirmation
            {
                DeliveryKey = input.DeliveryKey,
                Succeeded = succeeded,
                Adapters = [PartnerTicketSyncAdapter.AdapterName, LegacyTicketSyncAdapter.AdapterName],
                BusinessKey = ticket.Id.ToString(CultureInfo.InvariantCulture),
                RemoteReference = input.TicketNo,
                Note = "对接方回调",
            }, cancellationToken);
        return Result<PartnerTicketCallbackResult>.Ok(new PartnerTicketCallbackResult(outcome.ToString()));
    }
}
