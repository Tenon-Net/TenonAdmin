using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8c 存储层:领取谓词、AttemptCount fence、退避、死信和人工重放。
/// 入队仍只走 <see cref="WfOutboxStore.EnqueueAsync"/>。
/// </summary>
public class WfOutboxConsumerTests
{
    [Fact]
    public void Task_8b_enqueue_surface_stays_enqueue_only()
    {
        var methods = typeof(WfOutboxStore)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(WfOutboxStore))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal("EnqueueAsync", Assert.Single(methods));
    }

    [Fact]
    public void Retry_delay_uses_retry_after_inside_bounds_otherwise_exponential_backoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), WfOutboxConsumerStore.ResolveRetryDelay(1, TimeSpan.FromSeconds(45)));
        Assert.Equal(TimeSpan.FromSeconds(30), WfOutboxConsumerStore.ResolveRetryDelay(1, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(30), WfOutboxConsumerStore.ResolveRetryDelay(1, TimeSpan.FromHours(25)));
        Assert.Equal(TimeSpan.FromSeconds(60), WfOutboxConsumerStore.ResolveRetryDelay(2, null));
        Assert.Equal(TimeSpan.FromSeconds(30 << 5), WfOutboxConsumerStore.ResolveRetryDelay(99, null));
    }

    [Fact]
    public async Task Claiming_pending_marks_dispatching_and_bumps_attempt_count()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);

        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(db, row.Id, now, TimeSpan.FromMinutes(1));

        Assert.NotNull(claimed);
        Assert.Equal(WfOutboxStatus.Dispatching, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal(now.AddMinutes(1), claimed.AvailableAtUtc, TimeSpan.FromSeconds(1));
        Assert.Null(claimed.CompletedAtUtc);
    }

    [Fact]
    public async Task Claiming_within_the_visibility_window_fails_and_leaves_the_row_untouched()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var now = DateTime.UtcNow;
        var first = await ClaimAsync(db, row.Id, now, TimeSpan.FromMinutes(5));
        Assert.NotNull(first);

        var second = await ClaimAsync(db, row.Id, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.Null(second);

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(1, loaded.AttemptCount);
        Assert.Equal(WfOutboxStatus.Dispatching, loaded.Status);
    }

    [Fact]
    public async Task Claiming_after_visibility_timeout_succeeds_and_bumps_the_fence()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var now = DateTime.UtcNow;
        Assert.NotNull(await ClaimAsync(db, row.Id, now, TimeSpan.FromMinutes(5)));

        var reclaimAt = now.AddMinutes(6);
        var second = await ClaimAsync(db, row.Id, reclaimAt, TimeSpan.FromMinutes(5));
        Assert.NotNull(second);
        Assert.Equal(2, second.AttemptCount);
        Assert.Equal(WfOutboxStatus.Dispatching, second.Status);
    }

    [Fact]
    public async Task Future_retry_is_not_claimable_until_available_at()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(db, row.Id, now, TimeSpan.FromMinutes(1));
        Assert.NotNull(claimed);

        var retryAt = now.AddMinutes(10);
        Assert.True(await WfOutboxConsumerStore.ScheduleRetryAsync(
            db, claimed.Id, claimed.AttemptCount, retryAt, "timeout", CancellationToken.None));

        Assert.Null(await ClaimAsync(db, row.Id, now.AddMinutes(1), TimeSpan.FromMinutes(1)));

        var reloaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Pending, reloaded.Status);
        Assert.Equal("timeout", reloaded.LastError);
        AssertUtcClose(retryAt, reloaded.AvailableAtUtc);

        // 四库时间列精度不同,到期边界允许一个秒级量化窗口。
        var due = await ClaimAsync(db, row.Id, retryAt.AddSeconds(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(due);
        Assert.Equal(2, due.AttemptCount);
    }

    [Fact]
    public async Task Complete_cas_rejects_a_stale_attempt_count()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var now = DateTime.UtcNow;
        var first = await ClaimAsync(db, row.Id, now, TimeSpan.FromMinutes(1));
        Assert.NotNull(first);

        var later = now.AddMinutes(2);
        var second = await ClaimAsync(db, row.Id, later, TimeSpan.FromMinutes(1));
        Assert.NotNull(second);
        Assert.Equal(2, second.AttemptCount);

        Assert.False(await WfOutboxConsumerStore.CompleteAsync(
            db, first.Id, first.AttemptCount, later, CancellationToken.None));

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatching, loaded.Status);
        Assert.Equal(2, loaded.AttemptCount);

        Assert.True(await WfOutboxConsumerStore.CompleteAsync(
            db, second.Id, second.AttemptCount, later, CancellationToken.None));
        loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatched, loaded.Status);
        AssertUtcClose(later, loaded.CompletedAtUtc);
        Assert.Null(loaded.LastError);
    }

    [Fact]
    public async Task Terminal_rows_cannot_be_claimed_and_replay_only_accepts_failed()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var now = DateTime.UtcNow;
        var enqueueAt = now.AddSeconds(-2);
        var dispatched = await EnqueueAsync(db, "wf.node-execution.completed", enqueueAt);
        var claimed = await ClaimAsync(db, dispatched.Id, now, TimeSpan.FromMinutes(1));
        Assert.True(await WfOutboxConsumerStore.CompleteAsync(
            db, claimed!.Id, claimed.AttemptCount, now, CancellationToken.None));
        Assert.Null(await ClaimAsync(db, dispatched.Id, now.AddMinutes(10), TimeSpan.FromMinutes(1)));
        Assert.False(await WfOutboxConsumerStore.ReplayFailedAsync(
            db, dispatched.Id, now, CancellationToken.None));

        var failed = await EnqueueAsync(db, "wf.node-execution.webhook-sent", enqueueAt);
        var failClaim = await ClaimAsync(db, failed.Id, now, TimeSpan.FromMinutes(1));
        Assert.True(await WfOutboxConsumerStore.FailAsync(
            db, failClaim!.Id, failClaim.AttemptCount, now, "dead", CancellationToken.None));
        Assert.Null(await ClaimAsync(db, failed.Id, now.AddMinutes(10), TimeSpan.FromMinutes(1)));

        Assert.True(await WfOutboxConsumerStore.ReplayFailedAsync(
            db, failed.Id, now.AddMinutes(11), CancellationToken.None));
        var replayed = await db.Queryable<WfOutbox>().Where(o => o.Id == failed.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Pending, replayed.Status);
        Assert.Equal(0, replayed.AttemptCount);
        Assert.Null(replayed.LastError);
        Assert.Null(replayed.CompletedAtUtc);
        AssertUtcClose(now.AddMinutes(11), replayed.AvailableAtUtc);
    }

    [Fact]
    public async Task A_claim_inside_a_rolled_back_transaction_leaves_no_trace()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            await WfOutboxConsumerStore.ClaimAsync(
                db, row.Id, DateTime.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
            throw new InvalidOperationException("强制回滚,验证领取不留痕。");
        });
        Assert.False(tran.IsSuccess);

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Pending, loaded.Status);
        Assert.Equal(0, loaded.AttemptCount);
    }

    [Fact]
    public async Task Last_error_is_truncated_to_512_on_fail()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var claimed = await ClaimAsync(db, row.Id, DateTime.UtcNow, TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;
        Assert.True(await WfOutboxConsumerStore.FailAsync(
            db, claimed!.Id, claimed.AttemptCount, now, new string('x', 600), CancellationToken.None));

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(512, loaded.LastError!.Length);
        Assert.Equal(WfOutboxStatus.Failed, loaded.Status);
    }

    private static async Task<WfOutbox> EnqueueAsync(
        ISqlSugarClient db,
        string messageType = "wf.node-execution.completed",
        DateTime? nowUtc = null)
    {
        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = 1001L,
            TokenId = 2002L,
            NodeVisitId = 1L,
            NodeId = "node-1",
            NodeType = WfNodeType.Approval,
            DefinitionVersionId = 1L,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return await WfOutboxStore.EnqueueAsync(
            db, execution, messageType, "{\"ok\":true}", nowUtc ?? DateTime.UtcNow.AddSeconds(-2), CancellationToken.None);
    }

    private static void AssertUtcClose(DateTime expected, DateTime? actual)
    {
        Assert.True(actual.HasValue);
        Assert.InRange((actual.Value - expected).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private static Task<WfOutbox?> ClaimAsync(
        ISqlSugarClient db,
        long id,
        DateTime nowUtc,
        TimeSpan visibility) =>
        WfOutboxConsumerStore.ClaimAsync(db, id, nowUtc, visibility, CancellationToken.None);

    private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient();
        var scope = f.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }
}
