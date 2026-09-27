using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <c>wf_node_execution</c> 的占位 + 领取契约测试(M3a-1 Task 3)。用 <see cref="WorkflowAppFactory"/> 从
/// <c>ISqlSugarClient</c> 直接读写(与 <see cref="WfPersistenceContractTests"/> 同款姿势),不经引擎——本 Task
/// 交付的是可靠执行的存储层,调度器接线归 Task 6。
/// </summary>
public class WfNodeExecutionClaimTests
{
    /// <summary>#7 唯一索引真被建出来:同 <c>ExecutionKey</c> 插第二行 → 抛。</summary>
    [Fact]
    public async Task Duplicate_execution_key_is_rejected_by_the_unique_index()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var key = UniqueKey();

        await db.Insertable(NewRow(key)).ExecuteCommandAsync();

        Exception? failure = null;
        try
        {
            await db.Insertable(NewRow(key)).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.Equal(1, await db.Queryable<WfNodeExecution>().Where(e => e.ExecutionKey == key).CountAsync());
    }

    /// <summary>#8 新行读到 <c>Status=Pending, Fence=0, AttemptCount=0, LeaseOwner=null, LeaseExpiresAtUtc=null</c>。</summary>
    [Fact]
    public async Task A_freshly_inserted_row_starts_unclaimed()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        await db.Insertable(row).ExecuteCommandAsync();

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == row.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Pending, loaded.Status);
        Assert.Equal(0, loaded.Fence);
        Assert.Equal(0, loaded.AttemptCount);
        Assert.Null(loaded.LeaseOwner);
        Assert.Null(loaded.LeaseExpiresAtUtc);
    }

    /// <summary>
    /// #9 领取 <c>Pending</c> → 返回非 null,<c>Status=Running</c>、<c>Fence=1</c>、<c>AttemptCount=1</c>、
    /// owner/租约已写。
    /// </summary>
    [Fact]
    public async Task Claiming_a_pending_row_marks_it_running_and_leased()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        await db.Insertable(row).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(WfNodeExecutionStatus.Running, claimed.Status);
        Assert.Equal(1, claimed.Fence);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal("worker-a", claimed.LeaseOwner);
        Assert.Equal(now.AddMinutes(5), claimed.LeaseExpiresAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    /// <summary>#10 租约有效期内再领 → 返回 <c>null</c>,且行未被改动(<c>Fence</c> 仍 1、owner 未变)。</summary>
    [Fact]
    public async Task Claiming_within_the_lease_window_fails_and_leaves_the_row_untouched()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        await db.Insertable(row).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var first = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(first);

        var second = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-b", now.AddMinutes(1), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(second);

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == row.Id).FirstAsync();
        Assert.Equal(1, loaded.Fence);
        Assert.Equal("worker-a", loaded.LeaseOwner);
    }

    /// <summary>#11 把 <c>LeaseExpiresAtUtc</c> 直接 UPDATE 成过去时刻后再领 → 成功,<c>Fence=2</c>、<c>AttemptCount=2</c>。</summary>
    [Fact]
    public async Task Claiming_after_the_lease_expires_succeeds_and_bumps_the_fence()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        await db.Insertable(row).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var first = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(first);

        var past = now.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = past })
            .Where(e => e.Id == row.Id)
            .ExecuteCommandAsync();

        var reclaimAt = now.AddMinutes(1);
        var second = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-b", reclaimAt, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal(2, second.Fence);
        Assert.Equal(2, second.AttemptCount);
        Assert.Equal("worker-b", second.LeaseOwner);
    }

    /// <summary>#12 <c>RetryScheduled</c> + <c>NextRetryAtUtc</c> 在未来 → <c>null</c>;改成过去 → 领到。</summary>
    [Fact]
    public async Task Retry_scheduled_row_is_claimable_only_once_its_retry_time_has_passed()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var now = DateTime.UtcNow;
        var row = NewRow(UniqueKey());
        row.Status = WfNodeExecutionStatus.RetryScheduled;
        row.NextRetryAtUtc = now.AddMinutes(10);
        await db.Insertable(row).ExecuteCommandAsync();

        var tooEarly = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(tooEarly);

        var past = now.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { NextRetryAtUtc = past })
            .Where(e => e.Id == row.Id)
            .ExecuteCommandAsync();

        var onTime = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(onTime);
        Assert.Equal(WfNodeExecutionStatus.Running, onTime.Status);
    }

    /// <summary>
    /// #12b <c>RetryScheduled</c> 且 <c>NextRetryAtUtc == null</c>(刚标记退避、退避时间还没算出来)→ 不可领取。
    /// </summary>
    [Fact]
    public async Task Retry_scheduled_row_with_no_retry_time_is_never_claimable()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        row.Status = WfNodeExecutionStatus.RetryScheduled;
        row.NextRetryAtUtc = null;
        await db.Insertable(row).ExecuteCommandAsync();

        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(claimed);
    }

    /// <summary>#13 终态行(<c>Succeeded</c>)→ <c>null</c>。</summary>
    [Fact]
    public async Task A_terminal_row_can_never_be_claimed()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        row.Status = WfNodeExecutionStatus.Succeeded;
        await db.Insertable(row).ExecuteCommandAsync();

        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, "worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(claimed);
    }

    /// <summary>#14 <c>EnsureAsync</c> 按 <c>ExecutionKey</c> 幂等:同 key 第二次返回既有行,表内仍 1 行。</summary>
    [Fact]
    public async Task EnsureAsync_is_idempotent_by_execution_key()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var key = UniqueKey();

        var first = await WfNodeExecutionStore.EnsureAsync(db, NewRow(key), CancellationToken.None);
        var second = await WfNodeExecutionStore.EnsureAsync(db, NewRow(key), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Queryable<WfNodeExecution>().Where(e => e.ExecutionKey == key).CountAsync());
    }

    /// <summary>#15 领取处在被回滚的事务里 → <c>Fence</c>/<c>AttemptCount</c> 不留痕。</summary>
    [Fact]
    public async Task A_claim_inside_a_rolled_back_transaction_leaves_no_trace()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = NewRow(UniqueKey());
        await db.Insertable(row).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var claimed = await WfNodeExecutionStore.ClaimAsync(
                db, row.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
            Assert.NotNull(claimed);
            throw new InvalidOperationException("强制回滚,验证领取不留痕。");
        });

        Assert.False(tran.IsSuccess);

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == row.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Pending, loaded.Status);
        Assert.Equal(0, loaded.Fence);
        Assert.Equal(0, loaded.AttemptCount);
    }

    // ────────────────────────── 脚手架 ──────────────────────────

    private static WfNodeExecution NewRow(string executionKey) => new()
    {
        ExecutionKey = executionKey,
        ScopeKey = "org-1",
        InstanceId = 1001L,
        TokenId = 2002L,
        NodeVisitId = 1L,
        NodeId = "node-1",
        NodeType = WfNodeType.Approval,
        DefinitionVersionId = 1L,
        MaxAttempts = 3,
    };

    private static string UniqueKey() => Guid.NewGuid().ToString("N");

    /// <summary>宿主起来 + 建表;返回作用域与 SqlSugar 单例(不经引擎)。</summary>
    private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient(); // 触发宿主启动与 CodeFirst 建表
        var scope = f.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }
}
