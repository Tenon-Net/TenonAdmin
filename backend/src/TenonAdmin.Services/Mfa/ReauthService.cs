using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IReauthService"/> 默认实现:缓存键按 userId+sid 绑定,TTL 默认 5 分钟。
/// </summary>
public class ReauthService(
    ICacheProvider cache,
    AdminSecurityOptions security,
    IRepository<SysUser> users,
    ITotpService totp,
    ISecretProtector protector,
    IPasswordHasher hasher) : IReauthService
{
    /// <inheritdoc />
    public virtual async Task GrantAsync(long userId, string method, string? sessionId = null)
    {
        // ADR 0006:产品键 Totp:ReauthWindowMinutes 优先,回退历史 Level3 键
        var minutes = Math.Max(1, security.ResolveReauthWindowMinutes());
        var value = string.IsNullOrWhiteSpace(method) ? "totp" : method.Trim();
        await cache.SetAsync(CacheKeys.ReauthGrant(userId, sessionId), value, TimeSpan.FromMinutes(minutes));
        // 记录用户维度的 sid 集合,便于 RevokeAllForUserAsync(尽力而为)
        if (!string.IsNullOrEmpty(sessionId))
            await TrackSessionAsync(userId, sessionId, minutes);
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsGrantedAsync(long userId, TimeSpan? within = null, string? sessionId = null)
    {
        var v = await cache.GetAsync<string>(CacheKeys.ReauthGrant(userId, sessionId));
        return !string.IsNullOrEmpty(v);
    }

    /// <inheritdoc />
    public virtual async Task RevokeAsync(long userId, string? sessionId = null)
    {
        await cache.RemoveAsync(CacheKeys.ReauthGrant(userId, sessionId));
        if (!string.IsNullOrEmpty(sessionId))
            await UntackSessionAsync(userId, sessionId);
    }

    /// <inheritdoc />
    public virtual async Task RevokeAllForUserAsync(long userId)
    {
        var key = CacheKeys.ReauthSessions(userId);
        var sids = await cache.GetAsync<string[]>(key) ?? [];
        foreach (var sid in sids)
            await cache.RemoveAsync(CacheKeys.ReauthGrant(userId, sid));
        await cache.RemoveAsync(CacheKeys.ReauthGrant(userId, null)); // 无 sid 兼容键
        await cache.RemoveAsync(key);
    }

    /// <inheritdoc />
    public virtual async Task VerifyAndGrantAsync(long userId, ReauthInput input, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        var method = (input.Method ?? "totp").Trim().ToLowerInvariant();
        if (method == "password")
        {
            AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.Password), ErrorCode.PasswordWrong);
            if (!hasher.Verify(input.Password!, user!.Password))
                throw new AdminException(ErrorCode.PasswordWrong);
        }
        else
        {
            AdminException.ThrowIf(
                !user!.TotpEnabled || string.IsNullOrEmpty(user.TotpSeedProtected),
                ErrorCode.TotpNotBound);
            AdminException.ThrowIf(string.IsNullOrWhiteSpace(input.TotpCode), ErrorCode.TotpWrong);
            string seed;
            try { seed = protector.Unprotect(user.TotpSeedProtected!); }
            catch { throw new AdminException(ErrorCode.TotpWrong); }
            if (!totp.Verify(seed, input.TotpCode!.Trim()))
                throw new AdminException(ErrorCode.TotpWrong);
            method = "totp";
        }

        await GrantAsync(userId, method, sessionId);
    }

    private async Task TrackSessionAsync(long userId, string sessionId, int minutes)
    {
        var key = CacheKeys.ReauthSessions(userId);
        var existing = (await cache.GetAsync<string[]>(key) ?? [])
            .Where(s => !string.IsNullOrEmpty(s) && s != sessionId)
            .Append(sessionId)
            .Distinct()
            .ToArray();
        await cache.SetAsync(key, existing, TimeSpan.FromMinutes(Math.Max(minutes, 5) + 1));
    }

    private async Task UntackSessionAsync(long userId, string sessionId)
    {
        var key = CacheKeys.ReauthSessions(userId);
        var existing = await cache.GetAsync<string[]>(key);
        if (existing is null || existing.Length == 0) return;
        var next = existing.Where(s => s != sessionId).ToArray();
        if (next.Length == 0)
            await cache.RemoveAsync(key);
        else
            await cache.SetAsync(key, next, TimeSpan.FromMinutes(6));
    }
}
