using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>从用户表投影办理人候选项。不走机构范围,因为办理人可以跨部门转办。</summary>
public class WfParticipantLookupService(IRepository<SysUser> users) : IWfParticipantLookupService
{
    /// <summary>选择器单页上限,避免一次拉全员。</summary>
    public const int MaxPageSize = 50;

    /// <summary>
    /// 内置数据范围演示账号(<c>DefaultUserSeed</c>)。姓名和账号都是范围名称,
    /// 混进办理人选择器会看起来像「全部数据 / 仅本人数据」这类范围选项。
    /// </summary>
    internal static readonly string[] DataScopeFixtureAccounts =
    [
        "全部数据",
        "本机构数据",
        "本机构及以下",
        "仅本人数据",
        "自定义范围",
    ];

    /// <inheritdoc />
    public virtual async Task<PagedList<WfParticipantCandidateOutput>> PageAsync(
        WfParticipantLookupInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        var hiddenAccounts = DataScopeFixtureAccounts;
        var requested = input.PageSize is > 0 ? input.PageSize.Value : input.Size;
        var size = requested <= 0 ? 20 : Math.Min(requested, MaxPageSize);
        var page = await users.AsQueryable()
            .Where(u => u.Enabled == true && !hiddenAccounts.Contains(u.Account))
            .WhereIF(keyword is not null, u => u.Name.Contains(keyword!) || u.Account.Contains(keyword!))
            .OrderBy(u => u.Id, OrderByType.Asc)
            .Select(u => new { u.Id, u.Name, u.Account })
            .ToPagedListAsync(input.Current, size);

        return new PagedList<WfParticipantCandidateOutput>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items
                .Select(u => new WfParticipantCandidateOutput
                {
                    Id = u.Id,
                    Name = u.Name,
                    Account = u.Account,
                })
                .ToList(),
        };
    }
}
