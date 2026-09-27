using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b T10 并发与恢复测试：覆盖 handler 返回前中断、tx2 提交前崩溃后的新 owner 恢复，
/// 并把「外部副作用至少一次、本地 token 最多推进一次」同时落成可查断言。
/// </summary>
public class WfNodeExecutionRecoveryTests
{
    [Fact]
    public async Task A_handler_interruption_before_return_is_reclaimed_by_the_worker()
    {
        var handler = new InterruptingOnceHandler();
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, 41);
        var worker = scope.ServiceProvider.GetServices<IAdminJob>()
            .OfType<WfNodeExecutionJob>()
            .Single();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.ExecuteAsync(JobContext(), CancellationToken.None));

        var afterInterruption = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, afterInterruption.Status);
        Assert.Equal(1, afterInterruption.AttemptCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));

        await ExpireLeaseAsync(db, execution.Id);
        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var recovered = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal(2, handler.CallCount);

        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(2, attempt.AttemptNo);
        Assert.Equal(WfNodeExecutionResultType.Succeeded, attempt.ResultType);
        Assert.Equal(WfTokenStatus.Completed, await TokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatusAsync(db, execution.InstanceId));
        Assert.Single(await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == execution.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task A_commit_crash_after_an_external_side_effect_recovers_without_advancing_the_token_twice()
    {
        var appliedKeys = new HashSet<string>(StringComparer.Ordinal);
        var handler = new IdempotentSideEffectHandler(appliedKeys);
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var realEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var execution = await InsertExecutionAsync(db, 42);

        var crashingDispatcher = new WfNodeExecutionDispatcher(
            db,
            [handler],
            new CrashBeforeCommitEngine(),
            TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crashingDispatcher.RunAsync(
                execution.Id,
                "worker-a",
                TimeSpan.FromMinutes(5),
                CancellationToken.None));

        var afterCrash = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, afterCrash.Status);
        Assert.Equal(1, afterCrash.AttemptCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(1, handler.ExternalCallCount);
        Assert.Single(appliedKeys);

        await ExpireLeaseAsync(db, execution.Id);
        var recoveringDispatcher = new WfNodeExecutionDispatcher(
            db,
            [handler],
            realEngine,
            TimeProvider.System);
        var status = await recoveringDispatcher.RunAsync(
            execution.Id,
            "worker-b",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Equal(WfNodeExecutionStatus.Succeeded, status);
        Assert.Equal(2, handler.ExternalCallCount); // at-least-once:外部调用可能重复
        Assert.Single(appliedKeys);                  // ExecutionKey 让下游实际生效只一次

        var recovered = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(2, attempt.AttemptNo);
        Assert.Single(await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(WfTokenStatus.Completed, await TokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatusAsync(db, execution.InstanceId));
        Assert.Equal(1, await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .CountAsync());
    }

    private static WorkflowAppFactory NewFactory(IWorkflowNodeHandler handler) => new()
    {
        Overrides = services => services.Insert(
            0,
            ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => handler)),
    };

    private static JobExecutionContext JobContext() => new()
    {
        JobId = 1,
        JobCode = "wf-node-execution-scan",
        JobName = "工作流节点执行扫描",
        FireInstanceId = 1,
        ScheduledTime = DateTime.Now,
        FireTime = DateTime.Now,
    };

    private static async Task<WfNodeExecution> InsertExecutionAsync(ISqlSugarClient db, int tag)
    {
        var model = new WfModel
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
        var version = new WfDefinitionVersion
        {
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = WfModelJson.Serialize(model),
        };
        await db.Insertable(version).ExecuteCommandAsync();

        var instance = new WfInstance
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            Status = WfInstanceStatus.Running,
            BusinessKey = $"recovery-{tag}",
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        const long nodeVisitIdBase = 40_000;
        var nodeVisitId = nodeVisitIdBase + tag;
        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = "webhook",
            NodeVisitId = nodeVisitId,
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        var scopeKey = WfIdentityHash.NormalizeScopeKey(null);
        var execution = new WfNodeExecution
        {
            ExecutionKey = WfExecutionKey.Compute(
                scopeKey, instance.Id, token.Id, nodeVisitId, "webhook", version.Id),
            ScopeKey = scopeKey,
            InstanceId = instance.Id,
            TokenId = token.Id,
            NodeVisitId = nodeVisitId,
            NodeId = "webhook",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = version.Id,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return execution;
    }

    private static Task<WfNodeExecution> ReadExecutionAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfNodeExecution>().Where(e => e.Id == id).FirstAsync();

    private static Task<List<WfNodeExecutionAttempt>> ReadAttemptsAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == id)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();

    private static async Task ExpireLeaseAsync(ISqlSugarClient db, long id)
    {
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) })
            .Where(e => e.Id == id)
            .ExecuteCommandAsync();
    }

    private static async Task<WfTokenStatus> TokenStatusAsync(ISqlSugarClient db, long id) =>
        (await db.Queryable<WfToken>().Where(t => t.Id == id).FirstAsync()).Status;

    private static async Task<WfInstanceStatus> InstanceStatusAsync(ISqlSugarClient db, long id) =>
        (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().Where(i => i.Id == id).FirstAsync()).Status;

    private sealed class InterruptingOnceHandler : IWorkflowNodeHandler
    {
        public WfNodeType NodeType => WfNodeType.Webhook;

        public int CallCount { get; private set; }

        public Task<WfNodeExecutionResult> ExecuteAsync(
            WfNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
                throw new OperationCanceledException();
            return Task.FromResult(WfNodeExecutionResult.Succeeded(summary: "recovered"));
        }
    }

    private sealed class IdempotentSideEffectHandler(HashSet<string> appliedKeys) : IWorkflowNodeHandler
    {
        public WfNodeType NodeType => WfNodeType.Webhook;

        public int ExternalCallCount { get; private set; }

        public Task<WfNodeExecutionResult> ExecuteAsync(
            WfNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            ExternalCallCount++;
            appliedKeys.Add(context.ExecutionKey);
            return Task.FromResult(WfNodeExecutionResult.Succeeded(summary: "side-effect-observed"));
        }
    }

    private sealed class CrashBeforeCommitEngine : IWorkflowEngine
    {
        public Task<WfEngineResult> ExecuteAsync(
            IWfCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WfEngineResult>(
                new InvalidOperationException("模拟 handler 返回后、tx2 提交前的进程崩溃。"));
    }
}
