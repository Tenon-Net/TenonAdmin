using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放接口声明的启动校验(实现契约 §4.5):任一违规即拒绝启动,把消费者的声明错误挡在上线之前。
/// 规则见 <see cref="OpenApiConventionRules"/>。
/// </summary>
internal sealed class OpenApiStartupValidator(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var actions = services.GetService<IActionDescriptorCollectionProvider>();
        if (actions is null) return;   // 无 MVC 的宿主(纯后台 Worker)没有开放端点可校验

        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<OpenApiDataScopePolicyRegistry>();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policyNames = actions.ActionDescriptors.Items
            .SelectMany(a => a.EndpointMetadata.OfType<IAuthorizeData>())
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var policies = new Dictionary<string, AuthorizationPolicy?>(StringComparer.Ordinal);
        foreach (var name in policyNames)
            policies[name!] = await policyProvider.GetPolicyAsync(name!);

        var errors = OpenApiConventionRules.Validate(actions.ActionDescriptors.Items, registry.IsDeclarable,
            name => policies.GetValueOrDefault(name),
            await policyProvider.GetDefaultPolicyAsync(),
            await policyProvider.GetFallbackPolicyAsync());
        if (errors.Count > 0)
            throw new InvalidOperationException("开放接口声明不合法,拒绝启动:\n- " + string.Join("\n- ", errors));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// 开放接口声明规则(纯函数,便于单测):
/// <list type="bullet">
/// <item>位于 <c>api/open/</c> 下的动作必须带 <see cref="OpenApiAttribute"/>;带它的动作路由必须以 <c>api/open/v{n}/</c> 开头;</item>
/// <item>不带它的动作不得指定 <see cref="OpenAppAuthenticationDefaults.Scheme"/> 认证方案:接入凭据能进来,却没有应用授权与范围检查;</item>
/// <item>必须显式声明 HTTP 方法,必须声明已注册的数据范围(或 <see cref="OpenApiDataScopes.None"/>);</item>
/// <item>不得混用后台用户授权(<c>[RolePermission]</c>/<c>[ActiveSession]</c>/<c>[RequireReauth]</c>)或 <c>[AllowAnonymous]</c>;</item>
/// <item>返回类型必须是 <c>Result&lt;T&gt;</c>(与运行时信封一致,文档才准确),<c>T</c> 不得(直接或经属性)包含持久化实体。</item>
/// </list>
/// </summary>
public static class OpenApiConventionRules
{
    /// <summary>校验全部动作,返回违规描述(空表示通过)。</summary>
    public static IReadOnlyList<string> Validate(
        IEnumerable<ActionDescriptor> actions,
        Func<string?, bool> isDeclarableScope,
        Func<string, AuthorizationPolicy?>? resolvePolicy = null,
        AuthorizationPolicy? defaultPolicy = null,
        AuthorizationPolicy? fallbackPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(isDeclarableScope);
        var errors = new List<string>();
        foreach (var action in actions.OfType<ControllerActionDescriptor>())
        {
            var template = (action.AttributeRouteInfo?.Template ?? "").TrimStart('/');
            var name = $"{action.ControllerTypeInfo.FullName}.{action.MethodInfo.Name}";
            var isOpen = OpenApiCatalog.IsOpen(action);
            var underOpenPrefix = template.StartsWith("api/open/", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(template, "api/open", StringComparison.OrdinalIgnoreCase);

            if (!isOpen)
            {
                if (underOpenPrefix)
                    errors.Add($"{name}:路由 {template} 位于 api/open/ 下,但未标注 [OpenApi]。");
                if (NamesOpenAppScheme(action.EndpointMetadata, resolvePolicy, defaultPolicy, fallbackPolicy))
                    errors.Add($"{name}:未标注 [OpenApi] 却指定了 {OpenAppAuthenticationDefaults.Scheme} 认证方案——接入凭据能调用它," +
                               "却没有应用授权与数据范围检查;开放接口请标注 [OpenApi]。");
                continue;
            }

            if (OpenApiCatalog.VersionOf(template) is null)
                errors.Add($"{name}:开放端点路由必须以 api/open/v{{n}}/ 开头,实际为 {template}。");
            if (OpenApiCatalog.MethodsOf(action).Count == 0)
                errors.Add($"{name}:开放端点必须显式声明 HTTP 方法([HttpGet]/[HttpPost]...)。");

            var scope = OpenApiCatalog.ScopeKeyOf(action);
            if (string.IsNullOrWhiteSpace(scope))
                errors.Add($"{name}:开放端点必须声明 [OpenApiDataScope](不涉及范围数据时声明 \"{OpenApiDataScopes.None}\")。");
            else if (!isDeclarableScope(scope))
                errors.Add($"{name}:数据范围 \"{scope}\" 没有注册对应的 IOpenApiDataScopePolicy。");

            var metadata = action.EndpointMetadata;
            if (metadata.OfType<RolePermissionAttribute>().Any() || metadata.OfType<ActiveSessionAttribute>().Any()
                || metadata.OfType<RequireReauthAttribute>().Any())
                errors.Add($"{name}:开放端点不得挂后台用户授权特性([RolePermission]/[ActiveSession]/[RequireReauth])。");
            if (metadata.OfType<IAllowAnonymous>().Any())
                errors.Add($"{name}:开放端点不得标注 [AllowAnonymous]。");

            var outputError = CheckOutputType(action.MethodInfo.ReturnType);
            if (outputError is not null)
                errors.Add($"{name}:{outputError}");
        }
        return errors;
    }

    /// <summary>返回类型检查;通过返回 null。</summary>
    public static string? CheckOutputType(Type returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        var type = Unwrap(returnType);
        if (type is null) return null;   // void / Task:无响应体
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Result<>))
            return "开放端点必须返回 Result<T>(与运行时统一信封一致,文档才准确),不能返回 IActionResult/object 或裸 DTO;无响应体时返回 Task。";
        return FindOutputViolation(type.GetGenericArguments()[0], []);
    }

    /// <summary>授权元数据(<c>[Authorize]</c> 或策略)是否点名接入应用认证方案。</summary>
    private static bool NamesOpenAppScheme(
        IEnumerable<object> metadata,
        Func<string, AuthorizationPolicy?>? resolvePolicy,
        AuthorizationPolicy? defaultPolicy,
        AuthorizationPolicy? fallbackPolicy)
    {
        var authorization = metadata.OfType<IAuthorizeData>().ToList();
        var directPolicies = metadata.OfType<AuthorizationPolicy>().ToList();
        return authorization.Any(a => Names(a.AuthenticationSchemes?.Split(',')))
               || directPolicies.Any(p => Names(p.AuthenticationSchemes))
               || resolvePolicy is not null && authorization
                   .Any(a => !string.IsNullOrWhiteSpace(a.Policy) && Names(resolvePolicy(a.Policy!)?.AuthenticationSchemes))
               || authorization.Any(a => string.IsNullOrWhiteSpace(a.Policy) && string.IsNullOrWhiteSpace(a.Roles)
                                         && string.IsNullOrWhiteSpace(a.AuthenticationSchemes))
                   && Names(defaultPolicy?.AuthenticationSchemes)
               || authorization.Count == 0 && directPolicies.Count == 0 && !metadata.OfType<IAllowAnonymous>().Any()
                   && Names(fallbackPolicy?.AuthenticationSchemes);
    }

    private static bool Names(IEnumerable<string>? schemes) =>
        schemes?.Any(s => string.Equals(s.Trim(), OpenAppAuthenticationDefaults.Scheme, StringComparison.Ordinal)) == true;

    /// <summary>剥掉 Task/ValueTask/ActionResult 外壳;无响应体返回 null。</summary>
    private static Type? Unwrap(Type type)
    {
        while (true)
        {
            if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask)) return null;
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(Task<>) || def == typeof(ValueTask<>) || def == typeof(ActionResult<>))
                {
                    type = type.GetGenericArguments()[0];
                    continue;
                }
            }
            return type;
        }
    }

    private static string? FindOutputViolation(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type)) return null;
        if (typeof(PrimaryId).IsAssignableFrom(type))
            return $"输出模型不能包含持久化实体 {type.FullName};请定义只含可公开字段的 DTO。";
        if (type == typeof(object))
            return "输出模型不能包含 object;其运行时类型无法在启动时校验,请定义明确的输出 DTO。";
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan)
            || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return null;

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return FindOutputViolation(nullable, visited);
        if (type.IsArray) return FindOutputViolation(type.GetElementType()!, visited);
        if (type.IsGenericType)
            foreach (var arg in type.GetGenericArguments())
                if (FindOutputViolation(arg, visited) is { } fromArg) return fromArg;
        if (typeof(IEnumerable).IsAssignableFrom(type))
            return type.IsGenericType
                ? null   // 已检查过泛型元素
                : $"输出模型不能包含无元素类型的 {type.FullName};请使用泛型集合与明确的输出 DTO。";
        if (type.IsInterface || type.IsAbstract)
            return $"输出模型不能包含无法静态验证运行时类型的 {type.FullName};请使用明确的输出 DTO。";

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetIndexParameters().Length == 0 && FindOutputViolation(property.PropertyType, visited) is { } fromProperty)
                return fromProperty;
        return null;
    }
}
