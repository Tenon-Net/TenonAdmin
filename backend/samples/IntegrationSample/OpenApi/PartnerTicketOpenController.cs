using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.Samples.Integration;

/// <summary>开放输出模型:只含可公开字段(不含内部备注、同步通道与审计列);时间带偏移。</summary>
public sealed record PartnerTicketDto(long Id, string Title, string PartnerCode, decimal Amount, DateTimeOffset CreatedAt);

/// <summary>开放输入模型。</summary>
public sealed record PartnerTicketOpenInput(string Title, string PartnerCode, decimal Amount);

/// <summary>开放新建结果:工单 Id 与同步到合作方系统的投递标识。</summary>
public sealed record PartnerTicketOpenCreated(long Id, string DeliveryKey);

/// <summary>
/// 开放接口示例:只写 DTO、调用既有业务服务、声明数据范围——认证、授权(默认拒绝)、范围隔离、调用记录、限流全部由框架完成。
/// 列表与详情经 <see cref="OpenAppScopeQueryExtensions"/> 的 <c>WhereInScope</c> 过滤,
/// 写入前用 <see cref="OpenAppDataScope.EnsureAllowed(string)"/> 校验目标合作方。
/// </summary>
[ApiController]
[Route("api/open/v1/partner-tickets")]
[OpenApi]
[OpenApiDataScope(PartnerScopePolicy.ScopeKey)]
public class PartnerTicketOpenController(IPartnerTicketService tickets, IOpenAppContext app) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("查看可访问的示例工单")]
    public async Task<Result<PagedList<PartnerTicketDto>>> Page([FromQuery] OpenApiPageInput input) =>
        Result<PagedList<PartnerTicketDto>>.Ok(await tickets.Query()
            .WhereInScope(app.DataScope, t => t.PartnerCode)
            .OrderBy(t => t.Id)
            .ToOpenPagedListAsync(input, Map));

    [HttpGet("{id}")]
    [EndpointSummary("查看一张示例工单")]
    public async Task<Result<PartnerTicketDto>> Get(long id)
    {
        var ticket = await tickets.Query().Where(t => t.Id == id).WhereInScope(app.DataScope, t => t.PartnerCode).FirstAsync();
        return Result<PartnerTicketDto>.Ok(Map(ticket ?? throw SampleErrors.TicketNotFoundException()));
    }

    [HttpPost]
    [EndpointSummary("创建示例工单并同步给对方")]
    public async Task<Result<PartnerTicketOpenCreated>> Create(PartnerTicketOpenInput input, CancellationToken cancellationToken)
    {
        app.DataScope.EnsureAllowed(input.PartnerCode);
        var created = await tickets.CreateAsync(new PartnerTicketCreateInput(input.Title, input.PartnerCode, input.Amount), cancellationToken);
        return Result<PartnerTicketOpenCreated>.Ok(new PartnerTicketOpenCreated(created.Id, created.DeliveryKey));
    }

    internal static PartnerTicketDto Map(PartnerTicket t) =>
        new(t.Id, t.Title, t.PartnerCode, t.Amount, new DateTimeOffset(t.CreateTime, TimeZoneInfo.Local.GetUtcOffset(t.CreateTime)));
}
