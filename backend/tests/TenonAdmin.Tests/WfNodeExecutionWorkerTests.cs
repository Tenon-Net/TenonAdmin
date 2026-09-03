using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b worker：生产 execution worker 必须走现有
/// <see cref="IAdminJob"/> 调度 seam，扫描可领取 execution 后逐项调用 dispatcher。
/// 永久损坏的 execution 必须被 quarantine 为受控终态，不能靠 lease 无限重领。
/// </summary>
public class WfNodeExecutionWorkerTests
{
    [Fact]
    public async Task Workflow_bootstrap_seeds_a_ready_execution_scan_job()
    {
        using var f = NewFactory(new FakeNodeHandler(WfNodeExecutionResult.Succeeded()));
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var workerType = typeof(WorkflowOptions).Assembly.GetType(
            "TenonAdmin.Workflow.WfNodeExecutionJob");
        Assert.NotNull(workerType);

        var job = await db.Queryable<SysJob>()
            .Where(j => j.Code == "wf-node-execution-scan")
            .FirstAsync();

        Assert.NotNull(job);
        Assert.Equal(workerType!.FullName, job!.HandlerName);
        Assert.Equal(JobHandlerKind.Compiled, job.HandlerKind);
        Assert.Equal(JobTriggerKind.Interval, job.TriggerKind);
        Assert.Equal(5, job.IntervalSeconds);
        Assert.Equal(JobMisfireStrategy.Skip, job.MisfireStrategy);
        Assert.Equal(JobConcurrencyMode.SerialSkip, job.ConcurrencyMode);
        Assert.Equal(JobStatus.Ready, job.Status);
        Assert.Equal(0, job.TimeoutSeconds);
        Assert.False(job.IsSystem);
    }

    [Fact]
    public async Task Worker_dispatches_pending_due_retry_and_expired_running_but_skips_future_retry()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var now = DateTime.UtcNow;

        var pending = await InsertExecutionAsync(db, 1);
        var dueRetry = await InsertExecutionAsync(
            db, 2, WfNodeExecutionStatus.RetryScheduled, retryAtUtc: now.AddMinutes(-1));
        var expiredRunning = await InsertExecutionAsync(
            db, 3, WfNodeExecutionStatus.Running, leaseExpiresAtUtc: now.AddMinutes(-1));
        var futureRetry = await InsertExecutionAsync(
            db, 4, WfNodeExecutionStatus.RetryScheduled, retryAtUtc: now.AddMinutes(10));

        await ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, pending.Id));
        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, dueRetry.Id));
        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, expiredRunning.Id));
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, await StatusAsync(db, futureRetry.Id));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Worker_does_not_process_more_than_the_configured_batch_size()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        SetWorkflowIntOption(
            scope.ServiceProvider.GetRequiredService<WorkflowOptions>(),
            "NodeExecutionScanBatchSize",
            2);

        var rows = new[]
        {
            await InsertExecutionAsync(db, 11),
            await InsertExecutionAsync(db, 12),
            await InsertExecutionAsync(db, 13),
        };

        await ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), CancellationToken.None);

        var statuses = new List<WfNodeExecutionStatus>(rows.Length);
        foreach (var row in rows)
            statuses.Add(await StatusAsync(db, row.Id));
        Assert.Equal(2, statuses.Count(status => status == WfNodeExecutionStatus.Succeeded));
        Assert.Equal(1, statuses.Count(status => status == WfNodeExecutionStatus.Pending));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Worker_propagates_external_cancellation_before_processing_any_item()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded());
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), cts.Token));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Worker_quarantines_a_permanently_broken_execution_and_continues_with_the_next_item()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var failed = await InsertExecutionWithMissingInstanceAsync(db, 21);
        var healthy = await InsertExecutionAsync(db, 22);

        var worker = ResolveWorker(scope.ServiceProvider);
        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionStatus.Failed, await StatusAsync(db, failed.Id));
        var quarantineAttempts = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == failed.Id)
            .ToListAsync();
        var quarantineAttempt = Assert.Single(quarantineAttempts);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, quarantineAttempt.ResultType);
        Assert.Equal(WorkflowErrorCode.InstanceNotFound, quarantineAttempt.ErrorCode);
        Assert.Equal(1, await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == failed.Id && o.Status == WfOutboxStatus.Pending)
            .CountAsync());
        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, healthy.Id));
        Assert.Equal(1, handler.CallCount);

        // Failed execution 不再满足 worker 的三类扫描谓词；重复扫描不能重新领取或追加 attempt/outbox。
        await worker.ExecuteAsync(JobContext(), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Failed, await StatusAsync(db, failed.Id));
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == failed.Id)
            .CountAsync());
        Assert.Equal(1, await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == failed.Id)
            .CountAsync());
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Worker_quarantines_a_corrupt_definition_model_without_reclaiming_it()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "unused"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, 52);
        await db.Updateable<WfDefinitionVersion>()
            .SetColumns(v => new WfDefinitionVersion { ModelJson = "{ invalid-json" })
            .Where(v => v.Id == execution.DefinitionVersionId)
            .ExecuteCommandAsync();
        var worker = ResolveWorker(scope.ServiceProvider);

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Failed, reloaded.Status);
        Assert.Equal(WorkflowErrorCode.ModelInvalid, reloaded.ErrorCode);
        var attempts = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync();
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, Assert.Single(attempts).ResultType);
        Assert.Equal(WorkflowErrorCode.ModelInvalid, attempts[0].ErrorCode);
        Assert.Equal(0, handler.CallCount);

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .CountAsync());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Scheduler_tick_fires_the_seeded_worker_through_executor_and_handler_resolver()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "scheduled"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, 51);
        var job = await db.Queryable<SysJob>()
            .Where(j => j.Code == "wf-node-execution-scan")
            .FirstAsync();
        Assert.NotNull(job);

        var due = DateTime.Now.AddMinutes(-1);
        await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { NextRunTime = due })
            .Where(j => j.Id == job!.Id)
            .ExecuteCommandAsync();

        var scheduler = scope.ServiceProvider.GetRequiredService<JobSchedulerService>();
        await scheduler.TickAsync(CancellationToken.None);

        var deadline = Environment.TickCount64 + 5_000;
        List<SysJobLog> logs;
        do
        {
            logs = await db.Queryable<SysJobLog>()
                .Where(l => l.JobId == job!.Id)
                .ToListAsync();
            if (logs.Any(l => l.EndTime is not null)) break;
            await Task.Delay(50);
        } while (Environment.TickCount64 < deadline);

        var completedLog = Assert.Single(logs, l => l.EndTime is not null);
        Assert.Equal(JobRunStatus.Success, completedLog.RunStatus);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, execution.Id));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Repeating_a_scan_does_not_dispatch_a_terminal_execution_again()
    {
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"));
        using var f = NewFactory(handler);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var execution = await InsertExecutionAsync(db, 31);
        var worker = ResolveWorker(scope.ServiceProvider);

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);
        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        Assert.Equal(WfNodeExecutionStatus.Succeeded, await StatusAsync(db, execution.Id));
        Assert.Equal(1, handler.CallCount);
    }

    private static WorkflowAppFactory NewFactory(FakeNodeHandler handler) => new()
    {
        Overrides = services => services.Insert(
            0,
            ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => handler)),
    };

    private static IAdminJob ResolveWorker(IServiceProvider services)
    {
        var workerType = typeof(WorkflowOptions).Assembly.GetType(
            "TenonAdmin.Workflow.WfNodeExecutionJob");
        Assert.NotNull(workerType);

        var worker = services.GetServices<IAdminJob>()
            .SingleOrDefault(job => job.GetType() == workerType);
        Assert.NotNull(worker);
        return worker!;
    }

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
        int tag,
        WfNodeExecutionStatus status = WfNodeExecutionStatus.Pending,
        DateTime? retryAtUtc = null,
        DateTime? leaseExpiresAtUtc = null)
    {
        var model = WebhookModel();
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
            BusinessKey = $"worker-{tag}",
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var visitId = 10_000L + tag;
        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = "webhook",
            NodeVisitId = visitId,
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        var scopeKey = WfIdentityHash.NormalizeScopeKey(null);
        var execution = new WfNodeExecution
        {
            ExecutionKey = WfExecutionKey.Compute(
                scopeKey, instance.Id, token.Id, visitId, "webhook", version.Id),
            ScopeKey = scopeKey,
            InstanceId = instance.Id,
            TokenId = token.Id,
            NodeVisitId = visitId,
            NodeId = "webhook",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = version.Id,
            Status = status,
            MaxAttempts = 3,
            NextRetryAtUtc = retryAtUtc,
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return execution;
    }

    private static async Task<WfNodeExecution> InsertExecutionWithMissingInstanceAsync(
        ISqlSugarClient db,
        int tag)
    {
        var execution = new WfNodeExecution
        {
            ExecutionKey = $"missing-instance-{tag}-{Guid.NewGuid():N}",
            ScopeKey = WfIdentityHash.ScopeSentinel,
            InstanceId = 9_000_000 + tag,
            TokenId = 8_000_000 + tag,
            NodeVisitId = 20_000 + tag,
            NodeId = "webhook",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 7_000_000 + tag,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return execution;
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

    private static async Task<WfNodeExecutionStatus> StatusAsync(ISqlSugarClient db, long id) =>
        (await db.Queryable<WfNodeExecution>().Where(e => e.Id == id).FirstAsync()).Status;

    private static void SetWorkflowIntOption(WorkflowOptions options, string propertyName, int value)
    {
        var property = typeof(WorkflowOptions).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(typeof(int), property!.PropertyType);
        property.SetValue(options, value);
    }
}
