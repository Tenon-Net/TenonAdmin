using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8c dispatcher:事务外 transport、Complete/Retry/Failed CAS、预算耗尽与迟到回写。
/// </summary>
public class WfOutboxDispatcherTests
{
    [Fact]
    public async Task Successful_transport_marks_dispatched_outside_a_database_transaction()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var transport = new ScriptedTransport { Db = db };
        var dispatcher = CreateDispatcher(db, transport);

        var status = await dispatcher.RunAsync(row.Id, CancellationToken.None);

        Assert.Equal(WfOutboxStatus.Dispatched, status);
        Assert.False(transport.SawTransaction);
        var message = Assert.Single(transport.Messages);
        Assert.Equal(row.MessageKey, message.MessageKey);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("{\"ok\":true}", message.PayloadJson);

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatched, loaded.Status);
        Assert.NotNull(loaded.CompletedAtUtc);
    }

    [Fact]
    public async Task Retryable_transport_returns_to_pending_until_budget_is_exhausted()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var options = new WorkflowOptions { OutboxMaxAttempts = 2, OutboxVisibilityTimeoutSeconds = 30 };
        var transport = new ScriptedTransport
        {
            Next = WfOutboxDispatchResult.RetryableFailure(48031, "busy", TimeSpan.FromSeconds(5)),
        };
        var dispatcher = CreateDispatcher(db, transport, options);

        Assert.Equal(WfOutboxStatus.Pending, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        var afterRetry = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Pending, afterRetry.Status);
        Assert.Equal("busy", afterRetry.LastError);
        Assert.True(afterRetry.AvailableAtUtc > DateTime.UtcNow);

        var past = DateTime.UtcNow.AddMinutes(-1);
        await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox { AvailableAtUtc = past })
            .Where(o => o.Id == row.Id)
            .ExecuteCommandAsync();

        Assert.Equal(WfOutboxStatus.Failed, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        var failed = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Failed, failed.Status);
        Assert.Equal(2, failed.AttemptCount);
        Assert.Equal(2, transport.Messages.Count);
    }

    [Fact]
    public async Task Terminal_transport_fails_without_consuming_the_full_budget()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var transport = new ScriptedTransport
        {
            Next = WfOutboxDispatchResult.TerminalFailure(48029, "rejected"),
        };
        var dispatcher = CreateDispatcher(db, transport, new WorkflowOptions { OutboxMaxAttempts = 5 });

        Assert.Equal(WfOutboxStatus.Failed, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(1, loaded.AttemptCount);
        Assert.Equal("rejected", loaded.LastError);
    }

    [Fact]
    public async Task Unhandled_transport_exception_is_retryable_and_oce_does_not_write_back()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var retryRow = await EnqueueAsync(db, "wf.node-execution.completed");
        var retryTransport = new ScriptedTransport { Throw = new InvalidOperationException("boom") };
        var retryDispatcher = CreateDispatcher(db, retryTransport, new WorkflowOptions { OutboxMaxAttempts = 3 });
        Assert.Equal(WfOutboxStatus.Pending, await retryDispatcher.RunAsync(retryRow.Id, CancellationToken.None));
        var retried = await db.Queryable<WfOutbox>().Where(o => o.Id == retryRow.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Pending, retried.Status);
        Assert.Contains("InvalidOperationException", retried.LastError, StringComparison.Ordinal);

        var oceRow = await EnqueueAsync(db, "wf.node-execution.webhook-sent");
        var oceTransport = new ScriptedTransport { Throw = new OperationCanceledException() };
        var oceDispatcher = CreateDispatcher(db, oceTransport);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => oceDispatcher.RunAsync(oceRow.Id, CancellationToken.None));
        var stuck = await db.Queryable<WfOutbox>().Where(o => o.Id == oceRow.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatching, stuck.Status);
        Assert.Equal(1, stuck.AttemptCount);
        Assert.Null(stuck.LastError);
    }

    [Fact]
    public async Task A_late_complete_after_reclaim_does_not_overwrite_the_new_owner()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var now = DateTime.UtcNow;
        var first = await WfOutboxConsumerStore.ClaimAsync(
            db, row.Id, now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(first);

        var reclaimAt = now.AddMinutes(2);
        var second = await WfOutboxConsumerStore.ClaimAsync(
            db, row.Id, reclaimAt, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(second);

        var transport = new ScriptedTransport { Next = WfOutboxDispatchResult.Succeeded("stale") };
        var dispatcher = CreateDispatcher(db, transport);
        // dispatcher 会再领一次;把 AvailableAtUtc 推到未来,让 RunAsync 领不到,然后直接打 Complete 模拟迟到回写。
        Assert.False(await WfOutboxConsumerStore.CompleteAsync(
            db, first.Id, first.AttemptCount, reclaimAt, CancellationToken.None));

        Assert.True(await WfOutboxConsumerStore.CompleteAsync(
            db, second.Id, second.AttemptCount, reclaimAt, CancellationToken.None));
        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatched, loaded.Status);
        Assert.Equal(2, loaded.AttemptCount);
    }

    [Fact]
    public async Task Built_in_noop_transport_dispatches_pending_rows()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var dispatcher = scope.ServiceProvider.GetRequiredService<WfOutboxDispatcher>();

        Assert.Equal(WfOutboxStatus.Dispatched, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        Assert.IsType<NoOpWfOutboxTransport>(scope.ServiceProvider.GetRequiredService<IWfOutboxTransport>());
    }

    private static WfOutboxDispatcher CreateDispatcher(
        ISqlSugarClient db,
        IWfOutboxTransport transport,
        WorkflowOptions? options = null) =>
        new(db, transport, options ?? new WorkflowOptions(), TimeProvider.System, NullLogger<WfOutboxDispatcher>.Instance);

    private static async Task<WfOutbox> EnqueueAsync(
        ISqlSugarClient db,
        string messageType = "wf.node-execution.completed")
    {
        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = 1001L,
            TokenId = 2002L,
            NodeVisitId = 1L,
            NodeId = "node-1",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 1L,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return await WfOutboxStore.EnqueueAsync(
            db, execution, messageType, "{\"ok\":true}", DateTime.UtcNow, CancellationToken.None);
    }

    private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient();
        var scope = f.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }

    private sealed class ScriptedTransport : IWfOutboxTransport
    {
        public ISqlSugarClient? Db { get; init; }
        public bool? SawTransaction { get; private set; }
        public List<WfOutboxMessage> Messages { get; } = [];
        public WfOutboxDispatchResult? Next { get; set; }
        public Exception? Throw { get; init; }

        public Task<WfOutboxDispatchResult> DispatchAsync(WfOutboxMessage message, CancellationToken cancellationToken)
        {
            SawTransaction = Db?.Ado.Transaction is not null;
            Messages.Add(message);
            if (Throw is not null) throw Throw;
            return Task.FromResult(Next ?? WfOutboxDispatchResult.Succeeded());
        }
    }
}
