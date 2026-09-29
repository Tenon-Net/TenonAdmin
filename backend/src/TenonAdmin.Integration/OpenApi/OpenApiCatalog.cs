using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using TenonAdmin.AspNetCore;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenApiCatalog"/> 默认实现:从 MVC 动作描述构建开放端点清单。权限码与内核授权同一真源
/// <see cref="PermissionCode.Build"/>;动作集合版本变化时自动重建(单例,线程安全)。
/// </summary>
public partial class OpenApiCatalog(IActionDescriptorCollectionProvider actions) : IOpenApiCatalog
{
    private readonly object _gate = new();
    private Snapshot? _snapshot;

    [GeneratedRegex("^api/open/(v[0-9]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    /// <inheritdoc />
    public IReadOnlyList<OpenApiEndpointInfo> Endpoints => Current.Endpoints;

    /// <inheritdoc />
    public virtual OpenApiEndpointInfo? Find(ActionDescriptor action, string httpMethod)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Current.ByAction.TryGetValue(action.Id, out var list)
            ? list.FirstOrDefault(e => string.Equals(e.HttpMethod, httpMethod, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <inheritdoc />
    public virtual bool Contains(string permission) => Current.Permissions.Contains(permission);

    private Snapshot Current
    {
        get
        {
            var collection = actions.ActionDescriptors;
            var snapshot = _snapshot;
            if (snapshot is not null && snapshot.Version == collection.Version) return snapshot;
            lock (_gate)
            {
                if (_snapshot is null || _snapshot.Version != collection.Version)
                    _snapshot = Build(collection);
                return _snapshot;
            }
        }
    }

    private static Snapshot Build(ActionDescriptorCollection collection)
    {
        var byAction = new Dictionary<string, List<OpenApiEndpointInfo>>(StringComparer.Ordinal);
        foreach (var action in collection.Items.OfType<ControllerActionDescriptor>())
        {
            if (!IsOpen(action)) continue;
            var endpoints = Describe(action);
            if (endpoints.Count > 0) byAction[action.Id] = endpoints;
        }

        var all = byAction.Values.SelectMany(l => l)
            .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HttpMethod, StringComparer.Ordinal)
            .ToList();
        return new Snapshot(collection.Version, all, byAction, all.Select(e => e.Permission).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>动作是否带 <see cref="OpenApiAttribute"/>(类或方法)。</summary>
    public static bool IsOpen(ActionDescriptor action) =>
        action.EndpointMetadata.OfType<OpenApiAttribute>().Any();

    /// <summary>生效的范围声明(方法级覆盖类级:端点元数据按类→方法顺序追加,取最后一个)。</summary>
    public static string? ScopeKeyOf(ActionDescriptor action) =>
        action.EndpointMetadata.OfType<OpenApiDataScopeAttribute>().LastOrDefault()?.Key;

    /// <summary>动作声明的 HTTP 方法(大写,去重);未声明返回空集合。</summary>
    public static IReadOnlyList<string> MethodsOf(ActionDescriptor action) =>
        (action.ActionConstraints ?? [])
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(c => c.HttpMethods)
            .Select(m => m.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>路由里的版本段;不符合 <c>api/open/v{n}/</c> 返回 null。</summary>
    public static string? VersionOf(string? template)
    {
        var match = VersionPattern().Match((template ?? "").TrimStart('/'));
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static List<OpenApiEndpointInfo> Describe(ControllerActionDescriptor action)
    {
        var template = action.AttributeRouteInfo?.Template ?? "";
        var version = VersionOf(template) ?? "";
        var scope = ScopeKeyOf(action) ?? "";
        var summary = action.EndpointMetadata.OfType<IEndpointSummaryMetadata>().LastOrDefault()?.Summary;
        return MethodsOf(action)
            .Select(method => new OpenApiEndpointInfo(
                PermissionCode.Build(method, template), method, template, version, scope, summary, action.ControllerName))
            .ToList();
    }

    private sealed record Snapshot(
        int Version,
        IReadOnlyList<OpenApiEndpointInfo> Endpoints,
        IReadOnlyDictionary<string, List<OpenApiEndpointInfo>> ByAction,
        IReadOnlySet<string> Permissions);
}
