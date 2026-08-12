using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// QA25.1:<see cref="TenonHub.OnConnectedAsync"/> 必须在入组前核验 <c>sid</c> 仍活跃——
/// JWT 过期校验不代表会话仍活跃(强退/登出后原令牌在过期前仍"合法"),缺失/已失效的 sid 应直接断开连接、
/// 不加入任何组(否则被强退的连接仍能收到 user-{sub}/session-{sid} 定向推送)。
/// <para>SignalR <see cref="Hub"/> 的 <c>Context</c>/<c>Groups</c> 是可写属性,专为脱离真实连接的单元测试设计——
/// 本测试不起真实 Hub 管道,直接构造并断言。</para>
/// </summary>
public class TenonHubTests
{
    private sealed class FakeHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-1") : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; } = user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>只实现本测试用到的 <see cref="ISessionService.IsActiveAsync"/>,其余成员不会被调用。</summary>
    private sealed class FakeSessionService(bool active) : ISessionService
    {
        public Task OpenAsync(SysUser user, string sessionId, TokenPair pair) => throw new NotSupportedException();
        public Task<bool> IsActiveAsync(string sessionId) => Task.FromResult(active);
        public Task<RefreshedSession> RefreshAsync(string refreshToken) => throw new NotSupportedException();
        public Task RevokeAsync(string sessionId) => throw new NotSupportedException();
        public Task RevokeAllForUserAsync(long userId) => throw new NotSupportedException();
        public Task<PagedList<OnlineSessionItem>> ListOnlineAsync(SessionPageInput input) => throw new NotSupportedException();
    }

    private static ClaimsPrincipal PrincipalWith(string? sub, string? sid)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Sub, sub));
        if (sid is not null) claims.Add(new Claim(TokenClaimNames.SESSION_ID, sid));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Missing_sid_aborts_and_joins_no_group()
    {
        var groups = new FakeGroupManager();
        var ctx = new FakeHubCallerContext(PrincipalWith(sub: "1", sid: null));
        var hub = new TenonHub(new FakeSessionService(active: true)) { Context = ctx, Groups = groups };

        await hub.OnConnectedAsync();

        Assert.True(ctx.Aborted);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Inactive_sid_aborts_and_joins_no_group()
    {
        var groups = new FakeGroupManager();
        var ctx = new FakeHubCallerContext(PrincipalWith(sub: "1", sid: "revoked-sid"));
        var hub = new TenonHub(new FakeSessionService(active: false)) { Context = ctx, Groups = groups };

        await hub.OnConnectedAsync();

        Assert.True(ctx.Aborted);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Active_sid_joins_user_and_session_groups_without_aborting()
    {
        var groups = new FakeGroupManager();
        var ctx = new FakeHubCallerContext(PrincipalWith(sub: "42", sid: "live-sid"));
        var hub = new TenonHub(new FakeSessionService(active: true)) { Context = ctx, Groups = groups };

        await hub.OnConnectedAsync();

        Assert.False(ctx.Aborted);
        Assert.Contains(groups.Added, g => g.ConnectionId == ctx.ConnectionId && g.GroupName == "user-42");
        Assert.Contains(groups.Added, g => g.ConnectionId == ctx.ConnectionId && g.GroupName == "session-live-sid");
    }
}
