namespace TenonAdmin.Services;

/// <summary>
/// 机构服务——机构树的增删改查。后端只提供平铺列表(按 <c>Sort</c>/<c>Id</c> 排序),
/// 前端凭 <c>ParentId</c> 自行拼树,避免后端维护"树 + 平铺"两份数据。
/// <para>类 public、方法 virtual:自定义机构规则(如接第三方组织架构同步)时继承覆写(设计 §5.3)。</para>
/// </summary>
public interface IOrgService
{
    /// <summary>取全部机构(平铺列表,按 Sort、Id 排序;前端自行拼树)</summary>
    Task<IReadOnlyList<SysOrg>> ListAsync();

    /// <summary>按 Id 取单个机构,不存在则抛 <see cref="TenonAdmin.Core.ErrorCode.OrgNotFound"/></summary>
    Task<SysOrg> GetAsync(long id);

    /// <summary>新增机构,返回新 Id;若指定了父机构则校验其存在</summary>
    Task<long> AddAsync(OrgInput input);

    /// <summary>更新机构;不允许把父机构设成自身(会造成环)</summary>
    Task UpdateAsync(long id, OrgInput input);

    /// <summary>删除机构(软删除);若仍有子机构挂靠则拒绝(需先移除或迁移子机构)</summary>
    Task DeleteAsync(long id);

    /// <summary>
    /// 复制机构子树(含目标节点及其全部后代),整支克隆挂到源节点的同级下,返回新根 Id。
    /// 用于按既有结构快速搭建相似机构(如连锁网点)。克隆节点编码追加 <c>-copy</c> 后缀去重(编码唯一约束)。
    /// </summary>
    Task<long> CopyAsync(long id);
}
