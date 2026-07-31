using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// TOTP/MFA:自助绑定、恢复码、管理员清除、再次认证、高敏权限维护(ADR 0006)。
/// 不提供邀请 / InitGrant / 紧急授权端点。
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
    /// <summary>自助绑定启动(公开:账号 + 当前密码)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/bind/start")]
    public Task<TotpBindStartOutput> StartBind([FromBody] TotpBindStartInput input) =>
        enrollment.StartBindAsync(input);

    /// <summary>自助绑定完成(公开:挑战 + 首个 TOTP 码 → 恢复码)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/bind/complete")]
    public Task<TotpBindCompleteOutput> CompleteBind([FromBody] TotpBindCompleteInput input) =>
        enrollment.CompleteBindAsync(input);

    /// <summary>使用恢复码(公开:账密 + 恢复码 → 清 MFA 并吊销会话)。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/recovery")]
    public Task UseRecovery([FromBody] TotpRecoveryInput input) =>
        enrollment.UseRecoveryCodeAsync(input);

    /// <summary>TOTP 二次验证挑战校验。</summary>
    [AllowAnonymous]
    [HttpPost("auth/mfa/challenge/verify")]
    public async Task<MfaChallengeVerifyOutput> VerifyChallenge([FromBody] TotpChallengeVerifyInput input)
    {
        var userId = await totpChallenge.VerifyAndConsumeAsync(input.ChallengeId, input.Code);
        return new MfaChallengeVerifyOutput { UserId = userId };
    }

    /// <summary>短时再次认证:验 TOTP 或密码后写入 reauth 授予。</summary>
    [Authorize]
    [ActiveSession]
    [HttpPost("auth/reauth")]
    public async Task<bool> Reauth([FromBody] ReauthInput input)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        await reauth.VerifyAndGrantAsync(uid, input, currentUser.SessionId);
        return true;
    }

    /// <summary>管理员清除目标用户 MFA(目标之后自助重绑)。</summary>
    [Authorize]
    [RolePermission]
    [RequireReauth]
    [HttpPost("sys/mfa/clear")]
    public async Task ClearMfa([FromBody] TotpClearMfaInput input)
    {
        var uid = currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
        await enrollment.ClearUserMfaAsync(input.UserId, uid);
    }

    /// <summary>列出内核默认高敏权限 + 自定义追加项。</summary>
    [Authorize]
    [RolePermission]
    [HttpGet("sys/mfa/high-sensitivity")]
    public Task<HighSensitivityPermissionList> ListHighSensitivity() =>
        highSens.ListAsync();

    /// <summary>追加自定义高敏权限码。</summary>
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

    /// <summary>删除自定义高敏权限码。</summary>
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

/// <summary>TOTP 挑战校验入参。</summary>
public record TotpChallengeVerifyInput
{
    public string ChallengeId { get; init; } = "";
    public string Code { get; init; } = "";
}

/// <summary>挑战校验出参。</summary>
public record MfaChallengeVerifyOutput
{
    public long UserId { get; init; }
}
