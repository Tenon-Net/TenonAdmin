using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.SqlSugar;
using TenonApp.Modules;

namespace TenonApp.Integrations;

/// <summary>开放输出模型:只放可以给第三方看的字段(实体上的审计列、机构 Id 都不外露)。时间带偏移。</summary>
public sealed record SampleDocDto(long Id, string Title, DateTimeOffset CreatedAt);

/// <summary>开放输入模型。</summary>
public sealed record SampleDocOpenInput([Required, StringLength(128)] string Title);

/// <summary>
/// 开放接口示例:第三方系统用接入凭据(请求头 <c>X-Api-Key</c>)调用。认证、默认拒绝的端点授权、数据范围隔离、
/// 调用记录与限流都由框架完成,这里只写 DTO 和业务调用。
/// <para>
/// 使用前在后台「系统集成 → 接入应用」里新建应用、发放凭据、勾选下面两个端点,并为「机构」范围绑定可见机构。
/// 路由必须以 <c>api/open/v{n}/</c> 开头;同一版本内只做兼容变更,不兼容的改动放到新版本号下。
/// </para>
/// </summary>
[ApiController]
[Route("api/open/v1/sample-docs")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.Org)]   // 按应用绑定的机构(含下级)过滤,由内核全局过滤器与仓储写守卫强制
public class SampleDocOpenController(IRepository<SampleDoc> docs, ISampleDocService service) : ControllerBase
{
    /// <summary>分页查询应用可见的示例文档</summary>
    [HttpGet]
    [EndpointSummary("分页查询可见的示例文档")]
    public async Task<Result<PagedList<SampleDocDto>>> Page([FromQuery] OpenApiPageInput input) =>
        Result<PagedList<SampleDocDto>>.Ok(await docs.AsQueryable().OrderBy(d => d.Id).ToOpenPagedListAsync(input, Map));

    /// <summary>新建示例文档(机构锚点取接入应用的归属机构)</summary>
    [HttpPost]
    [EndpointSummary("新建示例文档")]
    public async Task<Result<long>> Create(SampleDocOpenInput input) =>
        Result<long>.Ok(await service.CreateAsync(input.Title));

    private static SampleDocDto Map(SampleDoc doc) =>
        new(doc.Id, doc.Title, new DateTimeOffset(doc.CreateTime, TimeZoneInfo.Local.GetUtcOffset(doc.CreateTime)));
}
