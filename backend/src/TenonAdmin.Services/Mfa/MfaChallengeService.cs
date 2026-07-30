using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMfaChallengeService"/> 默认实现:挑战存缓存,校验时解密用户 TOTP seed 并 <see cref="ITotpService.Verify"/>。
/// </summary>
public class MfaChallengeService(
    ICacheProvider cache,
    IRepository<SysUser> users,
    ITotpService totp,
    ISecretProtector protector,
    AdminSecurityOptions security) : IMfaChallengeService
{
    /// <inheritdoc />
    public virtual async Task<string> CreateChallengeAsync(long userId)
    {
        var challengeId = Guid.CreateVersion7().ToString("N");
        var ttl = TimeSpan.FromSeconds(Math.Max(60, security.Level3.TotpChallengeTtlSeconds));
        await cache.SetAsync(CacheKeys.TotpMfaChallenge(challengeId), userId, ttl);
        return challengeId;
    }

    /// <inheritdoc />
    public virtual Task<long> GetChallengeAsync(string challengeId) =>
        string.IsNullOrWhiteSpace(challengeId)
            ? Task.FromResult(0L)
            : cache.GetAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));

    /// <inheritdoc />
    public virtual async Task<long> VerifyAndConsumeAsync(string challengeId, string totpCode)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(challengeId), ErrorCode.TotpWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(totpCode), ErrorCode.TotpWrong);

        var userId = await cache.GetAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));
        AdminException.ThrowIf(userId == 0, ErrorCode.TotpWrong);

        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null || !user.TotpEnabled || string.IsNullOrEmpty(user.TotpSeedProtected),
            ErrorCode.TotpNotBound);

        string seed;
        try { seed = protector.Unprotect(user.TotpSeedProtected!); }
        catch { throw new AdminException(ErrorCode.TotpWrong); }

        if (!totp.Verify(seed, totpCode.Trim()))
            throw new AdminException(ErrorCode.TotpWrong);

        // 码对才消费挑战(防并发重放)
        var consumed = await cache.GetAndRemoveAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));
        AdminException.ThrowIf(consumed == 0, ErrorCode.TotpWrong);
        return consumed;
    }
}
