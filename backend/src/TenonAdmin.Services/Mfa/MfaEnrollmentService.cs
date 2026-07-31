using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// TOTP 自助绑定 / 恢复码 / 管理员清除 MFA(ADR 0006)。
/// 不实现 InitGrant、绑定邀请、紧急授权。
/// </summary>
public class MfaEnrollmentService(
    IRepository<SysUser> users,
    IRepository<SysTotpRecoveryCode> recoveryCodes,
    IPasswordHasher hasher,
    ISecretProtector protector,
    ITotpService totp,
    ISessionService sessions,
    ICacheProvider cache,
    AdminSecurityOptions security,
    ILogger<MfaEnrollmentService> logger,
    TimeProvider? time = null,
    IPermissionProvider? permissions = null) : IMfaEnrollmentService
{
    private static readonly TimeSpan BindChallengeTtl = TimeSpan.FromMinutes(10);

    private DateTime Now => (time ?? TimeProvider.System).GetLocalNow().DateTime;

    private bool TotpOn => security.IsTotpFeatureEnabled;

    private int RecoveryCodeCount =>
        security.Totp.RecoveryCodeCount > 0 ? security.Totp.RecoveryCodeCount : 10;

    /// <inheritdoc />
    public virtual async Task<TotpBindStartOutput> StartBindAsync(TotpBindStartInput input)
    {
        AdminException.ThrowIf(!TotpOn, ErrorCode.NoPermission);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.Account), ErrorCode.PasswordWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.CurrentPassword), ErrorCode.MfaBindPasswordRequired);

        var user = await users.GetFirstAsync(u => u.Account == input.Account.Trim());
        if (user is null)
        {
            hasher.Verify(input.CurrentPassword, hasher.Hash("tenon-admin.timing-dummy"));
            throw new AdminException(ErrorCode.PasswordWrong);
        }

        if (!hasher.Verify(input.CurrentPassword, user.Password))
            throw new AdminException(ErrorCode.PasswordWrong);

        AdminException.ThrowIf(
            user.TotpEnabled && !string.IsNullOrEmpty(user.TotpSeedProtected),
            ErrorCode.MfaBindInvalid);

        var seed = totp.GenerateSeed();
        var protectedSeed = protector.Protect(seed);
        var challengeId = Guid.CreateVersion7().ToString("N");
        var payload = new BindChallengePayload
        {
            UserId = user.Id,
            SeedProtected = protectedSeed,
        };
        await cache.SetAsync(
            CacheKeys.TotpBindChallenge(challengeId),
            JsonSerializer.Serialize(payload),
            BindChallengeTtl);

        var issuer = security.ResolveTotpIssuer();
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
        AdminException.ThrowIf(!TotpOn, ErrorCode.NoPermission);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.BindChallengeId), ErrorCode.MfaBindInvalid);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.TotpCode), ErrorCode.TotpWrong);

        var challengeKey = CacheKeys.TotpBindChallenge(input.BindChallengeId.Trim());
        var raw = await cache.GetAsync<string>(challengeKey);
        AdminException.ThrowIf(string.IsNullOrEmpty(raw), ErrorCode.MfaBindInvalid);

        var payload = JsonSerializer.Deserialize<BindChallengePayload>(raw!);
        AdminException.ThrowIf(payload is null || payload.UserId == 0, ErrorCode.MfaBindInvalid);

        string seed;
        try { seed = protector.Unprotect(payload!.SeedProtected); }
        catch { throw new AdminException(ErrorCode.MfaBindInvalid); }

        if (!totp.Verify(seed, input.TotpCode.Trim()))
            throw new AdminException(ErrorCode.TotpWrong);

        var user = await users.GetByIdAsync(payload.UserId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        var consumed = await cache.GetAndRemoveAsync<string>(challengeKey);
        AdminException.ThrowIf(
            string.IsNullOrEmpty(consumed) || !string.Equals(raw, consumed, StringComparison.Ordinal),
            ErrorCode.MfaBindInvalid);

        user!.TotpSeedProtected = protector.Protect(seed);
        user.TotpEnabled = true;
        user.TotpBoundAt = Now;
        await users.UpdateAsync(user);

        var plains = await ReplaceRecoveryCodesAsync(user.Id);

        logger.LogInformation(
            "TOTP bound (self-service): userId={UserId} account={Account}",
            user.Id, user.Account);

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

        await ClearMfaStateAsync(user, revokeSessions: true);

        logger.LogCritical(
            "SECURITY: TOTP recovery code used — userId={UserId} account={Account}; sessions revoked; rebind required",
            user.Id, user.Account);
    }

    /// <inheritdoc />
    public virtual async Task ClearUserMfaAsync(long targetUserId, long operatorUserId)
    {
        AdminException.ThrowIf(!TotpOn, ErrorCode.NoPermission);
        AdminException.ThrowIf(operatorUserId <= 0, ErrorCode.NoPermission);

        var op = await users.GetByIdAsync(operatorUserId);
        AdminException.ThrowIf(op is null || !op.Enabled, ErrorCode.NoPermission);
        if (op.TotpEnabled)
        {
            // 已绑操作人可清他人;未绑仅超管可清(避免锁死后无人能管)
        }
        else
        {
            AdminException.ThrowIf(!op.IsSuperAdmin, ErrorCode.TotpNotBound);
        }

        if (!op.IsSuperAdmin)
        {
            if (permissions is null)
                throw new AdminException(ErrorCode.NoPermission);
            var codes = await permissions.GetPermissionCodesAsync(op.Id);
            AdminException.ThrowIf(
                !codes.Contains(HighSensitivityPermissions.MfaClear, StringComparer.OrdinalIgnoreCase),
                ErrorCode.NoPermission);
        }

        var target = await users.GetByIdAsync(targetUserId);
        AdminException.ThrowIf(target is null, ErrorCode.UserNotFound);

        await ClearMfaStateAsync(target!, revokeSessions: true);

        logger.LogCritical(
            "SECURITY: admin cleared MFA — targetUserId={Target} operator={Operator}",
            targetUserId, operatorUserId);
    }

    protected virtual async Task ClearMfaStateAsync(SysUser user, bool revokeSessions)
    {
        user.TotpEnabled = false;
        user.TotpSeedProtected = null;
        user.TotpBoundAt = null;
        await users.UpdateAsync(user);

        var codes = await recoveryCodes.AsQueryable().Where(c => c.UserId == user.Id).ToListAsync();
        foreach (var c in codes)
            await recoveryCodes.DeleteAsync(c.Id);

        if (revokeSessions)
            await sessions.RevokeAllForUserAsync(user.Id);
    }

    protected virtual async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(long userId)
    {
        var existing = await recoveryCodes.AsQueryable().Where(c => c.UserId == userId).ToListAsync();
        foreach (var e in existing)
            await recoveryCodes.DeleteAsync(e.Id);

        var count = RecoveryCodeCount;
        var plains = new List<string>(count);
        var entities = new List<SysTotpRecoveryCode>(count);
        for (var i = 0; i < count; i++)
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
    }
}
