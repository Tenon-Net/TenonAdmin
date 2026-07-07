namespace TenonAdmin.AspNetCore;

/// <summary>
/// 模块归属标记(设计 §5.4)。挂在内置控制器上声明它属于哪个可选模块;
/// 当模块名出现在 <c>TenonAdmin:Api:DisabledModules</c> 里时,<see cref="DisabledModuleConvention"/>
/// 会把该控制器整体摘除(接口 404,等于关闭模块)。
/// <para>核心控制器(认证/用户/角色/权限等)不挂本特性,不可被禁。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModuleAttribute(string name) : Attribute
{
    /// <summary>模块名(与 DisabledModules 配置项按大小写不敏感比对)</summary>
    public string Name { get; } = name;
}
