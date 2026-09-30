using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>开放调用记录查询(后台用户接口,<c>[RolePermission]</c>)。记录不含正文与认证头,只向获授权的后台用户开放。</summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/v1/integration/inbound-log")]
[Module("Integration")]
public class IntegrationInboundLogController(IInboundLogService logs) : ControllerBase
{
    /// <summary>分页查询开放调用记录(新在前)</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<InboundLogView>>> Page([FromQuery] InboundLogPageInput input, CancellationToken cancellationToken) =>
        Result<PagedList<InboundLogView>>.Ok(await logs.PageAsync(input, cancellationToken));
}
