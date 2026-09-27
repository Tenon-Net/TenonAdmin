using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

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

    [Fact]
    public async Task Anonymous_server_info_is_401_and_40006()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().GetAsync("/api/v1/sys/monitor/server");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(40006, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task User_without_monitor_permission_is_403()
    {
        using var f = new AdminAppFactory();
        var c = await PingOnly(f);
        var resp = await c.GetAsync("/api/v1/sys/monitor/server");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(41001, (await resp.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Disabled_module_hides_server_info()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Monitor"] };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/sys/monitor/server")).StatusCode);
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
