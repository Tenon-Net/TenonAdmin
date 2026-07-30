using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMfaEnrollmentService"/> 默认实现。
/// 绑定邀请 15 分钟一次性;绑定必须验当前密码;seed 仅经 <see cref="ISecretProtector"/> 加密;
/// 恢复码 10 个只存哈希;使用后吊销全部会话并清 MFA。
/// </summary>
public class MfaEnrollmentService(
    IRepository<SysUser> users,
    IRepository<SysTotpBindInvite> invites,
    IRepository<SysTotpRecoveryCode> recoveryCodes,
    IPasswordHasher hasher,
    ISecretProtector protector,
    ITotpService totp,
    ISessionService sessions,
    ICacheProvider cache,
    AdminSecurityOptions security,
    ILogger<MfaEnrollmentService> logger,
    TimeProvider? time = null,
    ISecurityProfileAccessor? profile = null,
    IPermissionProvider? permissions = null,
    ILevel3DeployGrantStore? deployGrants = null) : IMfaEnrollmentService
{
    // deployGrants 在 Level3 下必须可用;非 Level3 不走 Init/Emergency 部署授权路径
    private const int RecoveryCodeCount = 10;
    private const int TokenBytes = 32;
    private static readonly TimeSpan BindChallengeTtl = TimeSpan.FromMinutes(10);

    private DateTime Now => (time ?? TimeProvider.System).GetLocalNow().DateTime;

    private bool IsLevel3 =>
        profile?.IsLevel3 == true || security.Profile == SecurityProfile.Level3;

    /// <inheritdoc />
    public virtual Task<TotpBindInviteOutput> IssueBindInviteAsync(long targetUserId, long issuedByUserId) =>
        IssueBindInviteCoreAsync(targetUserId, issuedByUserId, systemEmergency: false);

    /// <summary>
    /// 仅紧急恢复路径在已校验 EmergencyGrant 后调用:允许系统 issuer(0)。
    /// 普通入口不得调用本方法绕过发起人校验。
    /// </summary>
    protected virtual Task<TotpBindInviteOutput> IssueSystemEmergencyBindInviteAsync(long targetUserId) =>
        IssueBindInviteCoreAsync(targetUserId, issuedByUserId: 0, systemEmergency: true);

    /// <summary>发邀请核心:人类发起人须启用+TOTP+超管或持有邀请权限码;系统 issuer 仅紧急恢复。</summary>
    protected virtual async Task<TotpBindInviteOutput> IssueBindInviteCoreAsync(
        long targetUserId, long issuedByUserId, bool systemEmergency)
    {
        var target = await users.GetByIdAsync(targetUserId);
        AdminException.ThrowIf(target is null, ErrorCode.UserNotFound);

        if (systemEmergency)
        {
            // 调用方必须已完成 EmergencyGrant + 唯一超管校验(见 ResetSuperAdminMfaAsync)
            logger.LogCritical(
                "SECURITY HIGHEST: system emergency TOTP bind invite for userId={Target}",
                targetUserId);
        }
        else
        {
            // 普通路径禁止 issuedBy=0(不得伪装系统身份)
            AdminException.ThrowIf(issuedByUserId <= 0, ErrorCode.NoPermission);
            var issuer = await users.GetByIdAsync(issuedByUserId);
            AdminException.ThrowIf(issuer is null || !issuer.Enabled, ErrorCode.NoPermission);
            if (IsLevel3)
            {
                AdminException.ThrowIf(!issuer.TotpEnabled, ErrorCode.TotpNotBound);
                await EnsureOperatorAuthorizedAsync(issuer, HighSensitivityPermissions.MfaInvite);
            }
        }

        // 同用户未消费/未撤销的旧邀请一律作废
        var old = await invites.AsQueryable()
            .Where(i => i.UserId == targetUserId && i.ConsumedAt == null && i.RevokedAt == null)
            .ToListAsync();
        foreach (var o in old)
        {
            o.RevokedAt = Now;
            await invites.UpdateAsync(o);
        }

        var plain = CreateBearerToken();
        var minutes = Math.Max(1, security.Level3.BindInviteTtlMinutes);
        var entity = new SysTotpBindInvite
        {
            TokenHash = SecretHash.Hash(plain),
            UserId = targetUserId,
            ExpiresAt = Now.AddMinutes(minutes),
            IssuedByUserId = issuedByUserId,
        };
        await invites.InsertAsync(entity);

        logger.LogInformation(
            "TOTP bind invite issued: targetUserId={TargetUserId} issuedBy={IssuedBy} expiresAt={ExpiresAt}",
            targetUserId, issuedByUserId, entity.ExpiresAt);

        return new TotpBindInviteOutput
        {
            Token = plain,
            UserId = targetUserId,
            ExpiresAt = entity.ExpiresAt,
        };
    }

    /// <summary>操作人须为超管,或持有指定权限码(可替换 IPermissionProvider)。</summary>
    protected virtual async Task EnsureOperatorAuthorizedAsync(SysUser operatorUser, string permissionCode)
    {
        if (operatorUser.IsSuperAdmin) return;
        if (permissions is null)
            throw new AdminException(ErrorCode.NoPermission);

        var codes = await permissions.GetPermissionCodesAsync(operatorUser.Id);
        AdminException.ThrowIf(
            !codes.Contains(permissionCode, StringComparer.OrdinalIgnoreCase),
            ErrorCode.NoPermission);
    }

    /// <summary>Level3 下发/撤邀请:操作人须启用、已绑 TOTP,且有对应路由权限。</summary>
    protected virtual async Task EnsureMfaAdminOperatorAsync(long operatorUserId, string permissionCode)
    {
        AdminException.ThrowIf(operatorUserId <= 0, ErrorCode.NoPermission);
        var op = await users.GetByIdAsync(operatorUserId);
        AdminException.ThrowIf(op is null || !op.Enabled, ErrorCode.NoPermission);
        if (IsLevel3)
        {
            AdminException.ThrowIf(!op.TotpEnabled, ErrorCode.TotpNotBound);
            await EnsureOperatorAuthorizedAsync(op, permissionCode);
        }
    }

    /// <inheritdoc />
    public virtual async Task RevokeBindInviteAsync(long inviteId, long operatorUserId)
    {
        // 与发放路径同级授权:禁止控制器外伪造 operator 直接撤销
        await EnsureMfaAdminOperatorAsync(operatorUserId, HighSensitivityPermissions.MfaInviteRevoke);

        var invite = await invites.GetByIdAsync(inviteId);
        AdminException.ThrowIf(invite is null, ErrorCode.BindInviteInvalid);
        if (invite!.RevokedAt is not null || invite.ConsumedAt is not null) return;
        invite.RevokedAt = Now;
        await invites.UpdateAsync(invite);
        logger.LogInformation(
            "TOTP bind invite revoked: inviteId={InviteId} by={Operator}",
            inviteId, operatorUserId);
    }

    /// <inheritdoc />
    public virtual async Task<TotpBindStartOutput> StartBindAsync(TotpBindStartInput input)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.Token), ErrorCode.BindInviteInvalid);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.CurrentPassword), ErrorCode.MfaBindPasswordRequired);

        var (user, invite, usedInitGrant) = await ResolveBindTargetAsync(input.Token.Trim(), input.Account);

        // 必须验证当前密码后才允许写 seed(ADR 0005 / 绑定邀请边界)
        if (!hasher.Verify(input.CurrentPassword, user.Password))
            throw new AdminException(ErrorCode.PasswordWrong);

        // 已绑定则拒绝(须先走恢复/重置)
        AdminException.ThrowIf(user.TotpEnabled && !string.IsNullOrEmpty(user.TotpSeedProtected),
            ErrorCode.BindInviteInvalid);

        var seed = totp.GenerateSeed();
        var protectedSeed = protector.Protect(seed);
        var challengeId = Guid.CreateVersion7().ToString("N");
        var payload = new BindChallengePayload
        {
            UserId = user.Id,
            SeedProtected = protectedSeed,
            InviteId = invite?.Id,
            UsedInitGrant = usedInitGrant,
        };
        await cache.SetAsync(
            CacheKeys.TotpBindChallenge(challengeId),
            JsonSerializer.Serialize(payload),
            BindChallengeTtl);

        var issuer = string.IsNullOrWhiteSpace(security.Level3.TotpIssuer)
            ? "TenonAdmin"
            : security.Level3.TotpIssuer;
        var uri = totp.GetUri(user.Account, issuer, seed);

        return new TotpBindStartOutput
        {
            BindChallengeId = challengeId,
            OtpauthUri = uri,
            Seed = seed,
            ExpiresSeconds = (int)BindChallengeTtl.TotalSeconds,
        };
    }

    /// <inheritdoc />
    public virtual async Task<TotpBindCompleteOutput> CompleteBindAsync(TotpBindCompleteInput input)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.BindChallengeId), ErrorCode.BindInviteInvalid);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.TotpCode), ErrorCode.TotpWrong);

        var challengeKey = CacheKeys.TotpBindChallenge(input.BindChallengeId.Trim());
        var raw = await cache.GetAsync<string>(challengeKey);
        AdminException.ThrowIf(string.IsNullOrEmpty(raw), ErrorCode.BindInviteInvalid);

        var payload = JsonSerializer.Deserialize<BindChallengePayload>(raw!);
        AdminException.ThrowIf(payload is null || payload.UserId == 0, ErrorCode.BindInviteInvalid);

        string seed;
        try { seed = protector.Unprotect(payload!.SeedProtected); }
        catch { throw new AdminException(ErrorCode.BindInviteInvalid); }

        if (!totp.Verify(seed, input.TotpCode.Trim()))
            throw new AdminException(ErrorCode.TotpWrong);

        var user = await users.GetByIdAsync(payload.UserId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        // 先消费邀请 / InitGrant(完成时重验 TTL·NotAfter),失败时挑战仍可保留供重试
        if (payload.InviteId is long inviteId)
        {
            var invite = await invites.GetByIdAsync(inviteId);
            if (invite is null || invite.ConsumedAt is not null || invite.RevokedAt is not null
                || invite.ExpiresAt < Now)
                throw new AdminException(ErrorCode.BindInviteInvalid);
            invite.ConsumedAt = Now;
            await invites.UpdateAsync(invite);
        }
        else if (payload.UsedInitGrant)
        {
            // 完成时重验:已有其他超管完成 TOTP 则禁止 InitGrant 旁路
            var anyBound = await users.AnyAsync(u => u.IsSuperAdmin && u.TotpEnabled);
            AdminException.ThrowIf(anyBound, ErrorCode.BindInviteInvalid);
            var init = security.Level3.InitGrant?.Trim() ?? "";
            await ConsumeDeployGrantAsync(
                Level3DeployGrantKinds.Init,
                SecretHash.Hash(init),
                security.Level3.InitGrantTtlMinutes,
                security.Level3.InitGrantNotAfter);
        }

        // 仅在授权/邀请消费成功后原子消费挑战,防并发双写 MFA
        var consumed = await cache.GetAndRemoveAsync<string>(challengeKey);
        AdminException.ThrowIf(
            string.IsNullOrEmpty(consumed) || !string.Equals(raw, consumed, StringComparison.Ordinal),
            ErrorCode.BindInviteInvalid);

        // 写 seed(再次 Protect 同源材料,版本一致)、启用、清旧恢复码
        user!.TotpSeedProtected = protector.Protect(seed);
        user.TotpEnabled = true;
        user.TotpBoundAt = Now;
        await users.UpdateAsync(user);

        var plains = await ReplaceRecoveryCodesAsync(user.Id);

        logger.LogInformation(
            "TOTP bound: userId={UserId} account={Account} via={Via}",
            user.Id, user.Account, payload.UsedInitGrant ? "init-grant" : "invite");

        return new TotpBindCompleteOutput { RecoveryCodes = plains };
    }

    /// <inheritdoc />
    public virtual async Task UseRecoveryCodeAsync(TotpRecoveryInput input)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.Account), ErrorCode.PasswordWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.CurrentPassword), ErrorCode.PasswordWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.RecoveryCode), ErrorCode.RecoveryCodeInvalid);

        var user = await users.GetFirstAsync(u => u.Account == input.Account.Trim());
        if (user is null)
        {
            // 防枚举:跑等价代价
            hasher.Verify(input.CurrentPassword, hasher.Hash("tenon-admin.timing-dummy"));
            throw new AdminException(ErrorCode.PasswordWrong);
        }

        if (!hasher.Verify(input.CurrentPassword, user.Password))
            throw new AdminException(ErrorCode.PasswordWrong);

        var codeHash = SecretHash.Hash(NormalizeRecoveryCode(input.RecoveryCode));
        var match = await recoveryCodes.AsQueryable()
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .ToListAsync();
        var hit = match.FirstOrDefault(c => SecretHash.FixedEquals(c.CodeHash, codeHash));
        AdminException.ThrowIf(hit is null, ErrorCode.RecoveryCodeInvalid);

        hit!.UsedAt = Now;
        await recoveryCodes.UpdateAsync(hit);

        // 强制重新绑定:清 seed/Enabled,废全部恢复码,吊销全部会话
        await ClearUserMfaAsync(user, revokeSessions: true);

        logger.LogCritical(
            "SECURITY: TOTP recovery code used — userId={UserId} account={Account}; all sessions revoked; rebind required",
            user.Id, user.Account);
    }

    /// <inheritdoc />
    public virtual async Task<TotpResetOutput> ResetSuperAdminMfaAsync(
        TotpSuperAdminResetInput input, long? operatorUserId)
    {
        var target = await users.GetByIdAsync(input.TargetUserId);
        AdminException.ThrowIf(target is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(!target!.IsSuperAdmin, ErrorCode.NoPermission);

        var mode = (input.Mode ?? "peer").Trim().ToLowerInvariant();
        if (mode == "emergency")
        {
            // 先校验唯一超管再消费紧急授权,避免多超管场景误烧 grant
            var superCount = await users.AsQueryable().Where(u => u.IsSuperAdmin).CountAsync();
            AdminException.ThrowIf(superCount != 1, ErrorCode.NoPermission);
            await EnsureEmergencyGrantAsync(input.EmergencyGrant);
        }
        else
        {
            // peer:操作者必须是另一名已启用 TOTP 的超管
            AdminException.ThrowIf(operatorUserId is null or 0, ErrorCode.NoPermission);
            AdminException.ThrowIf(operatorUserId == target.Id, ErrorCode.NoPermission);
            var op = await users.GetByIdAsync(operatorUserId.Value);
            AdminException.ThrowIf(op is null || !op.IsSuperAdmin || !op.TotpEnabled, ErrorCode.NoPermission);
        }

        await ClearUserMfaAsync(target, revokeSessions: true);

        // 发放重新绑定邀请(不直接解除后放行登录——仍须绑定)
        TotpBindInviteOutput invite;
        if (mode == "emergency")
            invite = await IssueSystemEmergencyBindInviteAsync(target.Id);
        else
            invite = await IssueBindInviteAsync(target.Id, operatorUserId!.Value);

        logger.LogCritical(
            "SECURITY HIGHEST: Super-admin MFA reset — targetUserId={Target} mode={Mode} operator={Operator}",
            target.Id, mode, operatorUserId);

        return new TotpResetOutput { Invite = invite };
    }

    // ── 内部步骤(virtual 便于覆写)──────────────────────────────────────

    /// <summary>解析绑定目标:邀请哈希命中 或 InitGrant 匹配未消费。</summary>
    protected virtual async Task<(SysUser user, SysTotpBindInvite? invite, bool usedInitGrant)> ResolveBindTargetAsync(
        string token, string? account)
    {
        var tokenHash = SecretHash.Hash(token);

        // 1) 绑定邀请
        var invite = await invites.GetFirstAsync(i => i.TokenHash == tokenHash);
        if (invite is not null)
        {
            AdminException.ThrowIf(
                invite.ConsumedAt is not null || invite.RevokedAt is not null || invite.ExpiresAt < Now,
                ErrorCode.BindInviteInvalid);
            var u = await users.GetByIdAsync(invite.UserId);
            AdminException.ThrowIf(u is null, ErrorCode.UserNotFound);
            return (u!, invite, false);
        }

        // 2) 部署 InitGrant:仅当系统中尚无任何已绑定 TOTP 的超管
        var init = security.Level3.InitGrant;
        if (!string.IsNullOrWhiteSpace(init)
            && SecretHash.FixedEquals(SecretHash.Hash(init.Trim()), tokenHash))
        {
            var anyBoundSuper = await users.AnyAsync(u => u.IsSuperAdmin && u.TotpEnabled);
            AdminException.ThrowIf(anyBoundSuper, ErrorCode.BindInviteInvalid);

            await EnsureDeployGrantWithinTtlAsync(
                Level3DeployGrantKinds.Init,
                tokenHash,
                security.Level3.InitGrantTtlMinutes,
                security.Level3.InitGrantNotAfter);

            AdminException.ThrowIf(string.IsNullOrWhiteSpace(account), ErrorCode.BindInviteInvalid);
            var u = await users.GetFirstAsync(x => x.Account == account!.Trim());
            AdminException.ThrowIf(u is null || !u.IsSuperAdmin, ErrorCode.BindInviteInvalid);
            AdminException.ThrowIf(u!.TotpEnabled, ErrorCode.BindInviteInvalid);
            return (u, null, true);
        }

        throw new AdminException(ErrorCode.BindInviteInvalid);
    }

    /// <summary>
    /// 部署期一次性授权 TTL:仅经持久 <see cref="ILevel3DeployGrantStore"/>。
    /// Level3 下 store 缺失视为配置错误(预检/启动亦 fail-closed)。
    /// </summary>
    protected virtual Task EnsureDeployGrantWithinTtlAsync(
        string kind, string grantHash, int ttlMinutes, DateTimeOffset? absoluteNotAfter)
    {
        if (deployGrants is null)
            throw new AdminException(ErrorCode.Level3Misconfigured);
        return deployGrants.EnsureWithinTtlAsync(kind, grantHash, ttlMinutes, absoluteNotAfter);
    }

    protected virtual Task ConsumeDeployGrantAsync(
        string kind, string grantHash, int ttlMinutes, DateTimeOffset? absoluteNotAfter)
    {
        if (deployGrants is null)
            throw new AdminException(ErrorCode.Level3Misconfigured);
        return deployGrants.ConsumeAsync(kind, grantHash, ttlMinutes, absoluteNotAfter);
    }

    /// <summary>生成 10 个恢复码,旧码全部软删;返回明文列表。</summary>
    protected virtual async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(long userId)
    {
        var existing = await recoveryCodes.AsQueryable().Where(c => c.UserId == userId).ToListAsync();
        foreach (var e in existing)
            await recoveryCodes.DeleteAsync(e.Id);

        var plains = new List<string>(RecoveryCodeCount);
        var entities = new List<SysTotpRecoveryCode>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var plain = CreateRecoveryCode();
            plains.Add(plain);
            entities.Add(new SysTotpRecoveryCode
            {
                UserId = userId,
                CodeHash = SecretHash.Hash(NormalizeRecoveryCode(plain)),
            });
        }
        await recoveryCodes.InsertRangeAsync(entities);
        return plains;
    }

    /// <summary>清除用户 TOTP 状态并可选吊销会话。</summary>
    protected virtual async Task ClearUserMfaAsync(SysUser user, bool revokeSessions)
    {
        user.TotpEnabled = false;
        user.TotpSeedProtected = null;
        user.TotpBoundAt = null;
        await users.UpdateAsync(user);

        var codes = await recoveryCodes.AsQueryable().Where(c => c.UserId == user.Id).ToListAsync();
        foreach (var c in codes)
            await recoveryCodes.DeleteAsync(c.Id);

        // 作废未使用邀请
        var openInvites = await invites.AsQueryable()
            .Where(i => i.UserId == user.Id && i.ConsumedAt == null && i.RevokedAt == null)
            .ToListAsync();
        foreach (var i in openInvites)
        {
            i.RevokedAt = Now;
            await invites.UpdateAsync(i);
        }

        if (revokeSessions)
            await sessions.RevokeAllForUserAsync(user.Id);
    }

    /// <summary>校验紧急授权(含 TTL)并标记消费。</summary>
    protected virtual async Task EnsureEmergencyGrantAsync(string? grant)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(grant), ErrorCode.BindInviteInvalid);
        var expected = security.Level3.EmergencyGrant;
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(expected), ErrorCode.BindInviteInvalid);

        var plain = grant!.Trim();
        var hash = SecretHash.Hash(plain);
        var ok = SecretHash.FixedEquals(hash, SecretHash.Hash(expected!.Trim()));
        AdminException.ThrowIf(!ok, ErrorCode.BindInviteInvalid);

        await EnsureDeployGrantWithinTtlAsync(
            Level3DeployGrantKinds.Emergency,
            hash,
            security.Level3.EmergencyGrantTtlMinutes,
            security.Level3.EmergencyGrantNotAfter);

        await ConsumeDeployGrantAsync(
            Level3DeployGrantKinds.Emergency,
            hash,
            security.Level3.EmergencyGrantTtlMinutes,
            security.Level3.EmergencyGrantNotAfter);
    }

    /// <summary>
    /// 公开紧急恢复:唯一超管、无会话场景。须同时证明 EmergencyGrant + 账号密码;
    /// 错误统一为 BindInviteInvalid / PasswordWrong 语义,避免枚举。
    /// </summary>
    public virtual async Task<TotpResetOutput> EmergencyResetSoleSuperAdminAsync(TotpEmergencyResetInput input)
    {
        // 先做时间常数风格校验:缺字段统一失败
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.Account), ErrorCode.PasswordWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.CurrentPassword), ErrorCode.PasswordWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.EmergencyGrant), ErrorCode.BindInviteInvalid);

        var user = await users.GetFirstAsync(u => u.Account == input.Account.Trim());
        if (user is null || !user.IsSuperAdmin)
        {
            hasher.Verify(input.CurrentPassword, hasher.Hash("tenon-admin.timing-dummy"));
            throw new AdminException(ErrorCode.PasswordWrong);
        }

        if (!hasher.Verify(input.CurrentPassword, user.Password))
            throw new AdminException(ErrorCode.PasswordWrong);

        var superCount = await users.AsQueryable().Where(u => u.IsSuperAdmin).CountAsync();
        AdminException.ThrowIf(superCount != 1, ErrorCode.NoPermission);

        return await ResetSuperAdminMfaAsync(
            new TotpSuperAdminResetInput
            {
                TargetUserId = user.Id,
                Mode = "emergency",
                EmergencyGrant = input.EmergencyGrant,
            },
            operatorUserId: null);
    }

    /// <summary>密码学随机 bearer(Base64Url,无填充)。</summary>
    protected static string CreateBearerToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>恢复码形态:XXXX-XXXX-XXXX(大写字母数字,易读)。</summary>
    protected static string CreateRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> chars = stackalloc char[14];
        var n = 0;
        for (var g = 0; g < 3; g++)
        {
            if (g > 0) chars[n++] = '-';
            for (var i = 0; i < 4; i++)
                chars[n++] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(chars);
    }

    protected static string NormalizeRecoveryCode(string code) =>
        code.Trim().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
            .ToUpperInvariant();

    private sealed class BindChallengePayload
    {
        public long UserId { get; set; }
        public string SeedProtected { get; set; } = "";
        public long? InviteId { get; set; }
        public bool UsedInitGrant { get; set; }
    }
}
