using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b T8 未知异常红测：生产 worker 路径必须区分未知非取消异常与外部取消。
/// 未知异常应进入有限重试和受控审计；取消仍走异常通道，不伪造成业务 attempt。
/// </summary>
public class WfNodeExecutionExceptionTests
{
    [Fact]
    public async Task An_unknown_handler_exception_is_a_bounded_retry_and_safe_audit()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded())
        {
            OnExecute = () => throw new InvalidOperationException("handler-secret-body"),
        };
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, maxAttempts: 2);
        var worker = scope.ServiceProvider.GetServices<IAdminJob>()
            .OfType<WfNodeExecutionJob>()
            .Single();

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var afterFirst = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, afterFirst.Status);
        Assert.Equal(1, afterFirst.AttemptCount);
        Assert.NotNull(afterFirst.NextRetryAtUtc);

        var firstAttempt = await ReadAttemptsAsync(db, execution.Id);
        var first = Assert.Single(firstAttempt);
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, first.ResultType);
        Assert.Equal(48032, first.ErrorCode);
        Assert.DoesNotContain("handler-secret-body", first.ErrorSummary ?? "");

        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { NextRetryAtUtc = DateTime.UtcNow.AddSeconds(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var afterExhaustion = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Failed, afterExhaustion.Status);
        Assert.Equal(2, afterExhaustion.AttemptCount);
        Assert.NotNull(afterExhaustion.CompletedTimeUtc);
        Assert.Equal(48032, afterExhaustion.ErrorCode);
        Assert.DoesNotContain("handler-secret-body", afterExhaustion.Summary ?? "");
        Assert.Equal(2, (await ReadAttemptsAsync(db, execution.Id)).Count);

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task External_cancellation_escapes_without_a_pseudo_attempt_or_outbox()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded())
        {
            OnExecute = () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, maxAttempts: 2);
        var worker = scope.ServiceProvider.GetServices<IAdminJob>()
            .OfType<WfNodeExecutionJob>()
            .Single();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.ExecuteAsync(JobContext(), cts.Token));

        var reloaded = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(0, await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == execution.Id)
            .CountAsync());
        Assert.Equal(1, handler.CallCount);
    }

    private static WorkflowAppFactory NewFactory(FakeNodeHandler handler) => new()
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

    private static async Task<WfNodeExecution> InsertExecutionAsync(
        ISqlSugarClient db,
        int maxAttempts)
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
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        const long nodeVisitId = 30_001;
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
            MaxAttempts = maxAttempts,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return execution;
    }

    private static Task<WfNodeExecution> ReadExecutionAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfNodeExecution>().Where(e => e.Id == id).FirstAsync();

    private static Task<List<WfNodeExecutionAttempt>> ReadAttemptsAsync(ISqlSugarClient db, long executionId) =>
        db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == executionId)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();
}
