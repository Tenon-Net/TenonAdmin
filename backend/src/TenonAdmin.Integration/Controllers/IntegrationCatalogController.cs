using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>范围策略摘要(管理界面选择范围维度)。</summary>
/// <param name="Key">策略键</param>
/// <param name="Name">显示名</param>
public sealed record OpenApiScopePolicyInfo(string Key, string Name);

/// <summary>
/// 开放接口目录(后台用户接口):开放端点清单(授权勾选的来源)、范围策略与候选值、开放文档下载。全部 <c>[RolePermission]</c>。
/// </summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/v1/integration/catalog")]
[Module("Integration")]
public class IntegrationCatalogController(
    IOpenApiCatalog catalog,
    OpenApiDataScopePolicyRegistry policies,
    IntegrationOptions options) : ControllerBase
{
    /// <summary>开放端点清单(权限码、方法、路由、版本、范围声明)</summary>
    [HttpGet("endpoints")]
    [RolePermission]
    public Result<IReadOnlyList<OpenApiEndpointInfo>> Endpoints() =>
        Result<IReadOnlyList<OpenApiEndpointInfo>>.Ok(catalog.Endpoints);

    /// <summary>已注册的范围策略</summary>
    [HttpGet("scopes")]
    [RolePermission]
    public Result<IReadOnlyList<OpenApiScopePolicyInfo>> Scopes() =>
        Result<IReadOnlyList<OpenApiScopePolicyInfo>>.Ok(policies.Policies
            .Select(p => new OpenApiScopePolicyInfo(p.Key, p.Name))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// 下载开放接口 OpenAPI 文档(生产环境的受控获取方式:内核只在开发环境匿名映射 <c>/openapi/*.json</c>)。
    /// 返回 JSON 文件,交给对接方生成客户端或导入调试工具。
    /// </summary>
    [HttpGet("open-api/{version}")]
    [RolePermission]
    [Produces("application/json")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> OpenApiDocument(string version, CancellationToken cancellationToken)
    {
        var normalized = (version ?? "").Trim().ToLowerInvariant();
        IntegrationErrorCode.ThrowIf(!options.OpenApi.Versions.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)),
            IntegrationErrorCode.OpenApiVersionNotFound, new Dictionary<string, object?> { ["version"] = version });

        var name = OpenApiDocumentSetup.DocumentName(normalized);
        var provider = HttpContext.RequestServices.GetRequiredKeyedService<IOpenApiDocumentProvider>(name);
        var document = await provider.GetOpenApiDocumentAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await document.SerializeAsJsonAsync(buffer, OpenApiSpecVersion.OpenApi3_1, cancellationToken);
        return File(buffer.ToArray(), "application/json", name + ".json");
    }

    /// <summary>范围候选值(树形数据带父值)</summary>
    [HttpGet("scopes/{key}/options")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<OpenApiScopeOption>>> ScopeOptions(string key, [FromQuery] string? keyword, CancellationToken cancellationToken)
    {
        var policy = policies.Find(key);
        IntegrationErrorCode.ThrowIf(policy is null, IntegrationErrorCode.ScopePolicyUnknown,
            new Dictionary<string, object?> { ["scope"] = key });
        return Result<IReadOnlyList<OpenApiScopeOption>>.Ok(await policy!.ListOptionsAsync(keyword, cancellationToken));
    }
}
