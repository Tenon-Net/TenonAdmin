using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <c>wf_node_execution_attempt</c> 的 append-only 写入契约测试(M3a-1 Task 4)。用
/// <see cref="WorkflowAppFactory"/> 从 <c>ISqlSugarClient</c> 直接读写,姿势同
/// <see cref="WfNodeExecutionClaimTests"/>——不经引擎,本 Task 交付的是存储层,调度器接线归 Task 6。
/// </summary>
public class WfNodeExecutionAttemptTests
{
    /// <summary>#1 首次 attempt 的 <c>AttemptNo</c> = 1,且等于领取读回的 <c>AttemptCount</c>。</summary>
    [Fact]
    public async Task First_attempt_no_is_one_and_matches_the_claimed_attempt_count()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        var started = DateTime.UtcNow;
        var ended = started.AddSeconds(3); // 与 started 必须不等,否则测不出两个形参被互换
        var attempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, claimed, WfNodeExecutionResult.Succeeded(summary: "ok"), started, ended, CancellationToken.None);

        Assert.Equal(1, attempt.AttemptNo);
        Assert.Equal(claimed.AttemptCount, attempt.AttemptNo);
        Assert.Equal(execution.Id, attempt.ExecutionId);
        Assert.Equal(started, attempt.StartedAtUtc);
        Assert.Equal(ended, attempt.EndedAtUtc);
    }

    /// <summary>#2 重试新增一行,不覆盖旧 attempt——第一行原样留存,第二行是新领取的 <c>AttemptNo = 2</c>。</summary>
    [Fact]
    public async Task Retry_appends_a_new_row_without_overwriting_the_previous_attempt()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var firstClaim = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(firstClaim);

        var firstAttempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, firstClaim,
            WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: "first"),
            now, now, CancellationToken.None);

        var past = now.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = past })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var reclaimAt = now.AddMinutes(1);
        var secondClaim = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-b", reclaimAt, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(secondClaim);
        Assert.Equal(2, secondClaim.AttemptCount);

        var secondAttempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, secondClaim, WfNodeExecutionResult.Succeeded(summary: "ok"), reclaimAt, reclaimAt, CancellationToken.None);

        var rows = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].AttemptNo);
        Assert.Equal(2, rows[1].AttemptNo);
        Assert.Equal(firstAttempt.Id, rows[0].Id);
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, rows[0].ResultType);
        Assert.Equal(48001, rows[0].ErrorCode);
        Assert.Equal("first", rows[0].ErrorSummary);
        Assert.Equal(secondAttempt.Id, rows[1].Id);
    }

    /// <summary>#3 唯一索引 <c>(ExecutionId, AttemptNo)</c> 真的挡住重复。</summary>
    [Fact]
    public async Task Duplicate_execution_and_attempt_no_is_rejected_by_the_unique_index()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        await db.Insertable(NewAttempt(execution.Id, 1, now)).ExecuteCommandAsync();

        Exception? failure = null;
        try
        {
            await db.Insertable(NewAttempt(execution.Id, 1, now)).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id && a.AttemptNo == 1)
            .CountAsync());
    }

    /// <summary>#4 两个不同 execution 各自都可以有 <c>AttemptNo = 1</c>(专挡把唯一索引写成 <c>AttemptNo</c> 单列)。</summary>
    [Fact]
    public async Task Two_different_executions_can_each_have_attempt_no_one()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var executionA = NewExecution(UniqueKey());
        var executionB = NewExecution(UniqueKey());
        await db.Insertable(executionA).ExecuteCommandAsync();
        await db.Insertable(executionB).ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        await db.Insertable(NewAttempt(executionA.Id, 1, now)).ExecuteCommandAsync();
        await db.Insertable(NewAttempt(executionB.Id, 1, now)).ExecuteCommandAsync();

        var rows = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == executionA.Id || a.ExecutionId == executionB.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.AttemptNo));
        Assert.Contains(rows, r => r.ExecutionId == executionA.Id);
        Assert.Contains(rows, r => r.ExecutionId == executionB.Id);
    }

    /// <summary>#5 四种结果的列投影 + 超长摘要截断(参数互不相同且有辨识性,防参数错位)。</summary>
    [Fact]
    public async Task Each_result_type_projects_into_the_expected_columns_with_summary_truncated()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        async Task<WfNodeExecutionAttempt> AppendFor(WfNodeExecutionResult result)
        {
            var execution = NewExecution(UniqueKey());
            await db.Insertable(execution).ExecuteCommandAsync();
            var claimed = await WfNodeExecutionStore.ClaimAsync(
                db, execution.Id, "worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
            Assert.NotNull(claimed);
            var now = DateTime.UtcNow;
            return await WfNodeExecutionAttemptStore.AppendAsync(db, claimed, result, now, now, CancellationToken.None);
        }

        const string outputJson = "{\"a\":1}";
        var succeeded = await AppendFor(WfNodeExecutionResult.Succeeded(outputJson: outputJson, summary: "ok"));
        Assert.Equal(WfNodeExecutionResultType.Succeeded, succeeded.ResultType);
        Assert.Equal("ok", succeeded.OutputSummary);
        Assert.Null(succeeded.ErrorCode);
        Assert.Null(succeeded.ErrorSummary);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(outputJson)));
        Assert.Equal(expectedHash, succeeded.OutputHash);

        var retryable = await AppendFor(WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: "r"));
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, retryable.ResultType);
        Assert.Equal(48001, retryable.ErrorCode);
        Assert.Equal("r", retryable.ErrorSummary);
        Assert.Null(retryable.OutputSummary);
        Assert.Null(retryable.OutputHash);

        var manualFallback = await AppendFor(WfNodeExecutionResult.ManualFallback(errorCode: 48002, summary: "m"));
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, manualFallback.ResultType);
        Assert.Equal(48002, manualFallback.ErrorCode);
        Assert.Equal("m", manualFallback.ErrorSummary);
        Assert.Null(manualFallback.OutputSummary);
        Assert.Null(manualFallback.OutputHash);

        var overlong = new string('x', 600);
        var terminal = await AppendFor(WfNodeExecutionResult.TerminalFailure(errorCode: 48003, summary: overlong));
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, terminal.ResultType);
        Assert.Equal(48003, terminal.ErrorCode);
        Assert.Equal(512, terminal.ErrorSummary!.Length);
    }

    /// <summary>#6 <c>AppendAsync</c> 处在被回滚的事务里 → 一行不留。</summary>
    [Fact]
    public async Task An_append_inside_a_rolled_back_transaction_leaves_no_trace()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var now = DateTime.UtcNow;
            await WfNodeExecutionAttemptStore.AppendAsync(
                db, claimed, WfNodeExecutionResult.Succeeded(summary: "ok"), now, now, CancellationToken.None);
            throw new InvalidOperationException("强制回滚,验证 append 不留痕。");
        });

        Assert.False(tran.IsSuccess);

        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .CountAsync());
    }

    // ────────────────────────── 脚手架 ──────────────────────────

    private static WfNodeExecution NewExecution(string executionKey) => new()
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

    private static WfNodeExecutionAttempt NewAttempt(long executionId, int attemptNo, DateTime nowUtc) => new()
    {
        ExecutionId = executionId,
        AttemptNo = attemptNo,
        StartedAtUtc = nowUtc,
        EndedAtUtc = nowUtc,
        ResultType = WfNodeExecutionResultType.Succeeded,
        OutputSummary = "ok",
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
