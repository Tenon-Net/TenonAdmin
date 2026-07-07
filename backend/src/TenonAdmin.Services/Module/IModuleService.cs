namespace TenonAdmin.Services;

/// <summary>
/// 模块/应用服务(多应用门户)——模块的增删改查。模块是菜单树的分区维度,顶级目录挂靠模块。
/// <para>类 public、方法 virtual:自定义模块规则时继承覆写(设计 §5.3)。</para>
/// </summary>
public interface IModuleService
{
    /// <summary>取全部模块(平铺列表,按 Sort、Id 排序)</summary>
    Task<IReadOnlyList<SysModule>> ListAsync();

    /// <summary>按 Id 取单个模块,不存在则抛 <see cref="TenonAdmin.Core.ErrorCode.ModuleNotFound"/></summary>
    Task<SysModule> GetAsync(long id);

    /// <summary>新增模块,返回新 Id;编码唯一</summary>
    Task<long> AddAsync(ModuleInput input);

    /// <summary>更新模块;改编码时排除自身查重</summary>
    Task UpdateAsync(long id, ModuleInput input);

    /// <summary>删除模块(软删除);内置 system 模块受保护不可删</summary>
    Task DeleteAsync(long id);
}
