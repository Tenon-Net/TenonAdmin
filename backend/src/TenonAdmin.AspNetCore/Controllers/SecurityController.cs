using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 安全基线 / Level3 预检端点(等保三级应用安全一期)。
/// 高敏感:默认需路由权限;超管(sadm)放行。结果不含密钥/连接串密码/TOTP 种子。
/// <para>内核不宣称「已通过等保三级」——报告仅反映已实现能力版本与配置态势。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/security")]
[Module("Security")]
public class SecurityController(ILevel3PrecheckService precheck) : ControllerBase
{
    /// <summary>
    /// Level3 一期安全基线预检报告(机器可读)。
    /// 含 capabilityVersion、checks[]、unimplementedMandates[]、overallCompliantForPhase1。
    /// </summary>
    [HttpGet("baseline")]
    [RolePermission]
    public async Task<Result<Level3PrecheckResult>> Baseline(CancellationToken cancellationToken) =>
        Result<Level3PrecheckResult>.Ok(await precheck.RunAsync(cancellationToken));

    /// <summary>
    /// 与 <see cref="Baseline"/> 同义别名(<c>/api/v1/sys/level3/precheck</c> 风格路径的兼容入口挂在同控制器下)。
    /// </summary>
    [HttpGet("/api/v1/sys/level3/precheck")]
    [RolePermission]
    public Task<Result<Level3PrecheckResult>> Level3Precheck(CancellationToken cancellationToken) =>
        Baseline(cancellationToken);
}
