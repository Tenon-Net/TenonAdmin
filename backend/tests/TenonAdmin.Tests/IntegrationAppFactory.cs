extern alias integrationhost;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TenonAdmin.Tests;

/// <summary>
/// 第三方接入集成测试工厂:只在这里启用 <c>TenonAdmin.Integration</c>,避免共享 TestHost 为无关测试建 <c>itg_*</c> 表。
/// <para>多副本用例:第二个宿主传同一 <see cref="DbPath"/>、<see cref="ResetDatabase"/>=false 与不同 <see cref="WorkerId"/>,
/// 两个宿主即共享同一个库(与 <see cref="WorkflowAppFactory"/> 同一做法)。</para>
/// </summary>
public sealed class IntegrationAppFactory : WebApplicationFactory<integrationhost::IntegrationProgram>
{
    public string DbPath { get; init; } = Path.Combine(Path.GetTempPath(), $"tenon-itg-it-{Guid.NewGuid():N}.db");

    /// <summary>是否为当前测试库重置模板;并发测试的第二个宿主复用首个宿主已建好的库。</summary>
    public bool ResetDatabase { get; init; } = true;

    /// <summary>可选的 Snowflake WorkerId;多宿主共享同库时各用不同机器号。</summary>
    public int? WorkerId { get; init; }

    /// <summary>Dispose 时是否删库(多宿主共享库时,只让最后释放的那个删)。</summary>
    public bool DeleteDbOnDispose { get; init; } = true;

    /// <summary>每测试的服务覆盖(ConfigureTestServices)。</summary>
    public Action<IServiceCollection>? Overrides { get; init; }

    /// <summary>额外配置项(在 AddTenonAdmin/AddTenonAdminIntegration 绑定前生效)。</summary>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    /// <summary>宿主环境名(默认 Development;生产行为用例传 "Production",会同时允许生产建表)。</summary>
    public string EnvironmentName { get; init; } = "Development";

    /// <summary>
    /// <see cref="Settings"/> 与 <see cref="Overrides"/> 不影响表结构和种子(如只改出站目标、保留批大小,或替换出站服务)时置真:
    /// 服务器方言下沿用本进程预建的模板库,省去每个宿主一次完整 CodeFirst。模板每个测试进程按当前构建重建,不会过期。
    /// </summary>
    public bool SchemaNeutral { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        if (EnvironmentName == "Production")
            builder.UseSetting("TenonAdmin:Database:EnableCodeFirstInProduction", "true");
        builder.UseSetting("TenonAdmin:Database:DbType", TestDb.DbType);
        builder.UseSetting("TenonAdmin:Database:ConnectionString", TestDb.ConnectionString(DbPath, DbPath, "integration", ResetDatabase));
        if (WorkerId is { } workerId)
            builder.UseSetting("TenonAdmin:Id:WorkerId", workerId.ToString());
        builder.UseSetting("TenonAdmin:Id:WorkerIdLockDir", AdminAppFactory.WorkerIdLockDirFor(DbPath));
        if (TestDb.SchemaTemplateEnabled && (SchemaNeutral || (Overrides is null && Settings is null)) && EnvironmentName == "Development"
            && !TestDb.IsSchemaTemplateInitialization)
        {
            builder.UseSetting("TenonAdmin:Database:EnableCodeFirst", "false");
            builder.UseSetting("TenonAdmin:Database:EnableSeed", "false");
        }
        builder.UseSetting("TenonAdmin:Seed:AdminPassword", "Test@123456");
        builder.UseSetting("TenonAdmin:Jwt:SecretKey", "tenon-integration-module-test-key-please-keep-32plus");
        builder.UseSetting("TenonAdmin:Security:DataProtection:Key", Convert.ToBase64String(new byte[32]));
        builder.UseSetting("TenonAdmin:Security:RateLimit:Enabled", "false");
        // 模块会播种后台任务(投递扫描/保留清理);测试一律手动驱动,关掉真调度器避免与用例并发操作同一批行。
        builder.UseSetting("TenonAdmin:Jobs:SchedulerEnabled", "false");
        if (Settings != null)
            foreach (var kv in Settings) builder.UseSetting(kv.Key, kv.Value);
        if (Overrides != null) builder.ConfigureTestServices(Overrides);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && DeleteDbOnDispose && !TestDb.IsSchemaTemplateInitialization)
        {
            TestDb.Cleanup(DbPath, DbPath);
            AdminAppFactory.TryDeleteWorkerIdLockDir(DbPath);
        }
    }
}
