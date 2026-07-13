using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 工作台首页统计的 HTTP 级回归。这里唯一的非平凡逻辑是计数口径与近 7 日分组补零,
/// 所以断言就压在这两件事上:计数与种子数据对得上、趋势恰好 7 天且今天那格算上了本次登录。
/// </summary>
public class DashboardTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Summary_returns_real_counts_and_seven_day_trend()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);   // 这次登录本身会写一条成功的登录日志(今天)

        var data = (await (await c.GetAsync("/api/v1/dashboard/summary")).ReadEnvelope()).GetProperty("data");

        // 计数:种子数据保证角色/用户/权限点非零;刚登录过,必然至少一个活跃会话
        Assert.True(data.GetProperty("roles").GetInt32() > 0);
        Assert.True(data.GetProperty("users").GetInt32() > 0);
        Assert.True(data.GetProperty("perms").GetInt32() > 0);
        Assert.True(data.GetProperty("onlineSessions").GetInt32() > 0);

        // 趋势:恰好 7 天(无登录的日子补 0,不是跳过),三条数组等长
        var days = data.GetProperty("trendDays").EnumerateArray().Select(x => x.GetString()).ToList();
        var logins = data.GetProperty("trendLogins").EnumerateArray().Select(x => x.GetInt32()).ToList();
        var actives = data.GetProperty("trendActiveUsers").EnumerateArray().Select(x => x.GetInt32()).ToList();
        Assert.Equal(7, days.Count);
        Assert.Equal(7, logins.Count);
        Assert.Equal(7, actives.Count);

        // 今天是最后一格:本次登录必须被算进去(否则就是时区/分组把当天丢了)
        Assert.Equal(DateTime.Now.ToString("MM-dd"), days[^1]);
        Assert.True(logins[^1] > 0, "今天的登录数应当算上本次登录");
        Assert.True(actives[^1] > 0, "今天的活跃用户数应当算上超管本人");
    }
}
