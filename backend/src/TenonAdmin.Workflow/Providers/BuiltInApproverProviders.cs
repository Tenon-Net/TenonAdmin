using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>指定成员。<c>params.userIds</c> 或 <c>params.userId</c>。</summary>
public class UserApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.User;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = ApproverParamReader.GetLongList(context.Params, "userIds", "userId");
        return await FilterEnabledAsync(ids, cancellationToken);
    }
}

/// <summary>
/// 直属主管第 N 级。<c>params.level</c>(默认 1);沿 <c>SysUser.DirectorId</c> 上溯 N 步,只返回该级一人;
/// 链中断则空列表。
/// </summary>
public class LeaderApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.Leader;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var level = Math.Max(1, ApproverParamReader.GetInt(context.Params, "level", 1));
        var cursor = context.InitiatorUserId;
        for (var i = 0; i < level; i++)
        {
            var user = await GetUserAsync(cursor, cancellationToken);
            if (user?.DirectorId is not long directorId || directorId <= 0)
                return [];
            cursor = directorId;
        }

        return await FilterEnabledAsync([cursor], cancellationToken);
    }
}

/// <summary>
/// 连续多级主管。<c>params.level</c>=最多级数(默认 1);返回 1..N 级有序 <c>DirectorId</c> 链,
/// 供引擎展开为动态串行任务。
/// </summary>
public class MultiLeaderApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.MultiLeader;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maxLevel = Math.Max(1, ApproverParamReader.GetInt(context.Params, "level", 1));
        var chain = new List<long>();
        var current = context.InitiatorUserId;
        for (var i = 0; i < maxLevel; i++)
        {
            var user = await GetUserAsync(current, cancellationToken);
            if (user?.DirectorId is not long directorId || directorId <= 0)
                break;
            if (chain.Contains(directorId) || directorId == context.InitiatorUserId)
                break;
            chain.Add(directorId);
            current = directorId;
        }

        return await FilterEnabledAsync(chain, cancellationToken);
    }
}

/// <summary>角色下全体启用用户。<c>params.roleId</c> 或 <c>params.roleIds</c>。</summary>
public class RoleApproverProvider(
    IRepository<SysUser> users,
    IRepository<SysUserRole> userRoles) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.Role;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roleIds = ApproverParamReader.GetLongList(context.Params, "roleIds", "roleId");
        if (roleIds.Count == 0)
            return [];

        var userIds = await userRoles.AsQueryable()
            .Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .ToListAsync();
        return await FilterEnabledAsync(userIds, cancellationToken);
    }
}

/// <summary>
/// 职位下全体启用用户。<c>params.positionId</c> 必填;可选 <c>params.orgId</c> 限在该机构。
/// </summary>
public class PositionApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.Position;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var positionId = ApproverParamReader.GetLong(context.Params, "positionId");
        if (positionId is null or <= 0)
            return [];

        var orgId = ApproverParamReader.GetLong(context.Params, "orgId");
        var query = Users.AsQueryable()
            .Where(u => u.PositionId == positionId && u.Enabled);
        if (orgId is long oid && oid > 0)
            query = query.Where(u => u.OrgId == oid);

        var ids = await query.Select(u => u.Id).ToListAsync();
        return ids;
    }
}

/// <summary>发起人自选。Id 来自 <see cref="ApproverResolveContext.SelectedUserIds"/>。</summary>
public class SelfSelectApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.SelfSelect;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = context.SelectedUserIds ?? [];
        return await FilterEnabledAsync(selected, cancellationToken);
    }
}

/// <summary>发起人本人。</summary>
public class InitiatorApproverProvider(IRepository<SysUser> users) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.Initiator;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await FilterEnabledAsync([context.InitiatorUserId], cancellationToken);
    }
}

/// <summary>机构负责人:发起人主属机构的 <c>SysOrg.LeaderUserId</c>。</summary>
public class OrgLeaderApproverProvider(
    IRepository<SysUser> users,
    IRepository<SysOrg> orgs) : ApproverProviderBase(users)
{
    public override string Key => ApproverProviderKeys.OrgLeader;

    public override async Task<IReadOnlyList<long>> ResolveAsync(
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.InitiatorOrgId is not long orgId || orgId <= 0)
            return [];

        var org = await orgs.GetByIdAsync(orgId);
        if (org?.LeaderUserId is not long leaderId || leaderId <= 0)
            return [];

        return await FilterEnabledAsync([leaderId], cancellationToken);
    }
}
