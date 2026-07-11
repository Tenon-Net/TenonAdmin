using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISessionService"/> 默认实现(设计 §15)。会话落库(源) + 落缓存(热路径),
/// 刷新令牌只存哈希;轮换用条件更新(仅当仍 Active 才置 Used)兼作并发保护,复用即整会话吊销。
/// 时间统一走 UTC(<see cref="TimeProvider"/>),避免本地/UTC 混用导致过期判断错乱。
/// </summary>
public class SessionService(
    IRepository<SysSession> sessions,
    IRepository<SysRefreshToken> refreshTokens,
    IRepository<SysUser> users,
    ITokenProvider tokens,
    ICacheProvider cache,
    AdminSecurityOptions security,
    ISecurityPolicyProvider policy,
    TimeProvider time) : ISessionService
{
    // 同一用户的"淘汰旧会话 + 开新会话"串行化锁(单实例内)。避免并发登录各自读到同一活跃集合、
    // 各淘汰不足、最终活跃数超过单端/限并发上限(P2-15)。ponytail: 进程内锁字典;多实例需分布式锁,留待 Redis 包。
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> _openLocks = new();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>高熵随机串的哈希:SHA-256 十六进制(不是密码,无需 PBKDF2)。</summary>
    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <inheritdoc />
    public virtual async Task OpenAsync(SysUser user, string sessionId, TokenPair pair)
    {
        var gate = _openLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await EnforceConcurrencyAsync(user.Id);   // 开新会话前按单端/限并发腾位(与开会话同锁串行,防越额)

            var expiresAt = pair.RefreshExpiresAt.UtcDateTime;
            // 会话行 + 刷新令牌成对写入包事务(P2-16):半写不留"在线却不可刷新"的僵尸会话
            var result = await sessions.Db.Ado.UseTranAsync(async () =>
            {
                await sessions.InsertAsync(new SysSession { SessionId = sessionId, UserId = user.Id, Account = user.Account, ExpiresAt = expiresAt });
                await refreshTokens.InsertAsync(new SysRefreshToken
                {
                    SessionId = sessionId,
                    UserId = user.Id,
                    TokenHash = Sha256Hex(pair.RefreshToken),
                    ExpiresAt = expiresAt,
                    Status = RefreshTokenStatus.Active,
                });
            });
            if (!result.IsSuccess) throw result.ErrorException;
            await CacheActiveAsync(sessionId, user.Id, expiresAt);   // 缓存写在事务提交之后
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsActiveAsync(string sessionId)
    {
        var key = CacheKeys.Session(sessionId);
        var cached = await cache.GetAsync<SessionCacheInfo>(key);
        if (cached is not null) return cached.ExpiresAt > Now;   // 命中即活跃(强退会移除缓存)

        // 未命中:查库判定(可能是被驱逐、或本进程没缓存过),活跃则回填
        var session = await sessions.GetFirstAsync(s => s.SessionId == sessionId);
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= Now) return false;
        await CacheActiveAsync(sessionId, session.UserId, session.ExpiresAt);
        return true;
    }

    /// <inheritdoc />
    public virtual async Task<RefreshedSession> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        var rt = await refreshTokens.GetFirstAsync(t => t.TokenHash == Sha256Hex(refreshToken));
        if (rt is null) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 复用检测:已轮换令牌再现 = 重放,吊销整会话(攻击者与真用户一起下线,安全优先)
        if (rt.Status == RefreshTokenStatus.Used)
        {
            await RevokeAsync(rt.SessionId);
            throw new AdminException(ErrorCode.RefreshTokenInvalid);
        }
        if (rt.Status != RefreshTokenStatus.Active || rt.ExpiresAt <= Now)
            throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 会话须仍活跃(未强退/未过期)
        if (!await IsActiveAsync(rt.SessionId)) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        var user = await users.GetByIdAsync(rt.UserId);
        if (user is null) throw new AdminException(ErrorCode.RefreshTokenInvalid);
        AdminException.ThrowIf(!user.Enabled, ErrorCode.AccountDisabled);

        // 原子轮换:仅当仍 Active 才置 Used;rowsAffected==0 说明已被并发轮换,按无效处理
        var rotated = await refreshTokens.Db.Updateable<SysRefreshToken>()
            .SetColumns(t => t.Status == RefreshTokenStatus.Used)
            .Where(t => t.Id == rt.Id && t.Status == RefreshTokenStatus.Active)
            .ExecuteCommandAsync();
        if (rotated == 0) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 用同一 SessionId 签发新令牌对(会话延续,不新建),存新刷新令牌哈希;令牌时长运行时可配
        var (accessMin, refreshMin) = await policy.GetSessionTtlAsync();
        var pair = tokens.Create(new TokenSubject(user.Id, user.Account, rt.SessionId, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(accessMin), TimeSpan.FromMinutes(refreshMin));
        var expiresAt = pair.RefreshExpiresAt.UtcDateTime;
        await refreshTokens.InsertAsync(new SysRefreshToken
        {
            SessionId = rt.SessionId,
            UserId = user.Id,
            TokenHash = Sha256Hex(pair.RefreshToken),
            ExpiresAt = expiresAt,
            Status = RefreshTokenStatus.Active,
        });

        // 滑动续期:会话过期跟到新刷新令牌过期,缓存同步刷新
        await sessions.Db.Updateable<SysSession>()
            .SetColumns(s => s.ExpiresAt == expiresAt)
            .Where(s => s.SessionId == rt.SessionId)
            .ExecuteCommandAsync();
        await CacheActiveAsync(rt.SessionId, user.Id, expiresAt);

        return new RefreshedSession(pair, user);
    }

    /// <inheritdoc />
    public virtual async Task RevokeAsync(string sessionId)
    {
        await sessions.Db.Updateable<SysSession>()
            .SetColumns(s => s.RevokedAt == Now)
            .Where(s => s.SessionId == sessionId && s.RevokedAt == null)
            .ExecuteCommandAsync();
        await refreshTokens.Db.Updateable<SysRefreshToken>()
            .SetColumns(t => t.Status == RefreshTokenStatus.Revoked)
            .Where(t => t.SessionId == sessionId && t.Status == RefreshTokenStatus.Active)
            .ExecuteCommandAsync();
        await cache.RemoveAsync(CacheKeys.Session(sessionId));   // 缓存移除 → 下次校验查库得吊销 → 401
    }

    /// <inheritdoc />
    public virtual async Task RevokeAllForUserAsync(long userId)
    {
        // 与 EnforceConcurrencyAsync 同款"按 userId 取活跃会话再逐个吊销",少了单端/限并发的名额判断——
        // 停用/删除用户要下线其全部会话。逐个 RevokeAsync 复用其"标记两表 + 清会话缓存"逻辑。
        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .ToListAsync();
        foreach (var s in active) await RevokeAsync(s.SessionId);
    }

    /// <inheritdoc />
    public virtual Task<PagedList<OnlineSessionItem>> ListOnlineAsync(SessionPageInput input) =>
        sessions.AsQueryable()
            .Where(s => s.RevokedAt == null && s.ExpiresAt > Now)
            .WhereIF(input.UserId.HasValue, s => s.UserId == input.UserId)
            .OrderByDescending(s => s.CreateTime)
            .Select(s => new OnlineSessionItem
            {
                SessionId = s.SessionId,
                UserId = s.UserId,
                Account = s.Account,
                Ip = s.Ip,
                UserAgent = s.UserAgent,
                LoginTime = s.CreateTime,
                ExpiresAt = s.ExpiresAt,
            })
            .ToPagedListAsync(input.Current, input.Size);

    /// <summary>按单端/限并发策略吊销既有会话,给新会话腾位。</summary>
    protected virtual async Task EnforceConcurrencyAsync(long userId)
    {
        var mode = security.Session.Mode;
        var max = security.Session.MaxConcurrent;
        if (mode != SessionMode.Single && max <= 0) return;   // 多端不限:无需处理

        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .OrderBy(s => s.CreateTime)     // 最早登录在前,优先踢旧
            .ToListAsync();

        // 单端:旧会话全踢;限并发:留 1 个名额给即将开的新会话,踢最旧的超额部分
        var toRevoke = mode == SessionMode.Single
            ? active
            : active.Take(Math.Max(0, active.Count + 1 - max)).ToList();

        foreach (var s in toRevoke) await RevokeAsync(s.SessionId);
    }

    private Task CacheActiveAsync(string sessionId, long userId, DateTime expiresAt)
    {
        var ttl = expiresAt - Now;
        return ttl <= TimeSpan.Zero
            ? Task.CompletedTask
            : cache.SetAsync(CacheKeys.Session(sessionId), new SessionCacheInfo { UserId = userId, ExpiresAt = expiresAt }, ttl);
    }
}
