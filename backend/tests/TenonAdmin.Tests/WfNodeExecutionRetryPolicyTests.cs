using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b 重试预算契约：锁定节点级/模块全局/内置默认来源、合法范围、非法配置
/// 和 execution 创建时快照。配置入口与 execution 快照已经接入生产路径；反射只用于
/// 保持对公开配置形状和原始模型 JSON 的精确断言。
/// </summary>
public class WfNodeExecutionRetryPolicyTests
{
    [Fact]
    public async Task Missing_configuration_uses_the_builtin_max_attempts_default()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, WebhookModelJson());

        var result = await StartAsync(engine, version.Id);
        var execution = await SingleExecutionAsync(db, result.InstanceId);

        Assert.Equal(3, execution.MaxAttempts);
    }

    [Fact]
    public async Task A_valid_global_max_attempts_value_is_snapshotted_when_execution_is_created()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<WorkflowOptions>();
        SetMaxAttempts(options, 7);
        var engine = services.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, WebhookModelJson());

        var result = await StartAsync(engine, version.Id);
        var execution = await SingleExecutionAsync(db, result.InstanceId);

        Assert.Equal(7, execution.MaxAttempts);
    }

    [Fact]
    public async Task A_node_max_attempts_value_overrides_the_global_value()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var options = scope.ServiceProvider.GetRequiredService<WorkflowOptions>();
        SetMaxAttempts(options, 7);
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, WebhookModelJson(nodeMaxAttempts: 9));

        var result = await StartAsync(engine, version.Id);
        var execution = await SingleExecutionAsync(db, result.InstanceId);

        Assert.Equal(9, execution.MaxAttempts);
    }

    [Fact]
    public async Task A_created_execution_keeps_its_budget_after_configuration_changes()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var options = scope.ServiceProvider.GetRequiredService<WorkflowOptions>();
        SetMaxAttempts(options, 6);
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, WebhookModelJson(nodeMaxAttempts: 8));

        var result = await StartAsync(engine, version.Id);
        var before = await SingleExecutionAsync(db, result.InstanceId);

        SetMaxAttempts(options, 99);
        var after = await SingleExecutionAsync(db, result.InstanceId);

        Assert.Equal(8, before.MaxAttempts);
        Assert.Equal(8, after.MaxAttempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Global_max_attempts_accepts_the_declared_inclusive_bounds(int value)
    {
        var configuration = ConfigurationFor(value);
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddTenonAdminWorkflow(configuration));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Global_max_attempts_rejects_values_outside_the_declared_range(int value)
    {
        var configuration = ConfigurationFor(value);
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddTenonAdminWorkflow(configuration));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task A_node_max_attempts_outside_the_declared_range_rolls_back_entry()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, WebhookModelJson(nodeMaxAttempts: 0));

        var exception = await Record.ExceptionAsync(() => StartAsync(engine, version.Id));

        Assert.NotNull(exception);
        Assert.Equal(0, await db.Queryable<WfNodeExecution>().CountAsync());
    }

    private static async Task<WfEngineResult> StartAsync(IWorkflowEngine engine, long versionId) =>
        await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = versionId,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

    private static async Task<WfDefinitionVersion> InsertVersionAsync(
        ISqlSugarClient db,
        string modelJson)
    {
        var version = new WfDefinitionVersion
        {
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = modelJson,
        };
        await db.Insertable(version).ExecuteCommandAsync();
        return version;
    }

    private static async Task<WfNodeExecution> SingleExecutionAsync(ISqlSugarClient db, long instanceId)
    {
        var execution = await db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == instanceId)
            .FirstAsync();
        Assert.NotNull(execution);
        return execution!;
    }

    private static IConfiguration ConfigurationFor(int maxAttempts) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TenonAdmin:Workflow:MaxAttempts"] = maxAttempts.ToString(),
            })
            .Build();

    private static void SetMaxAttempts(WorkflowOptions options, int value)
    {
        var property = typeof(WorkflowOptions).GetProperty(
            "MaxAttempts",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(typeof(int), property!.PropertyType);
        property.SetValue(options, value);
    }

    private static string WebhookModelJson(int? nodeMaxAttempts = null)
    {
        var maxAttempts = nodeMaxAttempts is null
            ? ""
            : $",\"maxAttempts\":{nodeMaxAttempts.Value}";
        return "{\"version\":1,\"root\":{" +
               "\"id\":\"start\",\"type\":\"start\",\"name\":\"\",\"next\":{" +
               "\"id\":\"webhook\",\"type\":\"webhook\",\"name\":\"webhook\",\"props\":{" +
               "\"webhookUrl\":\"http://127.0.0.1:59999/webhook\"" + maxAttempts +
               "}}}}";
    }
}
