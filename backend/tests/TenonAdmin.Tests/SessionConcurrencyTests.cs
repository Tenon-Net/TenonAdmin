using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 会话并发上限在<b>没有进程内锁</b>时依然成立(§15 / T-D3)。
///
/// <para><b>为什么原来的写法必须靠锁</b>:<c>EnforceConcurrencyAsync</c> 跑在插入<b>之前</b>("腾位"):
/// 读活跃集合 → 算该踢几个 → 踢 → 再插入。这是个读-改-写,并发下两个登录各自读到<b>同一份</b>旧集合、
/// 各自都算出"不用踢",于是双双插入 → 上限被突破。原实现用一个 <c>static</c> 的进程内锁字典串行化它,
/// 但那把锁<b>只在一个进程里有效</b>:两个副本上的并发登录照样各读各的,
/// <c>SessionMode.Single</c>(挤下线)踢不掉旧会话、<c>MaxConcurrent</c> 被突破。换 Redis 也修不好——这不是缓存问题。</para>
///
/// <para><b>改法:先插入、再收敛</b>。插完再读,两个并发登录<b>都能看见对方的新行</b>,
/// 于是都算出同一个"只保留最新 N 个"的答案 → 收敛到上限。吊销是幂等的(只写 <c>RevokedAt IS NULL</c> 的行),
/// 重复吊销无害。不需要任何锁,也就不需要分布式锁。</para>
///
/// <para>注意:原来那把锁是 <c>static</c>,同进程内的两个 host 会<b>共用</b>它——所以"起两个 host"并不能
/// 在测试里复现这个 bug。真正能证伪的实验是<b>去掉锁</b>:去掉后 enforce-before-insert 立刻溢出上限,
/// 而 insert-then-trim 依然收敛。本用例测的就是后者(单 host 内并发登录,不依赖任何锁)。</para>
/// </summary>
public class SessionConcurrencyTests
{
    private const string ACCOUNT = "superAdmin";
    private const string PASSWORD = "Test@123456";

    private static AdminAppFactory Factory(string mode, int maxConcurrent) => new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["TenonAdmin:Security:Session:Mode"] = mode,
            ["TenonAdmin:Security:Session:MaxConcurrent"] = maxConcurrent.ToString(),
        },
    };

    /// <summary>该用户当前仍然活跃的会话(未吊销、未过期)。</summary>
    private static async Task<List<SysSession>> ActiveSessions(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();
        var now = DateTime.UtcNow;
        return await sessions.AsQueryable()
            .Where(s => s.Account == ACCOUNT && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync();
    }

    /// <summary>同一个账号同时发起 <paramref name="count"/> 次登录(模拟负载均衡下的并发登录)。</summary>
    private static async Task LoginConcurrently(AdminAppFactory f, int count)
    {
        var logins = Enumerable.Range(0, count).Select(async _ =>
        {
            var client = f.CreateClient();
            await client.PostJson("/api/v1/auth/login", new { account = ACCOUNT, password = PASSWORD });
        });
        await Task.WhenAll(logins);
    }

    [Fact]
    public async Task Concurrent_logins_converge_to_the_max_concurrent_cap()
    {
        using var f = Factory("Multi", maxConcurrent: 2);

        await LoginConcurrently(f, 4);   // 4 个并发登录,上限 2

        // 先插入再收敛:每个登录插完都重读、都看得见彼此,都把多余的最旧会话吊销 → 收敛到 2
        Assert.Equal(2, (await ActiveSessions(f)).Count);
    }

    [Fact]
    public async Task Single_mode_leaves_exactly_one_live_session_under_concurrency()
    {
        using var f = Factory("Single", maxConcurrent: 0);   // 单端:新登录踢掉该用户其他所有会话

        await LoginConcurrently(f, 3);

        // "最新的那个赢"——与原来串行执行(后来者踢掉先来者)的语义一致
        Assert.Single(await ActiveSessions(f));
    }
}
