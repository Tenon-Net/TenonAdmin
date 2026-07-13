using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 生产建表闸门(§12)的启动契约。闸门只挡建表,种子仍要跑——这中间的空档曾让"空库首次上生产"
/// 崩在驱动层的 no such table 上。两个用例锁住契约的两半:
/// <list type="bullet">
/// <item>表不存在 → 抛出**可行动**的错误(点名缺表 + 给出两条出路),而不是驱动层错误。</item>
/// <item>表已由 DBA 建好 → 闸门虽关,种子**照常执行**(超管账号与菜单树必须写入,否则交付一个空系统)。</item>
/// </list>
/// </summary>
public class ProductionBootstrapTests
{
    [Fact]
    public void Production_with_empty_db_and_gate_off_fails_with_actionable_message()
    {
        // 生产 + 闸门默认关 + 空库 = 消费者第一次上生产的默认配置
        using var f = new AdminAppFactory { EnvironmentName = "Production" };

        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());   // CreateClient 触发宿主启动

        // 断消息内容而非仅异常类型:别的启动前置(如 JWT 密钥缺失)也抛同类异常,只断类型会误判为通过
        Assert.Contains("sys_schema_version", ex.Message);                 // 点名到底缺哪张表
        Assert.Contains("EnableCodeFirstInProduction", ex.Message);        // 给出出路
    }

    [Fact]
    public async Task Production_with_pre_created_schema_still_runs_seeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-prodboot-{Guid.NewGuid():N}.db");

        // 1) 先造一个"有表无数据"的库(模拟 DBA 手工建好表结构):建表开、种子关
        using (var dba = new AdminAppFactory
        {
            DbPath = dbPath,
            DeleteDbOnDispose = false,
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Database:EnableSeed"] = "false" },
        })
            _ = dba.CreateClient();

        // 2) 同库上以生产启动:闸门关(不建表),但种子必须照跑
        using var f = new AdminAppFactory { DbPath = dbPath, EnvironmentName = "Production" };
        using var scope = f.Services.CreateScope();   // 触发宿主启动 → 探表通过 → 种子执行
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

        Assert.True(await users.AnyAsync(u => u.IsSuperAdmin));   // 超管种子确实写进了 DBA 建好的表
    }
}
