using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 日志的"可查性"(C1/C4)——审计日志要能回答"谁在什么时候干了什么",否则等于没有:
/// <para>C1:操作日志可按操作人 / 时间范围 / 接口路径筛(此前只有操作名模糊 + 成败);登录日志可按时间范围筛。</para>
/// <para>C4:操作人被软删(=离职)后,历史日志仍显示其姓名,而不是回落成一串雪花 Id
/// ——恰恰是"离职那人走之前删了什么"这类审计最需要看清是谁的时候。</para>
/// </summary>
public class LogQueryTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Operation_log_filters_by_operator_time_and_path()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 造两类不同路径的写操作
        await admin.PostJson("/api/v1/sys/position/add", new { name = "审计岗", code = "audit-post", sort = 0, enabled = true });
        await admin.PostJson("/api/v1/sys/role/add", new { name = "审计角", code = "audit-role", sort = 0, enabled = true });

        // 按路径筛:只捞角色相关的动作
        var byPath = await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=50&Path=/api/v1/sys/role")).ReadEnvelope();
        var paths = byPath.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("path").GetString()!).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.Contains("/api/v1/sys/role", p));

        // 按操作人筛:超管的 Id 从任一条日志里取(它们都是超管干的)
        var operatorId = byPath.GetProperty("data").GetProperty("items").EnumerateArray()
            .First().GetProperty("operatorId").GetInt64();
        var byOperator = await (await admin.GetAsync($"/api/v1/sys/log/op/page?Current=1&Size=50&OperatorId={operatorId}")).ReadEnvelope();
        Assert.NotEmpty(byOperator.GetProperty("data").GetProperty("items").EnumerateArray());

        // 换一个不存在的操作人 → 空集(证明过滤器真的在生效,而不是被忽略)
        var byOther = await (await admin.GetAsync($"/api/v1/sys/log/op/page?Current=1&Size=50&OperatorId={operatorId + 1}")).ReadEnvelope();
        Assert.Empty(byOther.GetProperty("data").GetProperty("items").EnumerateArray());

        // 时间范围:未来的窗口 → 空集;含今天的窗口 → 有数据
        var future = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss");
        var farFuture = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd HH:mm:ss");
        var none = await (await admin.GetAsync($"/api/v1/sys/log/op/page?Current=1&Size=50&StartTime={Uri.EscapeDataString(future)}&EndTime={Uri.EscapeDataString(farFuture)}")).ReadEnvelope();
        Assert.Empty(none.GetProperty("data").GetProperty("items").EnumerateArray());

        var past = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        var some = await (await admin.GetAsync($"/api/v1/sys/log/op/page?Current=1&Size=50&StartTime={Uri.EscapeDataString(past)}&EndTime={Uri.EscapeDataString(future)}")).ReadEnvelope();
        Assert.NotEmpty(some.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Login_log_filters_by_time_range()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);   // 登录本身就写了一条登录日志

        var future = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss");
        var past = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");

        var some = await (await admin.GetAsync($"/api/v1/sys/log/login/page?Current=1&Size=50&StartTime={Uri.EscapeDataString(past)}&EndTime={Uri.EscapeDataString(future)}")).ReadEnvelope();
        Assert.NotEmpty(some.GetProperty("data").GetProperty("items").EnumerateArray());

        var farFuture = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd HH:mm:ss");
        var none = await (await admin.GetAsync($"/api/v1/sys/log/login/page?Current=1&Size=50&StartTime={Uri.EscapeDataString(future)}&EndTime={Uri.EscapeDataString(farFuture)}")).ReadEnvelope();
        Assert.Empty(none.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Deleted_user_still_shows_name_in_history()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        // 建一个用户 → 让他自己干一件事(留下带他 OperatorId 的操作日志)→ 管理员把他删掉(软删)
        var add = await (await admin.PostJson("/api/v1/sys/user",
            new { account = "leaver", password = "InitPass123", name = "离职员工", enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();
        var id = add.GetProperty("data").GetProperty("id").GetInt64();

        var anon = f.CreateClient();
        var token = (await (await anon.PostJson("/api/v1/auth/login",
            new { account = "leaver", password = "InitPass123" })).ReadEnvelope())
            .GetProperty("data").GetProperty("accessToken").GetString()!;
        var leaver = f.CreateClient();
        leaver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // [ActiveSession] 端点,无需权限码;是个写操作 → 现在会留痕(A1)
        await leaver.PutJson("/api/v1/personal/profile", new { name = "离职员工" });

        Assert.Equal(0, (await (await admin.DeleteAsync($"/api/v1/sys/user/{id}")).ReadEnvelope()).GetProperty("code").GetInt32());

        // 确认真被软删了(列表里查不到)
        using (var scope = f.Services.CreateScope())
        {
            var page = await scope.ServiceProvider.GetRequiredService<IUserService>()
                .PageAsync(new UserPageInput { Account = "leaver", Current = 1, Size = 10 });
            Assert.Empty(page.Items);
        }

        // 但历史操作日志里,他仍然是"离职员工",而不是一串雪花 Id
        var logs = await (await admin.GetAsync($"/api/v1/sys/log/op/page?Current=1&Size=100&OperatorId={id}")).ReadEnvelope();
        var items = logs.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, x => Assert.Equal("离职员工", x.GetProperty("operatorName").GetString()));
    }
}
