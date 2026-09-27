using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

public class SessionRevocationRaceTests
{
    [Fact]
    public async Task Stale_cache_repopulation_cannot_restore_a_revoked_session()
    {
        using var factory = new AdminAppFactory();
        await factory.CreateClient().LoginToken("superAdmin", "Test@123456");
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var sessions = services.GetRequiredService<IRepository<SysSession>>();
        var session = await sessions.GetFirstAsync(s => s.Account == "superAdmin");
        Assert.NotNull(session);
        var cache = services.GetRequiredService<ICacheProvider>();
        var stale = await cache.GetAsync<SessionCacheInfo>(CacheKeys.Session(session.SessionId));
        Assert.NotNull(stale);
        var service = services.GetRequiredService<ISessionService>();
        await service.RevokeAsync(session.SessionId);

        // 模拟吊销前已读出的活动信息在删除缓存之后才完成回填。
        await cache.SetAsync(CacheKeys.Session(session.SessionId), stale, TimeSpan.FromHours(1));
        Assert.False(await service.IsActiveAsync(session.SessionId));
        Assert.False(await services.GetRequiredService<ISessionActivityTracker>()
            .TouchAsync(session.SessionId, session.UserId, session.ExpiresAt));
    }

    [Fact]
    public async Task Failed_successor_insert_rolls_back_token_consumption_and_session_renewal()
    {
        using var factory = new AdminAppFactory();
        var login = await (await factory.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        var refresh = login.GetProperty("data").GetProperty("refreshToken").GetString()!;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tokens = services.GetRequiredService<IRepository<SysRefreshToken>>();
        var original = await tokens.GetFirstAsync(t => t.Status == RefreshTokenStatus.Active);
        Assert.NotNull(original);
        var db = tokens.Db;
        db.Aop.OnLogExecuting = (sql, parameters) =>
        {
            if (sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("sys_refresh_token", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("injected successor insert failure");
        };
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => services.GetRequiredService<ISessionService>().RefreshAsync(refresh));
        }
        finally { db.Aop.OnLogExecuting = null; }

        Assert.Equal(RefreshTokenStatus.Active, (await tokens.GetByIdAsync(original.Id))!.Status);
        Assert.Single(await tokens.AsQueryable().Where(t => t.SessionId == original.SessionId).ToListAsync());
        // 恢复数据库后原票据仍能刷新，不会遗留半轮换会话。
        await services.GetRequiredService<ISessionService>().RefreshAsync(refresh);
    }
    [Fact]
    public async Task Revocation_after_initial_validation_prevents_rotation()
    {
        using var factory = new AdminAppFactory();
        var login = await (await factory.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        var refresh = login.GetProperty("data").GetProperty("refreshToken").GetString()!;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var service = new RevokeAfterValidationService(services);

        var error = await Assert.ThrowsAsync<AdminException>(() => service.RefreshAsync(refresh));
        Assert.Equal(ErrorCode.RefreshTokenInvalid, error.Code);
        var tokens = services.GetRequiredService<IRepository<SysRefreshToken>>();
        Assert.False(await tokens.AnyAsync(t => t.Status == RefreshTokenStatus.Active));
        var session = await services.GetRequiredService<IRepository<SysSession>>()
            .GetFirstAsync(s => s.Account == "superAdmin");
        Assert.NotNull(session);
        Assert.False(await services.GetRequiredService<ISessionService>().IsActiveAsync(session.SessionId));
    }

    private sealed class RevokeAfterValidationService(IServiceProvider services) : SessionService(
        services.GetRequiredService<IRepository<SysSession>>(),
        services.GetRequiredService<IRepository<SysRefreshToken>>(),
        services.GetRequiredService<IRepository<SysUser>>(),
        services.GetRequiredService<ITokenProvider>(),
        services.GetRequiredService<ICacheProvider>(),
        services.GetRequiredService<AdminSecurityOptions>(),
        services.GetRequiredService<ISecurityPolicyProvider>(),
        services.GetRequiredService<ICurrentUser>(),
        services.GetRequiredService<TimeProvider>())
    {
        public override async Task<bool> IsActiveAsync(string sessionId)
        {
            var active = await base.IsActiveAsync(sessionId);
            // 固定交错点：刷新已读到活跃会话，随后另一请求先完成吊销。
            await services.GetRequiredService<ISessionService>().RevokeAsync(sessionId);
            return active;
        }
    }

}
