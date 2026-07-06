using System.Reflection;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 实体程序集注册表——CodeFirst 建表时扫描哪些程序集,由它说了算。
/// <para>各层在自己的 AddXxx() 里登记(SqlSugar 层登记自身、Services 层登记业务实体所在程序集),
/// 用户模块程序集由 <c>AddTenonAdmin()</c> 按扫描配置追加(设计 §5.7)。
/// 初始化器对每个程序集扫 <c>[SugarTable]</c> 非抽象类 → <c>CodeFirst.InitTables</c>。</para>
/// </summary>
public sealed class TenonEntitySources
{
    private readonly List<Assembly> _assemblies = [];

    /// <summary>已登记的程序集(去重后)</summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>登记一个实体程序集(重复登记自动忽略)</summary>
    public void Add(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly)) _assemblies.Add(assembly);
    }
}
