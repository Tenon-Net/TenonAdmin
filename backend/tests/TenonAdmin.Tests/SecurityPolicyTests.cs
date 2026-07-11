using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 安全策略(密码复杂度)的 HTTP 级回归:弱口令拒 / 强口令过 / 策略运行时可配。
/// 第三例同时证明 SysConfig 新值被后端强制执行读到 + 缓存即时失效(改配置不重发)。
/// </summary>
public class SecurityPolicyTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task Weak_new_password_is_rejected()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // "abc":长度不足且缺字符类 → 42019 PasswordTooWeak
        var resp = await c.PutJson("/api/v1/personal/password", new { oldPassword = "Test@123456", newPassword = "abc" });
        Assert.Equal(42019, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Strong_new_password_is_accepted()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 默认策略(minLength=8,须含大写/小写/数字,特殊字符不强制):"Newpass123" 满足 → code 0
        var resp = await c.PutJson("/api/v1/personal/password", new { oldPassword = "Test@123456", newPassword = "Newpass123" });
        Assert.Equal(0, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Password_policy_is_runtime_configurable_and_cache_invalidates()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 默认 minLength=8:len-10 口令通过(顺带把 minLength=8 读进读穿透缓存)
        var ok = await c.PutJson("/api/v1/personal/password", new { oldPassword = "Test@123456", newPassword = "Newpass123" });
        Assert.Equal(0, (await ok.ReadEnvelope()).GetProperty("code").GetInt32());

        // 运行时把最小长度提到 20,并即时失效缓存
        var batch = await c.PutJson("/api/v1/sys/config/batch", new object[]
        {
            new { configKey = "sys.security.password.minLength", configValue = "20" },
        });
        Assert.Equal(0, (await batch.ReadEnvelope()).GetProperty("code").GetInt32());

        // 同样 len-10 口令现在被拒:证明后端读到 DB 新值(20)且缓存已失效(否则仍见旧值 8 会通过)
        var rejected = await c.PutJson("/api/v1/personal/password", new { oldPassword = "Newpass123", newPassword = "Another99" });
        Assert.Equal(42019, (await rejected.ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
