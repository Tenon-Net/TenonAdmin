using Microsoft.AspNetCore.Mvc.Abstractions;

namespace TenonAdmin.Integration;

/// <summary>
/// 当前开放请求的接入应用上下文(实现契约 §4.2):授权阶段写入,动作与业务服务读取。
/// 默认实现以 <c>HttpContext.Items</c> 为载体(与内核 <c>HttpContextDataScopeContext</c> 同理,不用 AsyncLocal)。
/// </summary>
public interface IOpenAppContext
{
    /// <summary>当前请求是否已通过开放接口授权。</summary>
    bool IsOpenAppRequest { get; }

    /// <summary>接入应用身份;非开放请求访问时抛 <see cref="InvalidOperationException"/>。</summary>
    OpenAppIdentity Identity { get; }

    /// <summary>本请求的生效数据范围;非开放请求访问时抛 <see cref="InvalidOperationException"/>。</summary>
    OpenAppDataScope DataScope { get; }

    /// <summary>当前开放端点信息;非开放请求访问时抛 <see cref="InvalidOperationException"/>。</summary>
    OpenApiEndpointInfo Endpoint { get; }
}

/// <summary>开放端点清单条目(规范化路由权限码 + 范围声明)。</summary>
/// <param name="Permission">规范化路由权限码 <c>VERB:/api/open/v{n}/...</c>(授权即勾选它)</param>
/// <param name="HttpMethod">HTTP 方法(大写)</param>
/// <param name="Route">路由模板(原样)</param>
/// <param name="Version">版本段(如 <c>v1</c>)</param>
/// <param name="ScopeKey">声明的数据范围策略键</param>
/// <param name="Summary">摘要(来自 <c>[EndpointSummary]</c>,可空)</param>
/// <param name="Group">分组(控制器名)</param>
public sealed record OpenApiEndpointInfo(
    string Permission,
    string HttpMethod,
    string Route,
    string Version,
    string ScopeKey,
    string? Summary,
    string Group);

/// <summary>
/// 开放端点清单:由 MVC 动作描述构建(带 <see cref="OpenApiAttribute"/> 的动作),供授权比对、管理界面勾选授权与启动校验。
/// </summary>
public interface IOpenApiCatalog
{
    /// <summary>全部开放端点(按路由、方法排序)。</summary>
    IReadOnlyList<OpenApiEndpointInfo> Endpoints { get; }

    /// <summary>按 MVC 动作与实际请求方法查端点;不是开放端点返回 null。</summary>
    OpenApiEndpointInfo? Find(ActionDescriptor action, string httpMethod);

    /// <summary>权限码是否对应一个现存开放端点。</summary>
    bool Contains(string permission);
}
