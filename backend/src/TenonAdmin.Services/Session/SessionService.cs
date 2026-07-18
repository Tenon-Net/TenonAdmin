using System.Security.Cryptography;
using System.Text;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISessionService"/> 默认实现(设计 §15)。会话落库(源) + 落缓存(热路径),
/// 刷新令牌只存哈希;轮换用条件更新(仅当仍 Active 才置 Used)兼作并发保护,复用即整会话吊销。
/// 时间统一走 UTC(<see cref="TimeProvider"/>),避免本地/UTC 混用导致过期判断错乱。
/// <para><b>无进程内锁</b>:单端/限并发的名额收敛采「先插入、再收敛」(见 <see cref="EnforceConcurrencyAsync"/>)——
/// 并发登录都落库后各自重读、各自算出同一个"保留最新 N"的答案,天然收敛,不靠锁串行化。
/// 原实现用一把 <c>static</c> 锁字典护住"读活跃集合 → 腾位 → 插入"这个读-改-写,但那把锁跨不了进程:
/// 多副本下两个登录照样各读各的,单端踢不掉旧会话、并发上限被突破(换 Redis 也修不好,它不是缓存问题)。</para>
/// </summary>
public class SessionService(
    IRepository<SysSession> sessions,
    IRepository<SysRefreshToken> refreshTokens,
    IRepository<SysUser> users,
    ITokenProvider tokens,
    ICacheProvider cache,
    AdminSecurityOptions security,
    ISecurityPolicyProvider policy,
    ICurrentUser currentUser,
    TimeProvider time,
    IRealtimePublisher? realtime = null) : ISessionService
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>高熵随机串的哈希:SHA-256 十六进制(不是密码,无需 PBKDF2)。</summary>
    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <inheritdoc />
    public virtual async Task OpenAsync(SysUser user, string sessionId, TokenPair pair)
    {
        var expiresAt = pair.RefreshExpiresAt.UtcDateTime;
        // 会话行 + 刷新令牌成对写入包事务(P2-16):半写不留"在线却不可刷新"的僵尸会话
        var result = await sessions.Db.Ado.UseTranAsync(async () =>
        {
            // IP/UA 取自当前登录请求(与 LogService 同款,登录前即可读到);会话行是登录时快照,刷新不重写
            await sessions.InsertAsync(new SysSession
            {
                SessionId = sessionId,
                UserId = user.Id,
                Account = user.Account,
                Ip = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
                ExpiresAt = expiresAt,
            });
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

        // 先插入、再收敛(见类注释):插完才腾位,并发的两个登录都能看见对方的新行,于是都算出同一个
        // "只保留最新 N 个"的答案 → 收敛到上限。不需要锁,因而也跨得了进程。
        await EnforceConcurrencyAsync(user.Id);
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
        // 实时推送(开启时):即时把该会话的在线连接踢下线,不必等它下次请求才吃 401。
        // 所有下线路径(强退/超并发收敛/刷新复用/停用删号)都汇聚到此,一处接线全覆盖。
        if (realtime is not null)
            await realtime.NotifySessionAsync(sessionId, "force-logout");
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

    /// <summary>
    /// 按单端/限并发策略收敛活跃会话:<b>只保留最新的 N 个,其余一律吊销</b>。
    /// <para>在新会话<b>插入之后</b>调用(见 <see cref="OpenAsync"/>)。这是关键:并发的两个登录都已落库,
    /// 于是都读得到对方的行、都算出同一个"保留最新 N"的答案 → 收敛到上限。吊销幂等(只写未吊销的行),
    /// 重复吊销无害。因此<b>不需要任何锁</b>——原来那把进程内锁跨不了副本,换 Redis 也修不好。</para>
    /// <para>排序以 <c>CreateTime</c> 为准,同毫秒用雪花 <c>Id</c> 决胜(单调递增),保证各副本算出的顺序一致。</para>
    /// </summary>
    protected virtual async Task EnforceConcurrencyAsync(long userId)
    {
        var mode = security.Session.Mode;
        var max = security.Session.MaxConcurrent;
        if (mode != SessionMode.Single && max <= 0) return;   // 多端不限:无需处理

        var keep = mode == SessionMode.Single ? 1 : max;      // 单端 = 只留最新一个

        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .OrderByDescending(s => s.CreateTime)             // 最新在前
            .OrderByDescending(s => s.Id)                     // 同毫秒:雪花 Id 决胜(各副本口径一致)
            .ToListAsync();

        foreach (var s in active.Skip(keep))                  // 超出名额的(最旧的那些)一律吊销
            await RevokeAsync(s.SessionId);
    }

    private Task CacheActiveAsync(string sessionId, long userId, DateTime expiresAt)
    {
        var ttl = expiresAt - Now;
        return ttl <= TimeSpan.Zero
            ? Task.CompletedTask
            : cache.SetAsync(CacheKeys.Session(sessionId), new SessionCacheInfo { UserId = userId, ExpiresAt = expiresAt }, ttl);
    }
}
