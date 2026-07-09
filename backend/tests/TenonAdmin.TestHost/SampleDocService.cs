using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例机构隔离业务服务——DataEntity 表的 CRUD 范本(消费方可直接照抄):
/// <list type="bullet">
///   <item>读(<see cref="ListAsync"/>)走 <c>AsQueryable()</c>,由全局过滤器自动按当前数据范围裁剪。</item>
///   <item>改/删先 <c>GetByIdAsync</c>(同样经范围过滤)校验可见性,看不到即视为"不存在/无权"返回 false;
///   仓储写路径另有越权兜底(<c>SqlSugarRepository</c> 对 IOrgScoped 的 Update/Delete 自带范围校验),双保险。</item>
///   <item>新建不设 <c>CreateOrgId</c>——由审计 AOP 从 <c>ICurrentUser.OrgId</c> 回填(机构数据范围锚点)。</item>
/// </list>
/// 内核内模块可另加 ErrorCode + <c>AdminException.ThrowIf</c> 返回精确 NotFound;消费方受限于 ErrorCode 不可扩展,
/// 这里用返回值表达结果(也可自定义异常经自写过滤器兜)。
/// </summary>
public class SampleDocService(IRepository<SampleDoc> repo) : ISampleDocService
{
    /// <inheritdoc />
    public virtual async Task<long> CreateAsync(string title)
    {
        var doc = new SampleDoc { Title = title };
        await repo.InsertAsync(doc);
        return doc.Id;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<SampleDoc>> ListAsync() =>
        await repo.AsQueryable().OrderBy(d => d.Id).ToListAsync();

    /// <inheritdoc />
    public virtual async Task<bool> RenameAsync(long id, string title)
    {
        var doc = await repo.GetByIdAsync(id);   // 越权/不存在 → null(经范围过滤)
        if (doc is null) return false;
        doc.Title = title;
        return await repo.UpdateAsync(doc) > 0;  // 仓储写守卫二次兜底
    }

    /// <inheritdoc />
    public virtual async Task<bool> DeleteAsync(long id)
    {
        if (await repo.GetByIdAsync(id) is null) return false;
        return await repo.DeleteAsync(id) > 0;
    }
}
