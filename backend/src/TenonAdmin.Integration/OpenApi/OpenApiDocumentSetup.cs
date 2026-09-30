using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace TenonAdmin.Integration;

/// <summary>
/// 独立的开放接口 OpenAPI 文档(实现契约 §4.7):每个版本一份 <c>open-{version}</c>,只收录 ApiExplorer 分组为该名的开放端点
/// (<see cref="OpenApiConvention"/> 设置),并写明真实认证方式与通用约定。开发环境由内核 <c>MapOpenApi()</c> 在
/// <c>/openapi/open-v1.json</c> 匿名提供;生产环境只能经受权限保护的管理接口下载。
/// </summary>
internal static class OpenApiDocumentSetup
{
    /// <summary>安全方案名。</summary>
    public const string SecuritySchemeName = "ApiKey";

    /// <summary>文档名(<c>open-v1</c>)。</summary>
    public static string DocumentName(string version) => OpenApiConvention.GroupNamePrefix + version.ToLowerInvariant();

    public static void AddOpenApiDocuments(IServiceCollection services, IntegrationOptions options)
    {
        foreach (var version in options.OpenApi.Versions)
        {
            var normalized = version.ToLowerInvariant();
            var name = DocumentName(normalized);
            services.AddOpenApi(name, o =>
            {
                // 默认规则会把未分组(后台)端点也收进每份文档;开放文档只收本分组
                o.ShouldInclude = description => string.Equals(description.GroupName, name, StringComparison.Ordinal)
                                                   && OpenApiCatalog.IsOpen(description.ActionDescriptor);
                o.AddDocumentTransformer((document, _, _) =>
                {
                    Describe(document, normalized);
                    return Task.CompletedTask;
                });
                o.AddOperationTransformer((operation, _, _) =>
                {
                    DescribeOperation(operation);
                    return Task.CompletedTask;
                });
            });
        }
    }

    private static void Describe(OpenApiDocument document, string version)
    {
        document.Info = new OpenApiInfo
        {
            Title = $"开放接口 {version}",
            Version = version,
            Description = string.Join('\n',
                "面向已授权接入应用的业务接口,路由前缀 `/api/open/" + version + "/`;同一主版本内保持兼容,破坏性变更进入新版本。",
                "",
                "**认证**:每个请求在请求头 `" + OpenAppAuthenticationDefaults.HeaderName + "` 中携带接入凭据(形如 `tna_{keyId}.{secret}`),必须经 HTTPS 传输;凭据不能放在 URL 中。",
                "",
                "**统一返回**:`{ code, msgKey, args, message, data }`,`code = 0` 表示成功。认证失败 HTTP 401(49001);未授权或未绑定数据范围 403(49002/49003);限流 429(49005/49006,附 `Retry-After`);请求参数不合法 400(49007,`args.errors` 为字段错误);服务端错误 500(50000,`args.traceId`)。业务失败沿用 HTTP 200 + 非 0 业务码。",
                "",
                "**分页**:入参 `current`(从 1 起)、`size`(1–" + OpenApiPageInput.MaxSize + ");出参 `{ current, size, total, pages, items }`。",
                "",
                "**时间**:ISO 8601,带时区偏移(如 `2026-09-27T10:00:00+08:00`)。",
                "",
                "**追踪**:每个响应带 `" + OpenApiHeaders.TraceId + "`;调用方可传 `" + OpenApiHeaders.RequestId + "`(≤64 位 `[A-Za-z0-9._:-]`),原样回显并与调用记录关联。"),
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = OpenAppAuthenticationDefaults.HeaderName,
            Description = "接入凭据(由管理员在「接入应用」中发放,仅展示一次)。",
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = [] },
        ];
    }

    private static void DescribeOperation(OpenApiOperation operation)
    {
        operation.Parameters ??= [];
        if (!operation.Parameters.Any(p => string.Equals(p.Name, OpenApiHeaders.RequestId, StringComparison.OrdinalIgnoreCase)))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = OpenApiHeaders.RequestId,
                In = ParameterLocation.Header,
                Required = false,
                Description = "调用方请求标识(可选,原样回显)。",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 64 },
            });
        }

        operation.Responses ??= new OpenApiResponses();
        foreach (var (status, description) in new[]
        {
            ("400", "请求参数不合法(49007)。"),
            ("401", "接入凭据缺失、无效、已撤销、已过期或应用已停用(49001)。"),
            ("403", "应用未被授予该接口(49002)或未绑定其数据范围(49003)。"),
            ("429", "调用过于频繁(49005)或认证失败过多(49006),见 Retry-After。"),
            ("500", "服务端错误(50000),args.traceId 为追踪标识。"),
        })
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = description });
    }
}
