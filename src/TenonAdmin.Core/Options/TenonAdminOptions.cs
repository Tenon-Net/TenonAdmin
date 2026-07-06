using System.Reflection;

namespace TenonAdmin.Core;

/// <summary>TenonAdmin 顶层配置(对应 appsettings 的 TenonAdmin 节 + 代码侧开关)</summary>
public class TenonAdminOptions
{
    public AdminDatabaseOptions Database { get; set; } = new();

    /// <summary>是否扫描入口程序集及其引用注册用户模块(设计 §5.7)。骨架暂未启用扫描。</summary>
    public bool ScanApplicationAssemblies { get; set; } = true;

    /// <summary>额外显式指定要扫描的程序集(代码侧,不从配置绑定)</summary>
    public List<Assembly> ApplicationAssemblies { get; set; } = new();
}
