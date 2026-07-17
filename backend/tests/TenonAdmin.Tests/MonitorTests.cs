using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 服务器监控(B5)——进程/主机基础指标端点返回合理形状(CPU 归一 0–100、处理器数为正、磁盘列表存在)。
/// </summary>
public class MonitorTests
{
    [Fact]
    public async Task Server_info_returns_sane_snapshot()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var data = (await (await c.GetAsync("/api/v1/sys/monitor/server")).ReadEnvelope()).GetProperty("data");

        Assert.False(string.IsNullOrEmpty(data.GetProperty("machineName").GetString()));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("frameworkDescription").GetString()));
        Assert.True(data.GetProperty("processorCount").GetInt32() > 0);
        Assert.True(data.GetProperty("processUptimeSeconds").GetInt64() >= 0);

        var cpu = data.GetProperty("processCpuPercent").GetDouble();
        Assert.InRange(cpu, 0, 100);

        Assert.True(data.GetProperty("processWorkingSetBytes").GetInt64() > 0);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, data.GetProperty("disks").ValueKind);
    }
}
