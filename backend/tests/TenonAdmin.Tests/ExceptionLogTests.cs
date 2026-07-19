using System.Net;
using System.Net.Http.Headers;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 异常日志(B1)——未捕获异常(程序缺陷)要留痕以便排障,但不能吞掉 500;业务异常是可预期分支,不进异常表。
/// <para>诊断端点 <c>/api/v1/diag/throw*</c> 由 TestHost 提供(仅测试宿主),故意抛异常供本用例验证过滤器契约。</para>
/// </summary>
public class ExceptionLogTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Unhandled_exception_is_logged_with_context_and_still_500()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var resp = await admin.GetAsync("/api/v1/diag/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);   // 500 照旧——异常未被吞

        // 落了一条,带路径 / 类型 / 消息 / 触发人回填
        var page = await (await admin.GetAsync(
            "/api/v1/sys/log/exception/page?Current=1&Size=50&Path=/api/v1/diag/throw")).ReadEnvelope();
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        var top = items[0];
        Assert.Equal("/api/v1/diag/throw", top.GetProperty("path").GetString());
        Assert.Contains("InvalidOperationException", top.GetProperty("exceptionType").GetString());
        Assert.Contains("boom-diag", top.GetProperty("message").GetString());
        Assert.False(string.IsNullOrEmpty(top.GetProperty("traceId").GetString()));
        // 触发人身份回填(超管有 sys_user 行)
        Assert.True(top.GetProperty("operatorId").GetInt64() > 0);
        Assert.False(string.IsNullOrEmpty(top.GetProperty("operatorName").GetString()));

        // 按异常类型筛真的在生效:命中类型有数据,不存在的类型空集
        var byType = await (await admin.GetAsync(
            "/api/v1/sys/log/exception/page?Current=1&Size=50&ExceptionType=InvalidOperation")).ReadEnvelope();
        Assert.NotEmpty(byType.GetProperty("data").GetProperty("items").EnumerateArray());
        var byOther = await (await admin.GetAsync(
            "/api/v1/sys/log/exception/page?Current=1&Size=50&ExceptionType=NoSuchExceptionType")).ReadEnvelope();
        Assert.Empty(byOther.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Business_exception_is_not_logged_and_returns_envelope()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var resp = await admin.GetAsync("/api/v1/diag/throw-business");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // 业务异常 → 200 信封,不是 500
        var env = await resp.ReadEnvelope();
        Assert.Equal((int)ErrorCode.PasswordWrong, env.GetProperty("code").GetInt32());

        // 业务异常不进异常表
        var page = await (await admin.GetAsync(
            "/api/v1/sys/log/exception/page?Current=1&Size=50&Path=/api/v1/diag/throw-business")).ReadEnvelope();
        Assert.Empty(page.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Clear_empties_the_exception_log()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        await admin.GetAsync("/api/v1/diag/throw");
        var before = await (await admin.GetAsync("/api/v1/sys/log/exception/page?Current=1&Size=50")).ReadEnvelope();
        Assert.NotEmpty(before.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(0, (await (await admin.DeleteAsync("/api/v1/sys/log/exception")).ReadEnvelope())
            .GetProperty("code").GetInt32());

        var after = await (await admin.GetAsync("/api/v1/sys/log/exception/page?Current=1&Size=50")).ReadEnvelope();
        Assert.Empty(after.GetProperty("data").GetProperty("items").EnumerateArray());
    }
}
