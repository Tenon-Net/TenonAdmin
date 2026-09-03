using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b T2 入口红测：Webhook 进入流程时必须在工作流事务内只创建/复用
/// <c>wf_node_execution</c>，不能在入口事务里调用 handler。
/// </summary>
public class WfNodeExecutionEntryTests
{
    [Fact]
    public async Task Entering_webhook_creates_one_pending_execution_without_invoking_a_handler()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded());
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.Insert(
                0,
                ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => handler)),
        };
        _ = f.CreateClient();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var model = WebhookModel();
        var version = await InsertVersionAsync(db, model);

        var result = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        Assert.Equal(WfInstanceStatus.Running, result.InstanceStatus);
        Assert.Equal(0, handler.CallCount);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == result.InstanceId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        Assert.NotNull(token);
        Assert.Equal("webhook", token!.NodeId);
        Assert.NotNull(token.NodeVisitId);

        var executions = await db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == result.InstanceId)
            .ToListAsync();
        var execution = Assert.Single(executions);
        Assert.Equal(WfNodeExecutionStatus.Pending, execution.Status);
        Assert.Equal(result.InstanceId, execution.InstanceId);
        Assert.Equal(token.Id, execution.TokenId);
        Assert.Equal(token.NodeVisitId, execution.NodeVisitId);
        Assert.Equal(WfNodeType.Webhook, execution.NodeType);
        Assert.Equal(
            WfExecutionKey.Compute("1", result.InstanceId, token.Id, token.NodeVisitId, "webhook", version.Id),
            execution.ExecutionKey);
    }

    [Fact]
    public async Task Replaying_the_same_node_visit_reuses_the_existing_execution_row()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ctx = await BuildContextAsync(scope.ServiceProvider, db);
        var webhook = ctx.Model.Root.Next!;

        var first = await db.Ado.UseTranAsync(() =>
            new EnterNodeOp(webhook).ExecuteAsync(ctx, CancellationToken.None));
        Assert.True(first.IsSuccess, first.ErrorException?.ToString());

        var second = await db.Ado.UseTranAsync(() =>
            new EnterNodeOp(webhook).ExecuteAsync(ctx, CancellationToken.None));
        Assert.True(second.IsSuccess, second.ErrorException?.ToString());

        var executions = await db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == ctx.Instance.Id && e.TokenId == ctx.Token.Id)
            .ToListAsync();
        var execution = Assert.Single(executions);
        Assert.Equal(ctx.Token.NodeVisitId, execution.NodeVisitId);
        Assert.Equal(
            WfExecutionKey.Compute("1", ctx.Instance.Id, ctx.Token.Id, ctx.Token.NodeVisitId, "webhook", ctx.DefinitionVersion.Id),
            execution.ExecutionKey);
    }

    [Fact]
    public async Task Rolling_back_after_webhook_entry_leaves_no_execution_or_token_history()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ctx = await BuildContextAsync(scope.ServiceProvider, db);
        var webhook = ctx.Model.Root.Next!;

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            await new EnterNodeOp(webhook).ExecuteAsync(ctx, CancellationToken.None);
            throw new InvalidOperationException("强制回滚，验证 Webhook 入口不留下半条执行链。");
        });

        Assert.False(tran.IsSuccess);

        var token = await db.Queryable<WfToken>().Where(t => t.Id == ctx.Token.Id).FirstAsync();
        Assert.Equal("start", token!.NodeId);
        Assert.Null(token.NodeVisitId);
        Assert.Equal(0, token.Version);
        Assert.Equal(0, await db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == ctx.Instance.Id)
            .CountAsync());
        Assert.Equal(0, await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == ctx.Instance.Id)
            .CountAsync());
    }

    private static WfModel WebhookModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "webhook",
                Type = WfNodeType.Webhook,
                Name = "webhook",
                Props = new WfNodeProps { WebhookUrl = "http://127.0.0.1:59999/webhook" },
            },
        },
    };

    private static async Task<WfDefinitionVersion> InsertVersionAsync(
        ISqlSugarClient db,
        WfModel model)
    {
        var version = new WfDefinitionVersion
        {
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = WfModelJson.Serialize(model),
        };
        await db.Insertable(version).ExecuteCommandAsync();
        return version;
    }

    private static async Task<WfExecutionContext> BuildContextAsync(
        IServiceProvider services,
        ISqlSugarClient db)
    {
        var model = WebhookModel();
        var version = await InsertVersionAsync(db, model);
        var instance = new WfInstance
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            Status = WfInstanceStatus.Running,
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = "start",
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        return new WfExecutionContext
        {
            Db = db,
            Agenda = new WfAgenda(),
            ApproverResolver = services.GetRequiredService<IApproverResolver>(),
            FormBinder = services.GetRequiredService<IWorkflowFormBinder>(),
            Options = services.GetRequiredService<WorkflowOptions>(),
            TimeProvider = services.GetRequiredService<TimeProvider>(),
            ConditionEvaluator = services.GetRequiredService<IWfConditionEvaluator>(),
            Notifier = services.GetRequiredService<IWorkflowNotifier>(),
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            RequestId = null,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = 1,
            IdGenerator = new FixedIdGenerator(7_001),
            StarterOrgId = 1,
        };
    }

    private sealed class FixedIdGenerator(long value) : IIdGenerator
    {
        public long NextId() => value;
    }
}
