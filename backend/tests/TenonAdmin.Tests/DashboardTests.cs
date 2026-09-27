using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

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

    [Fact]
    public async Task Anonymous_summary_is_401_and_40006()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().GetAsync("/api/v1/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Any_logged_in_user_can_read_summary()
    {
        // 工作台挂 [ActiveSession],不要求路由权限码;无权限的普通用户也应读到 7 日趋势
        using var f = new AdminAppFactory();
        var c = await PingOnly(f);
        var resp = await c.GetAsync("/api/v1/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.ReadEnvelope();
        Assert.Equal(0, body.GetProperty("code").GetInt32());
        Assert.Equal(7, body.GetProperty("data").GetProperty("trendDays").GetArrayLength());
    }

    [Fact]
    public async Task Disabled_module_hides_summary()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Dashboard"] };
        var c = await SuperAdminClient(f);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/dashboard/summary")).StatusCode);
    }

    private static async Task<HttpClient> PingOnly(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var role = new SysRole
        {
            Name = "受限角色",
            Code = "limited-" + Guid.CreateVersion7().ToString("N")[..8],
            Enabled = true,
        };
        await roles.InsertAsync(role);
        await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, [2]);
        var account = "limited-" + Guid.CreateVersion7().ToString("N")[..8];
        const string password = "Limited@123456";
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account,
            Password = password,
            Name = "受限用户",
            Enabled = true,
            RoleIds = [role.Id],
        });
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));
        return c;
    }
}
