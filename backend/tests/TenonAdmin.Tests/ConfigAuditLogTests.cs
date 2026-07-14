using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 配置写操作必须留操作日志:配置中心改的是密码策略/登录锁定/会话时长/限流阈值等安全开关,
/// 改了却无痕 = 安全配置被人悄悄放松却查不到是谁、何时。锁死 batch 写入留痕。
/// </summary>
public class ConfigAuditLogTests
{
    [Fact]
    public async Task Batch_config_write_is_audit_logged()
    {
        using var f = new AdminAppFactory();
        var admin = f.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await admin.LoginToken("superAdmin", "Test@123456"));

        // 改密码最小长度(已播种的安全键)
        var save = await (await admin.PutJson("/api/v1/sys/config/batch",
            new[] { new { configKey = "sys.security.password.minLength", configValue = "10" } })).ReadEnvelope();
        Assert.Equal(0, save.GetProperty("code").GetInt32());

        var logs = (await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var entry = logs.FirstOrDefault(l => l.GetProperty("httpMethod").GetString() == "PUT"
            && l.GetProperty("path").GetString() == "/api/v1/sys/config/batch");
        Assert.True(entry.ValueKind != System.Text.Json.JsonValueKind.Undefined, "配置批量写入未留操作日志");
        Assert.Equal("批量修改配置", entry.GetProperty("title").GetString());
    }
}
