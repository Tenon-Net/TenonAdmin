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

    /// <inheritdoc />
    public virtual async Task<PagedList<WfParticipantCandidateOutput>> PageAsync(
        WfParticipantLookupInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        var size = input.Size <= 0 ? 20 : Math.Min(input.Size, MaxPageSize);
        var page = await users.AsQueryable()
            .Where(u => u.Enabled == true)
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
