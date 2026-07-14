using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 反向代理后取真实客户端 IP(§14)。限流分区、登录日志、爆破防护全挂在"客户端 IP"这一个值上——
/// 而反代之后 <c>Connection.RemoteIpAddress</c> 是<b>代理的 IP</b>,不接 <c>ForwardedHeaders</c> 时:
/// 全体用户共享同一个限流桶,登录日志记的也是代理 IP(审计里的 IP 列作废)。
/// <para>观测点选<b>登录日志的 Ip 列</b>:它经 <c>ICurrentUser.IpAddress</c> → <c>Connection.RemoteIpAddress</c>,
/// 是这条链路的终点,且由真实登录流程同步写入(<c>LogService.RecordLoginAsync</c>)。</para>
/// <para><b>安全红线</b>:无条件采信 <c>X-Forwarded-For</c> 比不修更糟——攻击者每个请求换一个伪造 IP
/// 即可无限开新限流分区(限流被完全绕过),还能往别人头上栽爆破记录。故受信来源必须显式声明。</para>
/// </summary>
public class ForwardedHeadersTests
{
    private const string PROXY_IP = "127.0.0.1";        // 扮演反向代理:请求从它这里进来
    private const string CLIENT_IP = "203.0.113.7";     // 真实客户端(TEST-NET-3,文档保留段)
    private const string ATTACKER_IP = "198.51.100.1";  // 未被信任的直连来源(TEST-NET-2)

    /// <summary>用给定的连接来源 IP 发请求(TestServer 默认 RemoteIpAddress 为 null,必须显式设定)。</summary>
    private static HttpClient ClientFrom(AdminAppFactory f, string connectionIp)
    {
        var handler = f.Server.CreateHandler(ctx => ctx.Connection.RemoteIpAddress = IPAddress.Parse(connectionIp));
        return new HttpClient(handler) { BaseAddress = f.Server.BaseAddress };
    }

    /// <summary>读该账号最近一条登录日志的 IP。</summary>
    private static async Task<string?> LastLoginIp(AdminAppFactory f, string account)
    {
        using var scope = f.Services.CreateScope();
        var logs = scope.ServiceProvider.GetRequiredService<IRepository<SysLoginLog>>();
        var list = await logs.AsQueryable().Where(l => l.Account == account).OrderByDescending(l => l.Id).ToListAsync();
        return list.FirstOrDefault()?.Ip;
    }

    private static AdminAppFactory WithForwardedHeaders(params string[] knownProxies)
    {
        var settings = new Dictionary<string, string?> { ["TenonAdmin:Api:ForwardedHeaders:Enabled"] = "true" };
        for (var i = 0; i < knownProxies.Length; i++)
            settings[$"TenonAdmin:Api:ForwardedHeaders:KnownProxies:{i}"] = knownProxies[i];
        return new AdminAppFactory { Settings = settings };
    }

    [Fact]
    public async Task ForwardedFor_from_trusted_proxy_becomes_the_client_ip()
    {
        using var f = WithForwardedHeaders(PROXY_IP);
        var c = ClientFrom(f, PROXY_IP);
        c.DefaultRequestHeaders.Add("X-Forwarded-For", CLIENT_IP);

        var res = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // 代理受信 → 采信 XFF:记的是真实客户端,而不是代理
        Assert.Equal(CLIENT_IP, await LastLoginIp(f, "superAdmin"));
    }

    [Fact]
    public async Task ForwardedFor_from_untrusted_source_is_ignored()
    {
        // 只信 PROXY_IP;请求却从 ATTACKER_IP 直连进来,并自带一个伪造的 XFF
        using var f = WithForwardedHeaders(PROXY_IP);
        var c = ClientFrom(f, ATTACKER_IP);
        c.DefaultRequestHeaders.Add("X-Forwarded-For", CLIENT_IP);

        var res = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // 来源不受信 → 伪造的头被无视,仍记连接 IP。这条是防 IP 伪造的钉子。
        Assert.Equal(ATTACKER_IP, await LastLoginIp(f, "superAdmin"));
    }

    [Fact]
    public async Task Disabled_by_default_ignores_ForwardedFor()
    {
        using var f = new AdminAppFactory();   // 未开启:默认不在代理后
        var c = ClientFrom(f, ATTACKER_IP);
        c.DefaultRequestHeaders.Add("X-Forwarded-For", CLIENT_IP);

        await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });

        Assert.Equal(ATTACKER_IP, await LastLoginIp(f, "superAdmin"));
    }

    [Fact]
    public void Enabled_without_any_trusted_source_fails_fast()
    {
        // 开了转发头却不声明受信来源 = 采信任何人的 X-Forwarded-For = 限流可被伪造 IP 完全绕过。
        // 宁可启动就炸,也不静默留一个可绕过的限流器(同 JwtKeyResolver 生产缺密钥的成法)。
        using var f = WithForwardedHeaders();   // 一个受信来源都不给
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("ForwardedHeaders", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
