using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>连通性检查的输出:当前接入应用身份与服务器时间(不含任何业务数据或秘密)。</summary>
/// <param name="AppCode">接入应用编码</param>
/// <param name="KeyId">本次使用的凭据公开标识</param>
/// <param name="ServerTime">服务器时间(ISO 8601,带偏移)</param>
public sealed record OpenAppWhoAmI(string AppCode, string KeyId, DateTimeOffset ServerTime);

/// <summary>
/// 模块内置的开放端点:对接方用它确认凭据可用、链路通畅,不读写任何业务数据(范围 <c>none</c>)。
/// 与所有开放端点一样<b>默认拒绝</b>:管理员在应用授权里勾选后才可调用。
/// </summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/open/v1/whoami")]
[OpenApi]
[OpenApiDataScope(OpenApiDataScopes.None)]
public class OpenAppWhoAmIController(IOpenAppContext app, TimeProvider time) : ControllerBase
{
    /// <summary>连通性检查:返回当前接入应用身份</summary>
    [HttpGet]
    [EndpointSummary("连通性检查:返回当前接入应用身份")]
    [ProducesResponseType(typeof(Result<OpenAppWhoAmI>), StatusCodes.Status200OK)]
    public Result<OpenAppWhoAmI> Get() =>
        Result<OpenAppWhoAmI>.Ok(new OpenAppWhoAmI(app.Identity.AppCode, app.Identity.KeyId, time.GetUtcNow()));
}
