extern alias workflowhost;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TenonAdmin.Tests;

/// <summary>仅工作流集成测试启用卫星包,避免共享 TestHost 为无关测试建 wf_* 表。</summary>
public sealed class WorkflowAppFactory : WebApplicationFactory<workflowhost::WorkflowProgram>
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"tenon-wf-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("TenonAdmin:Database:DbType", TestDb.DbType);
        builder.UseSetting("TenonAdmin:Database:ConnectionString", TestDb.ConnectionString(DbPath, DbPath));
        builder.UseSetting("TenonAdmin:Seed:AdminPassword", "Test@123456");
        builder.UseSetting("TenonAdmin:Jwt:SecretKey", "tenon-workflow-test-signing-key-please-keep-32plus");
        builder.UseSetting("TenonAdmin:Security:DataProtection:Key", Convert.ToBase64String(new byte[32]));
        builder.UseSetting("TenonAdmin:Security:RateLimit:Enabled", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) TestDb.Cleanup(DbPath, DbPath);
    }
}
