using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary><see cref="IOrgService"/> 默认实现。</summary>
public class OrgService(
    IRepository<SysOrg> orgs,
    IRbacService rbac,
    // 可选依赖置尾，保持既有消费者的构造调用兼容。
    IDataScopeContext? dataScope = null,
    ICurrentUser? currentUser = null,
    IRepository<SysUser>? userRepo = null) : IOrgService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SysOrg>> ListAsync()
    {
        var all = await orgs.AsQueryable().OrderBy(o => o.Sort).OrderBy(o => o.Id).ToListAsync();
        if (currentUser?.IsSuperAdmin == true) return all;
        var scope = dataScope?.Current;
        if (scope is null || scope.IsUnrestricted) return all;

        // 范围内机构需要附带祖先节点，前端才能组成完整树。
        var scopeIds = scope.OrgIds.ToHashSet();
        var ancestorIds = new HashSet<long>();
        var byId = all.ToDictionary(o => o.Id);
        foreach (var id in scopeIds)
        {
            var cur = byId.GetValueOrDefault(id);
            while (cur is not null && cur.ParentId != 0 && ancestorIds.Add(cur.ParentId))
                cur = byId.GetValueOrDefault(cur.ParentId);
        }
        return all.Where(o => scopeIds.Contains(o.Id) || ancestorIds.Contains(o.Id)).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<SysOrg> GetAsync(long id)
    {
        var org = await orgs.GetByIdAsync(id);
        AdminException.ThrowIf(org is null, ErrorCode.OrgNotFound);
        return org!;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(OrgInput input)
    {
        // 指定父机构时，非超级管理员只能选择数据范围内的机构。
        ValidateOrgInScope(input.ParentId == 0 ? null : input.ParentId);

        if (input.ParentId != 0)
            AdminException.ThrowIf(!await orgs.AnyAsync(o => o.Id == input.ParentId), ErrorCode.OrgNotFound);
        await ValidateLeaderAsync(input.LeaderUserId);

        // 编码留空则自动生成 10 位随机字母数字串(参照 SimpleAdmin)
        var code = input.Code;
        if (string.IsNullOrWhiteSpace(code))
        {
            do { code = GenerateRandomCode(10); }
            while (await orgs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(o => o.Code == code));
        }
        else
        {
            // 编码唯一(库有 idx_sys_org_code 唯一索引):无前置查重会撞约束抛原生 500;查重纳入软删行。
            AdminException.ThrowIf(
                await orgs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(o => o.Code == code),
                ErrorCode.OrgCodeExists);
        }

        var entity = new SysOrg
        {
            ParentId = input.ParentId,
            Name = input.Name,
            Code = code!,
            Category = input.Category,
            Sort = input.Sort,
            Enabled = input.Enabled,
            LeaderUserId = input.LeaderUserId,
        };
        await orgs.InsertAsync(entity);
        // 新增机构改变了祖先"本机构及以下"的子孙集 → 失效全体 scope,新机构的数据方能被相应范围用户看见
        await rbac.InvalidateAllScopesAsync();
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, OrgInput input)
    {
        // 指定父机构时，非超级管理员只能选择数据范围内的机构。
        ValidateOrgInScope(input.ParentId == 0 ? null : input.ParentId);

        // 父指向自己是非法父级,用专用码 OrgInvalidParent(42008),不复用语义不符的 OrgNotFound。
        AdminException.ThrowIf(input.ParentId == id, ErrorCode.OrgInvalidParent);
        // 父指向自己的后代 → 成环:整支子树脱离根、从机构树 UI 消失且无法在 UI 修复。只挡"父指向自己"不够,
        // 得挡整条向下的路径(A→B→C,把 A 的父改成 C 同样成环)。仅在真的换父时才查树,避免每次改资料都拉全表。
        if (input.ParentId != 0)
            await EnsureNotDescendantAsync(id, input.ParentId);

        var entity = await GetAsync(id);
        await ValidateLeaderAsync(input.LeaderUserId);
        // 改编码时排除自身查重(纳入软删行)
        AdminException.ThrowIf(
            input.Code != entity.Code &&
            await orgs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(o => o.Code == input.Code && o.Id != id),
            ErrorCode.OrgCodeExists);

        entity.ParentId = input.ParentId;
        entity.Name = input.Name;
        entity.Code = input.Code;
        entity.Category = input.Category;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        entity.LeaderUserId = input.LeaderUserId;
        await orgs.UpdateAsync(entity);
        // 改父/改编码等结构变更可能改变"本机构及以下"解析 → 失效全体 scope
        await rbac.InvalidateAllScopesAsync();
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        // 非超级管理员只能删除数据范围内的机构。
        ValidateOrgInScope(id);

        AdminException.ThrowIf(await orgs.AnyAsync(o => o.ParentId == id), ErrorCode.OrgHasChildren);
        // 仍有未删除用户归属该机构时禁止删除。
        if (userRepo is not null)
            AdminException.ThrowIf(await userRepo.AnyAsync(u => u.OrgId == id), ErrorCode.OrgHasUsers);
        await orgs.DeleteAsync(id);
        // 删除机构改变了相关"本机构及以下"子孙集 → 失效全体 scope
        await rbac.InvalidateAllScopesAsync();
    }

    /// <inheritdoc />
    public virtual async Task<long> CopyAsync(long id, string? newName = null)
    {
        var all = await orgs.AsQueryable().ToListAsync();   // 平铺(软删已被全局过滤器排除)
        var source = all.FirstOrDefault(o => o.Id == id);
        AdminException.ThrowIf(source is null, ErrorCode.OrgNotFound);
        await ValidateLeaderAsync(source!.LeaderUserId);

        // 已用编码集合(纳入软删行,避免克隆编码撞唯一索引 idx_sys_org_code);生成克隆码时就地扩充
        var usedCodes = (await orgs.AsQueryable().ClearFilter<ISoftDelete>().Select(o => o.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // BFS 从源节点沿 ParentId 只遍历其子树,保证父先于子插入(克隆父的新 Id 已在 idMap 就绪)
        var childrenOf = all.GroupBy(o => o.ParentId).ToDictionary(g => g.Key, g => g.ToList());
        var idMap = new Dictionary<long, long>();   // 旧 Id → 新 Id
        var queue = new Queue<SysOrg>();
        queue.Enqueue(source!);

        // 整支克隆包一个事务:任一步失败整体回滚,不留半棵克隆树
        var result = await orgs.Db.Ado.UseTranAsync(async () =>
        {
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                var clone = new SysOrg
                {
                    // 根挂到源的同级(与源并列);其余重指到已克隆的新父
                    ParentId = node.Id == id ? source!.ParentId : idMap[node.ParentId],
                    Name = node.Id == id ? (newName ?? node.Name + "-副本") : node.Name,
                    Code = NextUniqueCode(node.Code, usedCodes),
                    Category = node.Category,
                    Sort = node.Sort,
                    Enabled = node.Enabled,
                    LeaderUserId = node.LeaderUserId,
                };
                await orgs.InsertAsync(clone);   // AOP 回填新雪花 Id
                idMap[node.Id] = clone.Id;
                if (childrenOf.TryGetValue(node.Id, out var kids))
                    foreach (var kid in kids) queue.Enqueue(kid);
            }
        });
        if (!result.IsSuccess) throw result.ErrorException;
        await rbac.InvalidateAllScopesAsync();   // 新增子树改变"本机构及以下"子孙集 → 失效全体 scope
        return idMap[id];
    }

    /// <summary>
    /// 校验机构是否在当前调用者的数据范围内。
    /// 超级管理员和无限制范围直接放行；<c>null</c> 表示根级。
    /// </summary>
    protected virtual void ValidateOrgInScope(long? orgId)
    {
        if (currentUser?.IsSuperAdmin == true) return;
        var scope = dataScope?.Current;
        if (scope is null || scope.IsUnrestricted) return;
        if (orgId is null) return;   // root-level add: ParentId==0 translates to null here
        AdminException.ThrowIf(!scope.OrgIds.Contains(orgId.Value), ErrorCode.OrgOutOfScope);
    }

    protected virtual async Task ValidateLeaderAsync(long? leaderUserId)
    {
        if (leaderUserId is not > 0 || userRepo is null) return;
        var user = await userRepo.GetByIdAsync(leaderUserId.Value);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(!user!.Enabled, ErrorCode.AccountDisabled);
    }

    /// <summary>
    /// 校验 <paramref name="candidateParent"/> 不是 <paramref name="id"/> 的后代(也不是它自己),否则改父会成环 →
    /// 抛 <see cref="ErrorCode.OrgInvalidParent"/>。BFS 从 id 向下收后代集,带去重防坏数据里已有的环导致死循环。
    /// </summary>
    protected virtual async Task EnsureNotDescendantAsync(long id, long candidateParent)
    {
        var all = await orgs.AsQueryable().Select(o => new { o.Id, o.ParentId }).ToListAsync();
        var descendants = new HashSet<long> { id };
        var queue = new Queue<long>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in all.Where(o => o.ParentId == parent))
                if (descendants.Add(child.Id)) queue.Enqueue(child.Id);
        }
        AdminException.ThrowIf(descendants.Contains(candidateParent), ErrorCode.OrgInvalidParent);
    }

    /// <summary>给克隆节点造唯一编码:优先 <c>{code}-copy</c>,冲突则 <c>-copy2</c>、<c>-copy3</c>…;结果就地登记进 used。</summary>
    protected static string NextUniqueCode(string baseCode, HashSet<string> used)
    {
        var candidate = baseCode + "-copy";
        for (var i = 2; used.Contains(candidate); i++) candidate = $"{baseCode}-copy{i}";
        used.Add(candidate);
        return candidate;
    }

    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>生成指定长度的随机字母数字编码(排除易混淆字符 0/O/1/I/l)</summary>
    protected static string GenerateRandomCode(int length)
    {
        return string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = CodeChars[Random.Shared.Next(CodeChars.Length)];
        });
    }
}
