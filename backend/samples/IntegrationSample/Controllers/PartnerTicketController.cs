using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Samples.Integration;

/// <summary>后台分页入参。</summary>
public record PartnerTicketPageInput : PageInputBase
{
    public string? PartnerCode { get; init; }
}

/// <summary>后台列表项(后台可见内部字段)。</summary>
public sealed record PartnerTicketAdminItem(long Id, string Title, string PartnerCode, decimal Amount, string Channel, string? InternalNote, DateTime CreateTime);

/// <summary>
/// 后台业务接口示例(<c>[RolePermission]</c>,按内核数据范围过滤):新建工单即在同一事务里登记同步投递,
/// 投递进度在「系统集成 → 可靠投递」按投递标识或业务键(工单 Id)查询。
/// </summary>
[ApiController]
[Route("api/v1/sample/partner-ticket")]
[Module("PartnerTicketSample")]
public class PartnerTicketController(IPartnerTicketService tickets) : ControllerBase
{
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<PartnerTicketAdminItem>>> Page([FromQuery] PartnerTicketPageInput input)
    {
        var code = input.PartnerCode?.Trim();
        var page = await tickets.Query()
            .WhereIF(!string.IsNullOrEmpty(code), t => t.PartnerCode == code)
            .OrderBy(t => t.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);
        return Result<PagedList<PartnerTicketAdminItem>>.Ok(new PagedList<PartnerTicketAdminItem>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(t => new PartnerTicketAdminItem(t.Id, t.Title, t.PartnerCode, t.Amount, t.Channel, t.InternalNote, t.CreateTime)).ToList(),
        });
    }

    [HttpPost]
    [RolePermission]
    [OperationLog("新增示例工单")]
    public async Task<Result<PartnerTicketCreated>> Create(PartnerTicketCreateInput input, CancellationToken cancellationToken) =>
        Result<PartnerTicketCreated>.Ok(await tickets.CreateAsync(input, cancellationToken));
}
