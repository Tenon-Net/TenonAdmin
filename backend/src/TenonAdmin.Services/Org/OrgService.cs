using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IOrgService"/> 默认实现。机构树本身不做环检测(设计上只禁止"父指向自己"这一种一步环),
/// 更复杂的多级环由前端拼树时天然规避(找不到父节点的机构不会出现在树上)。
/// </summary>
public class OrgService(IRepository<SysOrg> orgs, IRbacService rbac) : IOrgService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SysOrg>> ListAsync() =>
        await orgs.AsQueryable().OrderBy(o => o.Sort).OrderBy(o => o.Id).ToListAsync();

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
        if (input.ParentId != 0)
            AdminException.ThrowIf(!await orgs.AnyAsync(o => o.Id == input.ParentId), ErrorCode.OrgNotFound);
        // 编码唯一(库有 idx_sys_org_code 唯一索引):无前置查重会撞约束抛原生 500;查重纳入软删行(P1-10)
        AdminException.ThrowIf(
            await orgs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(o => o.Code == input.Code),
            ErrorCode.OrgCodeExists);

        var entity = new SysOrg
        {
            ParentId = input.ParentId,
            Name = input.Name,
            Code = input.Code,
            Sort = input.Sort,
            Enabled = input.Enabled,
        };
        await orgs.InsertAsync(entity);
        // 新增机构改变了祖先"本机构及以下"的子孙集 → 失效全体 scope,新机构的数据方能被相应范围用户看见
        await rbac.InvalidateAllScopesAsync();
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, OrgInput input)
    {
        // 父指向自己是非法父级,用专用码 OrgInvalidParent(42008),不再复用语义不符的 OrgNotFound(P2-12)
        AdminException.ThrowIf(input.ParentId == id, ErrorCode.OrgInvalidParent);

        var entity = await GetAsync(id);
        // 改编码时排除自身查重(纳入软删行)
        AdminException.ThrowIf(
            input.Code != entity.Code &&
            await orgs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(o => o.Code == input.Code && o.Id != id),
            ErrorCode.OrgCodeExists);

        entity.ParentId = input.ParentId;
        entity.Name = input.Name;
        entity.Code = input.Code;
        entity.Sort = input.Sort;
        entity.Enabled = input.Enabled;
        await orgs.UpdateAsync(entity);
        // 改父/改编码等结构变更可能改变"本机构及以下"解析 → 失效全体 scope
        await rbac.InvalidateAllScopesAsync();
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        AdminException.ThrowIf(await orgs.AnyAsync(o => o.ParentId == id), ErrorCode.OrgHasChildren);
        await orgs.DeleteAsync(id);
        // 删除机构改变了相关"本机构及以下"子孙集 → 失效全体 scope
        await rbac.InvalidateAllScopesAsync();
    }

    /// <inheritdoc />
    public virtual async Task<long> CopyAsync(long id)
    {
        var all = await orgs.AsQueryable().ToListAsync();   // 平铺(软删已被全局过滤器排除)
        var source = all.FirstOrDefault(o => o.Id == id);
        AdminException.ThrowIf(source is null, ErrorCode.OrgNotFound);

        // 已用编码集合(纳入软删行,避免克隆编码撞唯一索引 idx_sys_org_code);生成克隆码时就地扩充
        var usedCodes = (await orgs.AsQueryable().ClearFilter<ISoftDelete>().Select(o => o.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // BFS 从源节点沿 ParentId 只遍历其子树,保证父先于子插入(克隆父的新 Id 已在 idMap 就绪)
        var childrenOf = all.GroupBy(o => o.ParentId).ToDictionary(g => g.Key, g => g.ToList());
        var idMap = new Dictionary<long, long>();   // 旧 Id → 新 Id
        var queue = new Queue<SysOrg>();
        queue.Enqueue(source!);

        // 整支克隆包一个事务:任一步失败整体回滚,不留半棵克隆树
        await orgs.Db.Ado.UseTranAsync(async () =>
        {
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                var clone = new SysOrg
                {
                    // 根挂到源的同级(与源并列);其余重指到已克隆的新父
                    ParentId = node.Id == id ? source!.ParentId : idMap[node.ParentId],
                    Name = node.Id == id ? node.Name + "-副本" : node.Name,
                    Code = NextUniqueCode(node.Code, usedCodes),
                    Sort = node.Sort,
                    Enabled = node.Enabled,
                };
                await orgs.InsertAsync(clone);   // AOP 回填新雪花 Id
                idMap[node.Id] = clone.Id;
                if (childrenOf.TryGetValue(node.Id, out var kids))
                    foreach (var kid in kids) queue.Enqueue(kid);
            }
        });
        await rbac.InvalidateAllScopesAsync();   // 新增子树改变"本机构及以下"子孙集 → 失效全体 scope
        return idMap[id];
    }

    /// <summary>给克隆节点造唯一编码:优先 <c>{code}-copy</c>,冲突则 <c>-copy2</c>、<c>-copy3</c>…;结果就地登记进 used。</summary>
    protected static string NextUniqueCode(string baseCode, HashSet<string> used)
    {
        var candidate = baseCode + "-copy";
        for (var i = 2; used.Contains(candidate); i++) candidate = $"{baseCode}-copy{i}";
        used.Add(candidate);
        return candidate;
    }
}
