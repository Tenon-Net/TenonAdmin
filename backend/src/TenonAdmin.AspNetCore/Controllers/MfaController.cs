using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// TOTP/MFA 绑定、恢复、邀请、再次认证端点(等保三级应用安全一期)。
/// 权限码 = 规范化路由;高敏默认集合已纳入 MFA 管理写路径。
/// </summary>
[ApiController]
[Route("api/v1")]
public class MfaController(
    IMfaEnrollmentService enrollment,
    IMfaChallengeService totpChallenge,
    IReauthService reauth,
    IHighSensitivityPermissionService highSens,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>管理员为目标用户发放 TOTP 绑定邀请(须已绑 TOTP + 短时再认证)。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpPost("sys/mfa/invite")]
    public async Task<TotpBindInviteOutput> IssueInvite([FromBody] MfaInviteInput input)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        return await enrollment.IssueBindInviteAsync(input.UserId, uid);
    }

    /// <summary>撤销未使用绑定邀请。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpDelete("sys/mfa/invite/{id:long}")]
    public async Task RevokeInvite(long id)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        await enrollment.RevokeBindInviteAsync(id, uid);
    }

    /// <summary>绑定启动(公开:持邀请/InitGrant + 当前密码)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/bind/start")]
    public Task<TotpBindStartOutput> StartBind([FromBody] TotpBindStartInput input) =>
        enrollment.StartBindAsync(input);

    /// <summary>绑定完成(公开:挑战 + 首个 TOTP 码 → 恢复码)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/bind/complete")]
    public Task<TotpBindCompleteOutput> CompleteBind([FromBody] TotpBindCompleteInput input) =>
        enrollment.CompleteBindAsync(input);

    /// <summary>使用恢复码(公开:账密 + 恢复码 → 清 MFA 并吊销会话)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/recovery")]
    public Task UseRecovery([FromBody] TotpRecoveryInput input) =>
        enrollment.UseRecoveryCodeAsync(input);

    /// <summary>TOTP 二次验证挑战校验(登录下半场接线钩子)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/challenge/verify")]
    public async Task<MfaChallengeVerifyOutput> VerifyChallenge([FromBody] TotpChallengeVerifyInput input)
    {
        var userId = await totpChallenge.VerifyAndConsumeAsync(input.ChallengeId, input.Code);
        return new MfaChallengeVerifyOutput { UserId = userId };
    }

    /// <summary>短时再次认证:验 TOTP 或密码后写入 reauth 授予(绑定当前 sid)。</summary>
    [Authorize]
    [ActiveSession]
    [HttpPost("auth/reauth")]
    public async Task<bool> Reauth([FromBody] ReauthInput input)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        await reauth.VerifyAndGrantAsync(uid, input, currentUser.SessionId);
        return true;
    }

    /// <summary>超级管理员 MFA 重置(peer 或已登录 emergency;需 reauth)。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpPost("sys/mfa/reset")]
    public Task<TotpResetOutput> ResetSuperAdmin([FromBody] TotpSuperAdminResetInput input) =>
        enrollment.ResetSuperAdminMfaAsync(input, currentUser.UserId);

    /// <summary>
    /// 唯一超管紧急 MFA 恢复(匿名):无会话时凭 EmergencyGrant + 账密重置并签发重绑邀请。
    /// 走认证限流分区;错误不区分账户是否存在。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/emergency-reset")]
    public Task<TotpResetOutput> EmergencyReset([FromBody] TotpEmergencyResetInput input) =>
        enrollment.EmergencyResetSoleSuperAdminAsync(input);

    /// <summary>列出内核默认高敏权限(只读)+ 消费者自定义追加项。</summary>
    [Authorize]
    [RolePermission]
    [HttpGet("sys/mfa/high-sensitivity")]
    public Task<HighSensitivityPermissionList> ListHighSensitivity() =>
        highSens.ListAsync();

    /// <summary>追加自定义高敏权限码(不可删默认集;Level3 须 reauth)。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpPost("sys/mfa/high-sensitivity")]
    public async Task<HighSensitivityPermissionItem> AddHighSensitivity(
        [FromBody] HighSensitivityPermissionInput input)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        var row = await highSens.AddAsync(input, uid);
        return new HighSensitivityPermissionItem
        {
            Id = row.Id,
            PermissionCode = row.PermissionCode,
            Remark = row.Remark,
        };
    }

    /// <summary>删除自定义高敏权限码(禁止删内核默认)。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpDelete("sys/mfa/high-sensitivity/{id:long}")]
    public async Task DeleteHighSensitivity(long id)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        await highSens.DeleteAsync(id, uid);
    }
}

/// <summary>发放邀请入参。</summary>
public record MfaInviteInput
{
    public long UserId { get; init; }
}

/// <summary>TOTP 挑战校验入参。</summary>
public record TotpChallengeVerifyInput
{
    public string ChallengeId { get; init; } = "";
    public string Code { get; init; } = "";
}

/// <summary>挑战校验出参(供 Auth 接线与集成测试)。</summary>
public record MfaChallengeVerifyOutput
{
    public long UserId { get; init; }
}
