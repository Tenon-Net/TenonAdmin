using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8c worker 种子、扫描谓词、批量、取消,以及死信人工重放回执。
/// </summary>
public class WfOutboxWorkerTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Workflow_bootstrap_seeds_a_ready_outbox_scan_job()
    {
        using var f = new WorkflowAppFactory();
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var job = await db.Queryable<SysJob>()
            .Where(j => j.Code == "wf-outbox-scan")
            .FirstAsync();

        Assert.NotNull(job);
        Assert.Equal(typeof(WfOutboxJob).FullName, job!.HandlerName);
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
    public async Task Worker_dispatches_pending_and_expired_dispatching_but_skips_future_retry()
    {
        var transport = new CountingTransport();
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var now = DateTime.UtcNow;

        var pending = await EnqueueAsync(db, 1, now);
        var expired = await EnqueueAsync(db, 2, now);
        var claimed = await WfOutboxConsumerStore.ClaimAsync(
            db, expired.Id, now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(claimed);
        var expiredAt = now.AddMinutes(-1);
        await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox { AvailableAtUtc = expiredAt })
            .Where(o => o.Id == expired.Id)
            .ExecuteCommandAsync();
        var future = await EnqueueAsync(db, 3, now);
        var futureClaim = await WfOutboxConsumerStore.ClaimAsync(
            db, future.Id, now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(await WfOutboxConsumerStore.ScheduleRetryAsync(
            db, futureClaim!.Id, futureClaim.AttemptCount, now.AddMinutes(10), "later", CancellationToken.None));

        await ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), CancellationToken.None);

        Assert.Equal(WfOutboxStatus.Dispatched, await StatusAsync(db, pending.Id));
        Assert.Equal(WfOutboxStatus.Dispatched, await StatusAsync(db, expired.Id));
        Assert.Equal(WfOutboxStatus.Pending, await StatusAsync(db, future.Id));
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task Worker_does_not_process_more_than_the_configured_batch_size()
    {
        var transport = new CountingTransport();
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        typeof(WorkflowOptions).GetProperty("OutboxScanBatchSize")!
            .SetValue(scope.ServiceProvider.GetRequiredService<WorkflowOptions>(), 2);

        var rows = new[]
        {
            await EnqueueAsync(db, 11),
            await EnqueueAsync(db, 12),
            await EnqueueAsync(db, 13),
        };

        await ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), CancellationToken.None);

        var dispatched = 0;
        foreach (var row in rows)
        {
            if (await StatusAsync(db, row.Id) == WfOutboxStatus.Dispatched)
                dispatched++;
        }

        Assert.Equal(2, dispatched);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task Worker_propagates_external_cancellation_before_processing_any_item()
    {
        var transport = new CountingTransport();
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), cts.Token));
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Scheduler_tick_fires_the_seeded_outbox_worker()
    {
        var transport = new CountingTransport();
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await EnqueueAsync(db, 51);
        var job = await db.Queryable<SysJob>()
            .Where(j => j.Code == "wf-outbox-scan")
            .FirstAsync();
        Assert.NotNull(job);

        var due = DateTime.Now.AddSeconds(-10);
        await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { NextRunTime = due })
            .Where(j => j.Id == job!.Id)
            .ExecuteCommandAsync();

        await scope.ServiceProvider.GetRequiredService<JobSchedulerService>()
            .TickAsync(CancellationToken.None);

        var deadline = Environment.TickCount64 + 5_000;
        List<SysJobLog> logs;
        do
        {
            logs = await db.Queryable<SysJobLog>()
                .Where(l => l.JobId == job!.Id)
                .ToListAsync();
            if (logs.Any(l => l.EndTime is not null))
                break;
            await Task.Delay(50);
        } while (Environment.TickCount64 < deadline);

        Assert.Contains(logs, log => log.EndTime is not null && log.RunStatus == JobRunStatus.Success);
        Assert.Equal(WfOutboxStatus.Dispatched, await StatusAsync(db, row.Id));
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Replay_moves_failed_to_pending_and_replays_the_same_request_id()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var failed = await EnqueueFailedVisibleAsync(db);

        var first = await (await admin.PostAsync(
            $"/api/v1/workflow/outbox/{failed.Id}/replay?requestId=wf-outbox-replay-1",
            null)).ReadEnvelope();
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal((int)WfOutboxStatus.Pending, first.GetProperty("data").GetProperty("status").GetInt32());
        Assert.Equal(0, first.GetProperty("data").GetProperty("attemptCount").GetInt32());

        var replay = await (await admin.PostAsync(
            $"/api/v1/workflow/outbox/{failed.Id}/replay?requestId=wf-outbox-replay-1",
            null)).ReadEnvelope();
        Assert.Equal(0, replay.GetProperty("code").GetInt32());
        Assert.Equal(failed.Id, replay.GetProperty("data").GetProperty("id").GetInt64());

        var second = await (await admin.PostAsync(
            $"/api/v1/workflow/outbox/{failed.Id}/replay?requestId=wf-outbox-replay-2",
            null)).ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.OutboxReplayNotAllowed, second.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Replay_rejects_dispatched_and_missing_rows()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var pending = await EnqueueVisibleAsync(db, 71);
        var claimed = await WfOutboxConsumerStore.ClaimAsync(
            db, pending.Id, DateTime.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(await WfOutboxConsumerStore.CompleteAsync(
            db, claimed!.Id, claimed.AttemptCount, DateTime.UtcNow, CancellationToken.None));

        var dispatchedReplay = await (await admin.PostAsync(
            $"/api/v1/workflow/outbox/{pending.Id}/replay?requestId=wf-outbox-replay-dispatched",
            null)).ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.OutboxReplayNotAllowed, dispatchedReplay.GetProperty("code").GetInt32());

        var missing = await (await admin.PostAsync(
            "/api/v1/workflow/outbox/999999999999/replay?requestId=wf-outbox-replay-missing",
            null)).ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.OutboxNotFound, missing.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Dead_letter_page_lists_failed_rows_in_scope()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var failed = await EnqueueFailedVisibleAsync(db);
        await EnqueueVisibleAsync(db, 81);

        var page = await (await admin.GetAsync("/api/v1/workflow/outbox/page")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetInt64() == failed.Id);
        Assert.All(items, item => Assert.Equal((int)WfOutboxStatus.Failed, item.GetProperty("status").GetInt32()));
    }

    private static WorkflowAppFactory NewFactory(CountingTransport transport) => new()
    {
        Overrides = services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IWfOutboxTransport>(_ => transport));
        },
    };

    private static IAdminJob ResolveWorker(IServiceProvider services)
    {
        var worker = services.GetServices<IAdminJob>()
            .SingleOrDefault(job => job is WfOutboxJob);
        Assert.NotNull(worker);
        return worker!;
    }

    private static JobExecutionContext JobContext() => new()
    {
        JobId = 1,
        JobCode = "wf-outbox-scan",
        JobName = "工作流 outbox 扫描",
        FireInstanceId = 1,
        ScheduledTime = DateTime.Now,
        FireTime = DateTime.Now,
    };

    private static async Task<WfOutbox> EnqueueAsync(ISqlSugarClient db, int tag, DateTime? nowUtc = null)
    {
        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N") + tag.ToString("D4"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = 3000L + tag,
            TokenId = 4000L + tag,
            NodeVisitId = 1L,
            NodeId = "node-1",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 1L,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return await WfOutboxStore.EnqueueAsync(
            db, execution, WfOutboxStore.MessageTypeNodeExecutionCompleted, "{}", nowUtc ?? DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task<WfOutbox> EnqueueVisibleAsync(ISqlSugarClient db, int tag)
    {
        var instance = new WfInstance
        {
            DefinitionVersionId = 1,
            StarterUserId = 1,
            Status = WfInstanceStatus.Running,
            BusinessKey = $"outbox-{tag}",
        };
        await db.Insertable(instance).ExecuteCommandAsync();
        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N") + tag.ToString("D4"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = instance.Id,
            TokenId = 5000L + tag,
            NodeVisitId = 1L,
            NodeId = "node-1",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 1L,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return await WfOutboxStore.EnqueueAsync(
            db, execution, WfOutboxStore.MessageTypeNodeExecutionCompleted, "{}", DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task<WfOutbox> EnqueueFailedVisibleAsync(ISqlSugarClient db)
    {
        var row = await EnqueueVisibleAsync(db, 91);
        var claimed = await WfOutboxConsumerStore.ClaimAsync(
            db, row.Id, DateTime.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(await WfOutboxConsumerStore.FailAsync(
            db, claimed!.Id, claimed.AttemptCount, DateTime.UtcNow, "dead", CancellationToken.None));
        return await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
    }

    private static Task<WfOutboxStatus> StatusAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfOutbox>().Where(o => o.Id == id).Select(o => o.Status).FirstAsync();

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private sealed class CountingTransport : IWfOutboxTransport
    {
        public int CallCount { get; private set; }

        public Task<WfOutboxDispatchResult> DispatchAsync(WfOutboxMessage message, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(WfOutboxDispatchResult.Succeeded());
        }
    }
}
