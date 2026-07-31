using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 可选安全态势诊断端点(ADR 0006:非等保测评产品路径)。
/// 高敏感:默认需路由权限;超管(sadm)放行。结果不含密钥/连接串密码/TOTP 种子。
/// <para>默认菜单不展示;仅运维/调试或历史 Profile=Level3 过渡诊断用。内核不宣称通过等保。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/security")]
[Module("Security")]
public class SecurityController(ISecurityBaselinePrecheckService precheck) : ControllerBase
{
    /// <summary>
    /// 可选安全配置态势报告(机器可读,历史 capability 字段仍兼容)。
    /// </summary>
    [HttpGet("baseline")]
    [RolePermission]
    public async Task<Result<SecurityBaselinePrecheckResult>> Baseline(CancellationToken cancellationToken) =>
        Result<SecurityBaselinePrecheckResult>.Ok(await precheck.RunAsync(cancellationToken));

    /// <summary>
    /// 与 <see cref="Baseline"/> 同义别名(历史路径兼容;不作为产品主入口)。
    /// </summary>
    [HttpGet("/api/v1/sys/level3/precheck")]
    [RolePermission]
    public Task<Result<SecurityBaselinePrecheckResult>> LegacyLevel3Precheck(CancellationToken cancellationToken) =>
        Baseline(cancellationToken);
}
