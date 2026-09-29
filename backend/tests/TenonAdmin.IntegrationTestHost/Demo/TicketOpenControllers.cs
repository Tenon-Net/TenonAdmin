using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>开放输出模型:只含可公开字段(不含 InternalNote、审计列);时间用带偏移的 <see cref="DateTimeOffset"/>。</summary>
public sealed record TicketOpenDto(long Id, string Title, string PartnerCode, decimal Amount, bool Closed, DateTimeOffset CreatedAt);

/// <summary>开放输入模型。</summary>
public sealed record TicketOpenInput(string Title, string PartnerCode, decimal Amount);

/// <summary>开放连通性检查的输出。</summary>
public sealed record PingOpenDto(string AppCode, string ScopeKey);

/// <summary>
/// 最小消费者开放端点(机构范围):只写 DTO、调用既有业务服务、声明范围——认证、授权、机构隔离、调用记录全部由框架完成。
/// </summary>
[ApiController]
[Route("api/open/v1/org-tickets")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.Org)]
public class OrgTicketOpenController(IDemoTicketService tickets) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("分页查询本机构范围内的工单")]
    public async Task<Result<PagedList<TicketOpenDto>>> Page([FromQuery] OpenApiPageInput input) =>
        Result<PagedList<TicketOpenDto>>.Ok(await tickets.Query().OrderBy(t => t.Id).ToOpenPagedListAsync(input, Map));

    [HttpGet("{id}")]
    public async Task<Result<TicketOpenDto>> Get(long id) =>
        Result<TicketOpenDto>.Ok(Map(await tickets.GetAsync(id) ?? throw DemoTicketErrors.NotFoundException()));

    [HttpPost]
    public async Task<Result<long>> Create(TicketOpenInput input) =>
        Result<long>.Ok(await tickets.CreateAsync(input.Title, input.PartnerCode, input.Amount));

    [HttpPut("{id}")]
    public async Task<Result<bool>> Update(long id, TicketOpenInput input) =>
        Result<bool>.Ok(await tickets.UpdateAsync(id, input.Title, input.Amount) ? true : throw DemoTicketErrors.NotFoundException());

    [HttpDelete("{id}")]
    public async Task<Result<bool>> Delete(long id) =>
        Result<bool>.Ok(await tickets.DeleteAsync(id) ? true : throw DemoTicketErrors.NotFoundException());

    internal static TicketOpenDto Map(DemoTicket t) =>
        new(t.Id, t.Title, t.PartnerCode, t.Amount, t.Closed, new DateTimeOffset(t.CreateTime, TimeZoneInfo.Local.GetUtcOffset(t.CreateTime)));
}

/// <summary>最小消费者开放端点(自定义合作方范围):列表/详情用框架的范围过滤,写入前校验目标值。</summary>
[ApiController]
[Route("api/open/v1/partner-tickets")]
[OpenApi]
[OpenApiDataScope(PartnerScopePolicy.ScopeKey)]
public class PartnerTicketOpenController(IDemoTicketService tickets, IOpenAppContext app) : ControllerBase
{
    [HttpGet]
    public async Task<Result<PagedList<TicketOpenDto>>> Page([FromQuery] OpenApiPageInput input) =>
        Result<PagedList<TicketOpenDto>>.Ok(await tickets.Query()
            .WhereInScope(app.DataScope, t => t.PartnerCode)
            .OrderBy(t => t.Id)
            .ToOpenPagedListAsync(input, OrgTicketOpenController.Map));

    [HttpGet("{id}")]
    public async Task<Result<TicketOpenDto>> Get(long id)
    {
        var ticket = await tickets.Query().Where(t => t.Id == id).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync();
        return Result<TicketOpenDto>.Ok(OrgTicketOpenController.Map(ticket ?? throw DemoTicketErrors.NotFoundException()));
    }

    [HttpPost]
    public async Task<Result<long>> Create(TicketOpenInput input)
    {
        app.DataScope.EnsureAllowed(input.PartnerCode);
        return Result<long>.Ok(await tickets.CreateAsync(input.Title, input.PartnerCode, input.Amount));
    }

    [HttpPost("{id}/close")]
    [EndpointSummary("关闭合作方工单")]
    public async Task<Result<bool>> Close(long id)
    {
        var ticket = await tickets.Query().Where(t => t.Id == id).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync()
                     ?? throw DemoTicketErrors.NotFoundException();
        return Result<bool>.Ok(await tickets.CloseAsync(ticket.Id));
    }
}

/// <summary>对方回调:告知某次投递的最终结果。</summary>
public sealed record TicketCallbackInput(string DeliveryKey, string Status, string? TicketNo);

/// <summary>回调处理结果。</summary>
public sealed record TicketCallbackResult(string Outcome);

/// <summary>
/// 异步受理的回调确认(合作方范围):投递标识来自对方,先反解出工单并按本应用范围核实,看不到的与不存在同样答复;
/// 再声明本端点负责的适配器与业务键,别的应用拿到标识也确认不了不属于自己的投递。
/// </summary>
[ApiController]
[Route("api/open/v1/partner-callbacks")]
[OpenApi]
[OpenApiDataScope(PartnerScopePolicy.ScopeKey)]
public class PartnerCallbackOpenController(IDeliveryConfirmationService confirmations, IDemoTicketService tickets, IOpenAppContext app)
    : ControllerBase
{
    [HttpPost("ticket-status")]
    [EndpointSummary("合作方回调工单最终状态")]
    public async Task<Result<TicketCallbackResult>> TicketStatus(TicketCallbackInput input, CancellationToken cancellationToken)
    {
        var ticket = DemoTicketService.TryParseTicketId(input.DeliveryKey, out var ticketId)
            ? await tickets.Query().Where(t => t.Id == ticketId).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync(cancellationToken)
            : null;
        var outcome = ticket is null
            ? DeliveryConfirmOutcome.NotFound
            : await confirmations.ConfirmAsync(new DeliveryConfirmation
            {
                DeliveryKey = input.DeliveryKey,
                Succeeded = input.Status == "done",
                Adapters =
                [
                    DemoPartnerDeliveryAdapter.AdapterName, DemoPartnerDedupeOnlyAdapter.AdapterName,
                    DemoPartnerQueryOnlyAdapter.AdapterName, DemoPartnerNoDedupeAdapter.AdapterName,
                ],
                BusinessKey = ticket.Id.ToString(CultureInfo.InvariantCulture),
                RemoteReference = input.TicketNo,
                Note = "对方回调",
            }, cancellationToken);
        return Result<TicketCallbackResult>.Ok(new TicketCallbackResult(outcome.ToString()));
    }
}

/// <summary>在开放请求里发起普通出站调用(验证开放调用记录与它触发的出站调用记录共用同一追踪标识)。</summary>
[ApiController]
[Route("api/open/v1/partner-lookups")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.None)]
public class PartnerLookupOpenController(DemoPartnerClient partner) : ControllerBase
{
    [HttpGet("{key}")]
    [EndpointSummary("向合作方查询某个幂等标识是否已有工单")]
    public async Task<Result<bool>> Get(string key, CancellationToken cancellationToken) =>
        Result<bool>.Ok(await partner.FindByKeyAsync(key, cancellationToken) is not null);
}

/// <summary>不涉及范围数据的开放端点:返回当前应用身份(证明系统身份经上下文可读)。</summary>
[ApiController]
[Route("api/open/v1/ping")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.None)]
public class PingOpenController(IOpenAppContext app) : ControllerBase
{
    [HttpGet]
    public Result<PingOpenDto> Get() => Result<PingOpenDto>.Ok(new PingOpenDto(app.Identity.AppCode, app.DataScope.Key));

    /// <summary>故意抛未处理异常,验证开放调用记录与错误出口。</summary>
    [HttpPost("boom")]
    public Result<bool> Boom() => throw new InvalidOperationException("demo failure");
}
