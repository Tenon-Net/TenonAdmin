using Microsoft.AspNetCore.Authorization;
using TenonAdmin.AspNetCore;

namespace TenonAdmin.Integration;

/// <summary>接入应用认证方案的常量。</summary>
public static class OpenAppAuthenticationDefaults
{
    /// <summary>认证方案名。开放端点只用它认证,后台端点只用内核默认 JWT 方案,两条通道互不替代。</summary>
    public const string Scheme = "TenonOpenApp";

    /// <summary>默认凭据请求头。凭据只从请求头读取,永不从 URL 读取。</summary>
    public const string HeaderName = "X-Api-Key";
}

/// <summary>
/// 标记开放业务接口(类或方法,实现契约 §4.4–§4.5):
/// <list type="bullet">
/// <item>只用 <see cref="OpenAppAuthenticationDefaults.Scheme"/> 认证——JWT 用户令牌进不来,接入凭据也进不了后台接口;</item>
/// <item>默认拒绝:应用须被授予本端点的规范化路由权限码(<c>VERB:/api/open/v{n}/...</c>);</item>
/// <item>必须同时声明 <see cref="OpenApiDataScopeAttribute"/>;路由须以 <c>api/open/v{n}/</c> 开头;</item>
/// <item>进入独立 OpenAPI 文档分组、写开放调用记录,不再进入用户操作日志(<see cref="ISkipOperationLogMetadata"/>)。</item>
/// </list>
/// 这些约束在启动时统一校验,不满足即拒绝启动。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OpenApiAttribute : Attribute, IAuthorizeData, ISkipOperationLogMetadata
{
    /// <summary>不支持:开放接口的授权由应用授权表决定,不走 ASP.NET 策略。</summary>
    public string? Policy
    {
        get => null;
        set => throw new NotSupportedException("开放接口不支持 ASP.NET 授权策略,请在接入应用授权中勾选端点。");
    }

    /// <summary>不支持:接入应用没有角色。</summary>
    public string? Roles
    {
        get => null;
        set => throw new NotSupportedException("开放接口不支持角色授权,请在接入应用授权中勾选端点。");
    }

    /// <inheritdoc />
    public string? AuthenticationSchemes
    {
        get => OpenAppAuthenticationDefaults.Scheme;
        set => throw new NotSupportedException("开放接口的认证方案固定为 " + OpenAppAuthenticationDefaults.Scheme + "。");
    }
}

/// <summary>
/// 声明开放端点使用的数据范围策略(类或方法;方法级覆盖类级)。未声明即拒绝启动;应用未绑定该范围即 403。
/// <para><see cref="OpenApiDataScopes.None"/> 表示端点不读写受范围约束的数据(无需绑定,内核机构维度按拒绝处理);
/// 其余键必须对应已注册的 <see cref="IOpenApiDataScopePolicy"/>。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OpenApiDataScopeAttribute(string key) : Attribute
{
    /// <summary>范围策略键。</summary>
    public string Key { get; } = key;
}

/// <summary>内置范围策略键。</summary>
public static class OpenApiDataScopes
{
    /// <summary>端点不涉及受范围约束的数据:无需绑定;内核机构维度写成拒绝(空机构集合)。</summary>
    public const string None = "none";

    /// <summary>内置机构范围:绑定机构 Id(含下级),由内核全局过滤器与仓储写守卫强制。</summary>
    public const string Org = "org";
}
