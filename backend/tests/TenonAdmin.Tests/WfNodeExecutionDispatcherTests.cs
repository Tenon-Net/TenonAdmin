using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfNodeExecutionDispatcher"/> 的 24 条契约测试(M3a-1 Task 6/7)——「领取 → 调 handler → 落结果」,
/// 第一次把 Task 2(SPI)/3(lease-fence 领取)/4(attempt)/5(outbox)装配起来。脚手架:用户经 HTTP(复用
/// <see cref="WfReceiptEngineTests"/> 的 <c>ClientFor</c>/<c>AddUser</c> 姿势,唯一需要认证的部分);流程定义
/// 版本 / 实例 / token 直接经 <see cref="ISqlSugarClient"/> 与 <see cref="IWorkflowEngine.ExecuteAsync"/> 构造
/// (不经 HTTP,发起 <c>StartInstanceCmd</c> 不需要 <c>wf_definition</c> 行),从而拿到<b>真实</b>的
/// <see cref="WfInstance"/>/<see cref="WfToken"/>/<see cref="WfDefinitionVersion"/> 三元组——execution 行的
/// <c>NodeId</c> 必须命中该版本模型树里的真实节点,否则回写时 <c>ctx.FindNode</c> 找不到节点。
/// </summary>
public class WfNodeExecutionDispatcherTests
{
    private const string Password = "Test@123456";

    // ── T1:事务边界 ──────────────────────────────────────────────────────

    /// <summary>
    /// handler 执行时刻意没有活动数据库事务(AI 基石 §4.6/§4.8 硬约束)。能让它转红的变异:把
    /// <c>handler.ExecuteAsync</c> 挪进 tx1 的 <c>UseTranAsync</c> 闭包里。
    /// </summary>
    [Fact]
    public async Task Handler_runs_with_no_active_database_transaction()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t1");
        var execution = await BuildExecutionAsync(db, s);

        // 初值故意设成"看见了事务"——只有 OnExecute 真的跑过并把它翻成 false,断言才有鉴别力。
        var sawTran = true;
        var sawTransactionObject = true;
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"), WfNodeType.Webhook)
        {
            OnExecute = () =>
            {
                sawTran = db.Ado.IsAnyTran();
                sawTransactionObject = db.Ado.Transaction is not null;
            },
        };
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.False(sawTran, "handler 执行时不应处于任何数据库事务中。");
        Assert.False(sawTransactionObject, "handler 执行时 db.Ado.Transaction 应为 null。");
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, status);

        // P2-3:WfNodeExecutionContext 投影快照——之前 12 条测试从未读过 LastContext。
        var ctx = handler.LastContext!;
        Assert.Equal(execution.ExecutionKey, ctx.ExecutionKey);
        Assert.Equal(s.InstanceId, ctx.InstanceId);
        Assert.Equal(s.Token.Id, ctx.TokenId);
        Assert.Equal(s.Token.NodeVisitId, ctx.NodeVisitId);
        Assert.Equal("node1", ctx.NodeId);
        Assert.Equal(1, ctx.Attempt); // AttemptCount 三处口径的第三处——handler 看见的那个数。
        Assert.Equal(TimeSpan.Zero, ctx.DeadlineAtUtc.Offset); // SpecifyKind(Utc) 的直接证据,任何时区都为真。
        Assert.NotNull(ctx.NodeProps?.Assignee);
    }

    // ── T2/T3:同一 ExecutionKey 只推进一次(双谓词 CAS) ─────────────────────

    /// <summary>老 owner 迟到回写:fence 谓词挡住。删掉 CAS 的 <c>Fence == fence</c> 会让它转红。</summary>
    [Fact]
    public async Task A_stale_fence_writeback_is_rejected_and_leaves_nothing_behind()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t2");
        var execution = await BuildExecutionAsync(db, s);

        var now = DateTime.UtcNow;
        var first = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(first);
        Assert.Equal(1, first.Fence);

        // 直接把租约打到过去,模拟"领了但一直没回写"。
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = now.AddMinutes(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var second = await ClaimAsync(db, execution.Id, "worker-b", now.AddMinutes(1));
        Assert.NotNull(second);
        Assert.Equal(2, second.Fence);

        var startedAtUtc = now;
        var endedAtUtc = now.AddSeconds(1);
        var ex = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(
            new NodeExecutionCompletedCmd
            {
                ExecutionId = execution.Id,
                Fence = 1, // 老 owner 手上的过期 fence
                Result = WfNodeExecutionResult.Succeeded(summary: "ok"),
                HandlerType = "test",
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
            }));
        Assert.Equal(48004, (int)ex.Code);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(2, reloaded.Fence);
        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var reloadedToken = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal("node1", reloadedToken.NodeId);

        var reloadedInstance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Running, reloadedInstance.Status);
    }

    /// <summary>同一 fence 的结果被回放两次:第二次必须被拒绝。删掉 CAS 的 <c>Status == Running</c> 会让它转红。</summary>
    [Fact]
    public async Task The_same_fence_can_write_back_only_once()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t3");
        var execution = await BuildExecutionAsync(db, s);

        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(claimed);

        var cmd = new NodeExecutionCompletedCmd
        {
            ExecutionId = execution.Id,
            Fence = claimed.Fence,
            Result = WfNodeExecutionResult.Succeeded(summary: "ok"),
            HandlerType = "test",
            StartedAtUtc = now,
            EndedAtUtc = now.AddSeconds(1),
        };

        await engine.ExecuteAsync(cmd);

        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
        var tokenAfterFirst = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        var instanceAfterFirst = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();

        // P3-1:先捕获异常、先断副作用,最后才断异常类型——原先"先断异常类型"的顺序下,删掉
        // Status == Running 谓词会让本方法红在一个误导人的 UNIQUE constraint failed 上,后面几条副作用
        // 断言根本执行不到(review B1 判定)。重排后原有每一条断言都还在,只是顺序变了。
        var ex = await Record.ExceptionAsync(() => engine.ExecuteAsync(cmd));

        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var tokenAfterSecond = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenAfterFirst.NodeId, tokenAfterSecond.NodeId);
        Assert.Equal(tokenAfterFirst.NodeVisitId, tokenAfterSecond.NodeVisitId);

        var instanceAfterSecond = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(instanceAfterFirst.Status, instanceAfterSecond.Status);

        var admin = Assert.IsType<AdminException>(ex);
        Assert.Equal(48004, (int)admin.Code);
    }

    [Fact]
    public async Task Quarantine_writeback_requires_the_claim_fence_and_is_atomic()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var started = await StartAsync(f, db, engine, "quarantine-fence");
        var execution = await BuildExecutionAsync(db, started);
        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(claimed);

        var ex = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(
            new NodeExecutionQuarantinedCmd
            {
                ExecutionId = execution.Id,
                Fence = claimed!.Fence - 1,
                Result = WfNodeExecutionResult.TerminalFailure(
                    WorkflowErrorCode.InstanceNotFound,
                    "上下文不可用(测试)"),
                StartedAtUtc = now,
                EndedAtUtc = now.AddSeconds(1),
            }));

        Assert.Equal(WorkflowErrorCode.InstanceStatusConflict, (int)ex.Code);
        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(claimed.Fence, reloaded.Fence);
        Assert.Empty(await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Empty(await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == execution.Id)
            .ToListAsync());
    }

    // ── P1-1:RetryScheduled 分支的 fence CAS(与终态分支各自独立写,两条谓词都要各自守门)──────

    /// <summary>
    /// 老 owner 迟到的 RetryableFailure 回写:重试分支的 fence 谓词挡住,不得把新 worker 的租约打回可领取
    /// 状态。删掉 <c>ClaimExecutionWritebackAsync</c> 重试分支 CAS 的 <c>Fence == fence</c> 会让它转红
    /// (review A4)。
    /// </summary>
    [Fact]
    public async Task A_stale_fence_retry_writeback_is_rejected_and_does_not_clear_the_new_lease()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t2b");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 3);

        var now = DateTime.UtcNow;
        var first = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(first);
        Assert.Equal(1, first.Fence);

        // 直接把租约打到过去,模拟"领了但一直没回写"。
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = now.AddMinutes(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var second = await ClaimAsync(db, execution.Id, "worker-b", now.AddMinutes(1));
        Assert.NotNull(second);
        Assert.Equal(2, second.Fence);

        var startedAtUtc = now;
        var endedAtUtc = now.AddSeconds(1);
        var ex = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(
            new NodeExecutionCompletedCmd
            {
                ExecutionId = execution.Id,
                Fence = 1, // 老 owner 手上的过期 fence
                Result = WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: "transient"),
                HandlerType = "test",
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
            }));
        Assert.Equal(48004, (int)ex.Code);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(2, reloaded.Fence);
        Assert.Equal("worker-b", reloaded.LeaseOwner); // 关键:租约没被老 owner 的迟到回写清掉。
        Assert.Null(reloaded.NextRetryAtUtc);
        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    /// <summary>
    /// 重试分支同一 fence 的结果被回放两次:第二次必须被拒绝。删掉重试分支 CAS 的
    /// <c>Status == Running</c> 会让它转红(review A5)。
    /// </summary>
    [Fact]
    public async Task The_same_fence_can_schedule_a_retry_only_once()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t3b");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 3);

        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(claimed);

        var cmd = new NodeExecutionCompletedCmd
        {
            ExecutionId = execution.Id,
            Fence = claimed.Fence,
            Result = WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: "transient"),
            HandlerType = "test",
            StartedAtUtc = now,
            EndedAtUtc = now.AddSeconds(1),
        };

        await engine.ExecuteAsync(cmd);

        var reloadedAfterFirst = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, reloadedAfterFirst.Status);
        Assert.NotNull(reloadedAfterFirst.NextRetryAtUtc);
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());

        var ex = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(cmd));
        Assert.Equal(48004, (int)ex.Code);

        var reloadedAfterSecond = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(reloadedAfterFirst.NextRetryAtUtc, reloadedAfterSecond.NextRetryAtUtc);
    }

    // ── T4/T5/T6:RetryableFailure ──────────────────────────────────────────

    /// <summary>
    /// 预算未耗尽:行进 <c>RetryScheduled</c>,租约释放。变异:(a) <c>RetryScheduled</c> 分支不写
    /// <c>NextRetryAtUtc</c>(留 null);(b) outbox 入队条件从"终态"放宽成"无条件"。
    /// </summary>
    [Fact]
    public async Task Retryable_failure_schedules_a_retry_and_releases_the_lease()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t4");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 3);

        var beforeUtc = DateTime.UtcNow;
        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: "transient"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, reloaded.Status);
        Assert.NotNull(reloaded.NextRetryAtUtc);
        Assert.True(reloaded.NextRetryAtUtc!.Value > beforeUtc, "NextRetryAtUtc 必须在未来。");
        Assert.Null(reloaded.LeaseOwner);
        Assert.Null(reloaded.LeaseExpiresAtUtc);
        Assert.Null(reloaded.CompletedTimeUtc);

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, attempt.ResultType);

        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal("node1", token.NodeId);
    }

    /// <summary>预算耗尽(<c>MaxAttempts = 1</c>):一次可重试失败即转 <c>Failed</c>。变异:预算判定 <c>&gt;=</c> 改 <c>&gt;</c>。</summary>
    [Fact]
    public async Task Retryable_failure_past_the_budget_fails_terminally()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t5");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 1);

        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.RetryableFailure(errorCode: 48002, summary: "dead"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var beforeUtc = DateTime.UtcNow;
        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Failed, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Failed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedTimeUtc);
        Assert.Equal(48002, reloaded.ErrorCode);
        Assert.Equal(typeof(FakeNodeHandler).FullName, reloaded.HandlerType); // P2-1:真读回真断值,不是 NotNull
        Assert.Equal("dead", reloaded.Summary);                                // P2-1:真读回真断值
        Assert.Equal(beforeUtc, reloaded.CompletedTimeUtc!.Value, TimeSpan.FromSeconds(10)); // P2-1:断值

        var outbox = Assert.Single(
            await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).ToListAsync());
        Assert.Equal("wf.node-execution.completed", outbox.MessageType); // 字面值,不是常量(禁写清单 #10)
        Assert.Equal($"{reloaded.ExecutionKey}:wf.node-execution.completed", outbox.MessageKey);
        var payload = JsonDocument.Parse(outbox.PayloadJson!).RootElement;
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Equal(execution.Id, payload.GetProperty("executionId").GetInt64());
        Assert.False(payload.TryGetProperty("outputJson", out _)); // D6:OutputJson 正文绝不进 payload
    }

    /// <summary>
    /// P2-1 截断用例:超长 summary 落库前经 <see cref="WfNodeExecutionAttemptStore.Truncate"/> 截到 512——
    /// 与 attempt 表的 <c>OutputSummary</c>/<c>ErrorSummary</c> 同一份规则(Task 5 P3-1 教训);这里验证复用
    /// 真的作用在了 <c>wf_node_execution.Summary</c> 上,而不只是没有第二份截断代码。
    /// </summary>
    [Fact]
    public async Task Summary_longer_than_the_column_width_is_truncated_to_512()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t5trunc");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 1);

        var longSummary = new string('x', 600);
        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.RetryableFailure(errorCode: 48002, summary: longSummary), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(512, reloaded.Summary!.Length);
    }

    /// <summary>
    /// 退避下界:<c>RetryAfter = 0</c> → 忽略,退到 30s 基线(<c>AttemptCount = 1</c> → <c>30 &lt;&lt; 0</c>)。
    /// P3-2 从原 <c>Handler_supplied_retry_delay_is_clamped_at_both_ends</c> 拆出(review B2:前段失败会掩盖
    /// 后段,已在本 loop 误导过一次结论),断言逐字搬运。变异:<c>ResolveRetryDelay</c> 首行退化成
    /// <c>return result.RetryAfter ?? 默认;</c>(去掉钳制)。
    /// </summary>
    [Fact]
    public async Task Handler_supplied_retry_delay_below_the_lower_bound_falls_back_to_the_base_backoff()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        // 下界:RetryAfter = 0。
        var sLow = await StartAsync(f, db, engine, "t6low");
        var lowExecution = await BuildExecutionAsync(db, sLow, maxAttempts: 5);
        var beforeLow = DateTime.UtcNow;
        var lowHandler = new FakeNodeHandler(WfNodeExecutionResult.RetryableFailure(retryAfter: TimeSpan.Zero), WfNodeType.Webhook);
        var lowDispatcher = new WfNodeExecutionDispatcher(db, [lowHandler], engine, TimeProvider.System);
        await lowDispatcher.RunAsync(lowExecution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        var lowReloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == lowExecution.Id).FirstAsync();
        Assert.NotNull(lowReloaded.NextRetryAtUtc);
        Assert.Equal(beforeLow.AddSeconds(30), lowReloaded.NextRetryAtUtc!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handler_supplied_retry_delay_in_range_is_used_exactly()
    {
        var fixedNow = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
        var clock = new MutableTime(fixedNow);
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var bootstrapEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, bootstrapEngine, "t6inrange");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 5);
        var engine = ActivatorUtilities.CreateInstance<WorkflowEngine>(scope.ServiceProvider, clock);
        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.RetryableFailure(retryAfter: TimeSpan.FromMinutes(7)), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, clock);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, status);
        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, reloaded.Status);
        Assert.Equal(fixedNow.UtcDateTime.AddMinutes(7), reloaded.NextRetryAtUtc);
    }

    /// <summary>
    /// 退避上界:<c>RetryAfter = 3650 天</c>——超出 <c>(0, 24h]</c> 的值被忽略,退回默认退避(而不是钳到 24h)。
    /// P3-2 从原 <c>Handler_supplied_retry_delay_is_clamped_at_both_ends</c> 拆出,断言逐字搬运。变异:同上界
    /// 的变异会让本条转红,而下界那条不受影响——两条各自独占鉴别力。
    /// </summary>
    [Fact]
    public async Task Handler_supplied_retry_delay_above_the_upper_bound_is_ignored()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        // 上界:RetryAfter = 3650 天——超出 (0, 24h] 的值被忽略,退回默认退避(而不是钳到 24h;
        // 语义契约字面写的是"否则(含 null、<=0、>24h)→ 30s << min(AttemptCount-1,5)")。用一个真实的量值
        // 上界断言(<= 24h + 容差)钉住"没有被原样采用而逼近 DateTime.MaxValue"这件事,不是松散的 "> now"。
        var sHigh = await StartAsync(f, db, engine, "t6high");
        var highExecution = await BuildExecutionAsync(db, sHigh, maxAttempts: 5);
        var beforeHigh = DateTime.UtcNow;
        var highHandler = new FakeNodeHandler(
            WfNodeExecutionResult.RetryableFailure(retryAfter: TimeSpan.FromDays(3650)), WfNodeType.Webhook);
        var highDispatcher = new WfNodeExecutionDispatcher(db, [highHandler], engine, TimeProvider.System);
        await highDispatcher.RunAsync(highExecution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        var highReloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == highExecution.Id).FirstAsync();
        Assert.NotNull(highReloaded.NextRetryAtUtc);
        Assert.True(
            highReloaded.NextRetryAtUtc!.Value <= beforeHigh.AddHours(24).AddSeconds(5),
            $"NextRetryAtUtc={highReloaded.NextRetryAtUtc:O} 越过了 24h 上界,RetryAfter 没有被钳制/忽略。");
        // 量值断言(P2-2):单边松界 <= 24h 连"钳到 24h"这个被注释明文排除的实现都放过,补一条精确到
        // 30s 基线的量值断言,和下界那条同款。
        Assert.Equal(beforeHigh.AddSeconds(30), highReloaded.NextRetryAtUtc!.Value, TimeSpan.FromSeconds(5));
    }

    // ── T7/T8:ManualFallback ────────────────────────────────────────────────

    /// <summary>
    /// 建人工待办,不重新进入节点(<c>NodeVisitId</c> 不变)。变异:(a) 换成 <c>new EnterNodeOp(node)</c>
    /// → <c>NodeVisitId</c> 会变;(b) 删掉 <c>CreateTaskAsync</c> 调用 → 无任务。
    /// </summary>
    [Fact]
    public async Task Manual_fallback_creates_a_task_at_the_same_node_without_re_entering_it()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t7");
        var execution = await BuildExecutionAsync(db, s);

        var tokenBefore = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();

        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.ManualFallback(errorCode: 48003, summary: "handler broke"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, status);

        var tasks = await db.Queryable<WfTask>()
            .Where(t => t.NodeId == "node1" && t.InstanceId == s.InstanceId)
            .ToListAsync();
        Assert.Equal(2, tasks.Count); // 一开始真实进入 node1 建的那一件 + ManualFallback 新建的一件。
        var newTask = Assert.Single(tasks, t => t.Id != s.PreexistingTaskId);
        Assert.Equal(s.Token.Id, newTask.TokenId);
        Assert.Equal("node1", newTask.NodeId);
        Assert.Equal(tokenBefore.NodeVisitId, newTask.NodeVisitId);

        var tokenAfter = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenBefore.NodeId, tokenAfter.NodeId);
        Assert.Equal(tokenBefore.NodeVisitId, tokenAfter.NodeVisitId);

        var actors = await db.Queryable<WfTaskActor>().Where(a => a.TaskId == newTask.Id).ToListAsync();
        Assert.Contains(actors, a => a.UserId == s.AssigneeUserId);

        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    /// <summary>
    /// 未配置 <c>assignee</c>:不建任务、不自动放行。变异:早返回换成 <c>await EnterApprovalAsync(ctx, ct)</c>
    /// (落到默认 <c>autoPass</c>)→ 实例被自动放行完结。
    /// </summary>
    [Fact]
    public async Task Manual_fallback_without_an_assignee_never_auto_passes()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t8");
        var execution = await BuildExecutionAsync(db, s);

        // token 真实位置不变,只是把这份版本的 ModelJson 换成"node1 无 assignee"——
        // 手法同 Task 3 review「直接 UPDATE 时间戳到过去」:操纵已存在的真实行,不是伪造内存对象。
        await db.Updateable<WfDefinitionVersion>()
            .SetColumns(v => new WfDefinitionVersion { ModelJson = WfModelJson.Serialize(BuildModel(null)) })
            .Where(v => v.Id == s.DefinitionVersionId)
            .ExecuteCommandAsync();

        var tokenBefore = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();

        var handler = new FakeNodeHandler(WfNodeExecutionResult.ManualFallback(summary: "handler broke"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, status);

        var tasks = await db.Queryable<WfTask>()
            .Where(t => t.NodeId == "node1" && t.InstanceId == s.InstanceId)
            .ToListAsync();
        var onlyTask = Assert.Single(tasks); // 只有一开始真实进入 node1 建的那一件,ManualFallback 没建新任务。
        Assert.Equal(s.PreexistingTaskId, onlyTask.Id);

        var tokenAfter = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenBefore.NodeId, tokenAfter.NodeId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Running, instance.Status); // 不是 Approved——没被自动放行。
    }

    /// <summary>
    /// 配了 assignee 但解析出 0 人(如 userId 指向一个不存在的用户):同上,不建任务、不自动放行(P1-2)。
    /// 这是 <see cref="WfManualFallbackOp"/> 的第二条自动放行出口,与上一条覆盖的第一条(provider 空白)是
    /// 同一个"自动节点执行失败后被静默自动放行"的两半——生产上比"压根没配 provider"更常见。变异:把
    /// <c>users.Count == 0</c> 的早返回换成 <c>await EnterApprovalAsync(ctx, ct)</c> 会让它转红。
    /// </summary>
    [Fact]
    public async Task Manual_fallback_with_zero_resolved_approvers_never_auto_passes()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t8b");
        var execution = await BuildExecutionAsync(db, s);

        // provider 存在(User),但 userId 指向一个不存在的用户——ApproverResolver(FilterEnabledAsync)
        // 会正常返回 0 人,而不是抛异常。
        await db.Updateable<WfDefinitionVersion>()
            .SetColumns(v => new WfDefinitionVersion { ModelJson = WfModelJson.Serialize(BuildModel(999_999_999L)) })
            .Where(v => v.Id == s.DefinitionVersionId)
            .ExecuteCommandAsync();

        var tokenBefore = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();

        var handler = new FakeNodeHandler(WfNodeExecutionResult.ManualFallback(summary: "handler broke"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, status);

        var tasks = await db.Queryable<WfTask>()
            .Where(t => t.NodeId == "node1" && t.InstanceId == s.InstanceId)
            .ToListAsync();
        var onlyTask = Assert.Single(tasks); // 只有一开始真实进入 node1 建的那一件,ManualFallback 没建新任务。
        Assert.Equal(s.PreexistingTaskId, onlyTask.Id);

        var tokenAfter = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenBefore.NodeId, tokenAfter.NodeId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Running, instance.Status); // 不是 Approved——没被自动放行。
    }

    // ── T9:外部撤销的结果被丢弃 ────────────────────────────────────────────

    /// <summary>
    /// 领取后、回写前实例被外部撤销 → 结果丢弃,execution 落 <c>Cancelled</c>。变异:删掉
    /// <see cref="WorkflowEngine.ResolveExecutionOutcome"/> 里的实例状态前置判定 → 走 <c>TakeTransitionOp</c>
    /// → <c>ClaimInstanceAsync(Running)</c> 抛 48004 → execution 停在 <c>Running</c>。
    /// </summary>
    [Fact]
    public async Task A_result_for_a_no_longer_running_instance_is_discarded()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t9");
        var execution = await BuildExecutionAsync(db, s);

        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"), WfNodeType.Webhook)
        {
            // 领取已成功、handler 正在跑——此刻外部把实例撤销,模拟"结果算出来时实例已经不是 Running 了"。
            OnExecute = () => db.Updateable<WfInstance>()
                .SetColumns(i => new WfInstance { Status = WfInstanceStatus.Cancelled })
                .Where(i => i.Id == s.InstanceId)
                .ExecuteCommand(),
        };
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Cancelled, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Cancelled, reloaded.Status);
        Assert.NotNull(reloaded.CompletedTimeUtc);

        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal("node1", token.NodeId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Cancelled, instance.Status);
    }

    /// <summary>
    /// execution fence 只证明 owner 仍拥有 execution 行；token 已进入新 visit 时，旧结果也不得拿当前
    /// token 继续旧节点的 transition。这里直接模拟已提交的 token 重定位，避免把该防线只间接绑定在
    /// Resubmit 的 active-execution invalidation 上。变异：移除 token visit 判定 → 旧成功结果会把 token
    /// 推离 replacement visit，execution 也会错误落为 Succeeded。
    /// </summary>
    [Fact]
    public async Task A_result_for_a_superseded_token_visit_is_cancelled_without_advancing_the_replacement_visit()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t9-token-visit");
        var execution = await BuildExecutionAsync(db, s);
        var replacementVisitId = execution.NodeVisitId!.Value + 1;
        var replacementVersion = s.Token.Version + 1;

        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "late-ok"), WfNodeType.Webhook)
        {
            OnExecute = () => db.Updateable<WfToken>()
                .SetColumns(t => new WfToken
                {
                    NodeVisitId = replacementVisitId,
                    Version = replacementVersion,
                })
                .Where(t => t.Id == s.Token.Id && t.NodeVisitId == execution.NodeVisitId)
                .ExecuteCommand(),
        };
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Cancelled, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Cancelled, reloaded.Status);

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Active, token.Status);
        Assert.Equal("node1", token.NodeId);
        Assert.Equal(replacementVisitId, token.NodeVisitId);
        Assert.Equal(replacementVersion, token.Version);
    }

    // ── T10:无注册 handler ────────────────────────────────────────────────

    /// <summary>
    /// 找不到匹配 handler → 合成 <c>TerminalFailure(48008)</c> 走正常回写路径,不抛异常。变异:改回抛异常
    /// → 无 execution 终态、无 attempt 行。
    /// </summary>
    [Fact]
    public async Task A_node_type_with_no_registered_handler_fails_terminally()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t10");
        var execution = await BuildExecutionAsync(db, s);

        var dispatcher = new WfNodeExecutionDispatcher(db, [], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Failed, status);

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, attempt.ResultType);
        Assert.Equal(48008, attempt.ErrorCode); // 字面值,不是常量(禁写清单 #10)——钉住上线数字,不是钉住产品代码自己算出的值。
        Assert.False(string.IsNullOrEmpty(attempt.ErrorSummary));

        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    // ── T11:领不到什么都不做 ──────────────────────────────────────────────

    /// <summary>已是终态的行领不到 → <c>RunAsync</c> 返回 <c>null</c>,handler 不被调用,无副作用行。变异:忽略 <c>ClaimAsync</c> 的 <c>null</c> 返回、照常调 handler。</summary>
    [Fact]
    public async Task An_unclaimable_execution_does_nothing_at_all()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t11");
        var execution = await BuildExecutionAsync(db, s);

        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { Status = WfNodeExecutionStatus.Succeeded })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(status);
        Assert.Equal(0, handler.CallCount);

        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    // ── T12:AttemptNo 口径 ───────────────────────────────────────────────

    /// <summary>
    /// 两次跑(第一次 RetryableFailure,第二次 Succeeded)→ 两行 attempt 的 <c>AttemptNo</c> 分别是 1、2,
    /// 第二行等于回写时 <c>execution.AttemptCount</c>。变异:(a) 任一处口径 <c>+ 1</c>;(b) 给 <c>AppendAsync</c>
    /// 传 <c>started, started</c> 两个同值。
    /// </summary>
    [Fact]
    public async Task Attempt_numbers_follow_the_claim_count_and_are_never_double_incremented()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "t12");
        var execution = await BuildExecutionAsync(db, s, maxAttempts: 5);
        var clock = new MutableTime(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));

        var handler1 = new FakeNodeHandler(WfNodeExecutionResult.RetryableFailure(summary: "first"), WfNodeType.Webhook)
        {
            OnExecute = () => clock.Advance(TimeSpan.FromSeconds(1)),
        };
        var dispatcher1 = new WfNodeExecutionDispatcher(db, [handler1], engine, clock);
        var status1 = await dispatcher1.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, status1);

        // 直接把重试时刻推到过去,使用同一 MutableTime。先落局部变量,避免 zh-CN 下
        // SqlSugar 把内联 DateTime 按区域格式化进 SQL(near "上午")。
        var pastRetryAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { NextRetryAtUtc = pastRetryAtUtc })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handler2 = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "second"), WfNodeType.Webhook)
        {
            OnExecute = () => clock.Advance(TimeSpan.FromSeconds(1)),
        };
        var dispatcher2 = new WfNodeExecutionDispatcher(db, [handler2], engine, clock);
        var status2 = await dispatcher2.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, status2);

        var reloadedExecution = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        var attempts = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(1, attempts[0].AttemptNo);
        Assert.Equal(2, attempts[1].AttemptNo);
        Assert.Equal(reloadedExecution.AttemptCount, attempts[1].AttemptNo);

        foreach (var attempt in attempts)
        {
            Assert.NotEqual(attempt.StartedAtUtc, attempt.EndedAtUtc);
            Assert.True(attempt.EndedAtUtc > attempt.StartedAtUtc);
        }
    }

    // ── N1/N2:Task 7 补的两条结果路径增量(Succeeded 推进副作用 / handler 自己返回的 TerminalFailure)──

    /// <summary>
    /// Succeeded 的推进副作用(D4 增量):T1 只断了 status/CallCount/context 投影,从未断过
    /// <see cref="TakeTransitionOp"/> 的推进效果(token/instance/history)与 attempt/outbox 行。全部从库读回再断。
    /// 变异:(a) <c>case Succeeded</c> 改成 <c>break</c>(不 Plan(TakeTransitionOp))→ instance 仍 Running、
    /// token 仍 Active;(b) <c>ActorType = Worker</c> 改成 <c>Human</c>(这一列全仓目前零断言);(c)
    /// <c>WfNodeExecutionAttemptStore</c> 的 <c>OutputSummary</c> 映射丢掉。
    /// </summary>
    [Fact]
    public async Task A_successful_run_advances_the_token_and_completes_the_instance()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var bootstrapEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, bootstrapEngine, "n1");
        var execution = await BuildExecutionAsync(db, s);

        var clock = new MutableTime(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var beforeUtc = clock.GetUtcNow().UtcDateTime;
        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "ok"), WfNodeType.Webhook)
        {
            OnExecute = () => clock.Advance(TimeSpan.FromSeconds(1)),
        };
        var engine = ActivatorUtilities.CreateInstance<WorkflowEngine>(scope.ServiceProvider, clock);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, clock);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Succeeded, reloaded.Status);
        Assert.Equal(1, reloaded.Fence);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Null(reloaded.ErrorCode);
        Assert.Null(reloaded.Summary);
        Assert.Equal(typeof(FakeNodeHandler).FullName, reloaded.HandlerType);
        Assert.Equal(beforeUtc.AddSeconds(1), reloaded.CompletedTimeUtc);

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(1, attempt.AttemptNo);
        Assert.Equal(WfNodeExecutionResultType.Succeeded, attempt.ResultType);
        Assert.Equal("ok", attempt.OutputSummary);
        Assert.Null(attempt.ErrorSummary);
        Assert.Equal(beforeUtc, attempt.StartedAtUtc);
        Assert.Equal(beforeUtc.AddSeconds(1), attempt.EndedAtUtc);
        Assert.True(attempt.EndedAtUtc > attempt.StartedAtUtc);

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Completed, token.Status);
        Assert.Equal("node1", token.NodeId); // 模型 start → node1 → null,next is null 时 NodeId 不变(D10)。
        Assert.Equal(s.Token.NodeVisitId, token.NodeVisitId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Approved, instance.Status);

        var history = await db.Queryable<WfHistory>().Where(h => h.InstanceId == s.InstanceId).ToListAsync();
        Assert.Single(history, h => h.EventType == WfHistoryEventType.InstanceCompleted);
        // 只看 node1 的 NodeLeave——StartAsync 发起时离开 start 节点那条是真实用户触发(ActorType=Human),
        // 与本测试要钉的「dispatcher 推进时的 NodeLeave 是 Worker」是两回事,混着断会被那条 Human 拖累。
        var nodeLeavesAtNode1 = history.Where(h => h.EventType == WfHistoryEventType.NodeLeave && h.NodeId == "node1").ToList();
        Assert.NotEmpty(nodeLeavesAtNode1);
        Assert.All(nodeLeavesAtNode1, h => Assert.Equal(WfHistoryActorType.Worker, h.ActorType));

        var outbox = Assert.Single(await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).ToListAsync());
        Assert.Equal("wf.node-execution.completed", outbox.MessageType); // 字面值,不是常量(禁写清单 #10)。
        var payload = JsonDocument.Parse(outbox.PayloadJson!).RootElement;
        Assert.Equal("succeeded", payload.GetProperty("status").GetString());
    }

    /// <summary>
    /// handler 自己返回 <c>TerminalFailure</c>(与 T10 的合成路径不同):<c>ErrorCode</c>/<c>Summary</c> 必须
    /// 原样带出,不是 dispatcher 写死的 48008——用一个不同的字面码(48003)证明。实例不一起终止(D4)。
    /// 变异:(a) <c>case TerminalFailure</c> 错配成 <c>Plan(TakeTransitionOp)</c> → instance Approved;(b)
    /// 错配成 <c>Plan(WfManualFallbackOp)</c> → 多一行 wf_task;(c) 丢掉 <c>result.ErrorCode</c>/<c>Summary</c>
    /// (写 null)——T10 抓不到 (c),它的 48008 是合成时写死的。
    /// </summary>
    [Fact]
    public async Task A_handler_returned_terminal_failure_stops_the_execution_without_touching_the_instance()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n2");
        var execution = await BuildExecutionAsync(db, s);

        var beforeUtc = DateTime.UtcNow;
        var handler = new FakeNodeHandler(
            WfNodeExecutionResult.TerminalFailure(errorCode: 48003, summary: "boom"), WfNodeType.Webhook);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Failed, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Failed, reloaded.Status);
        Assert.Equal(48003, reloaded.ErrorCode); // 字面值,刻意 ≠ 合成路径的 48008。
        Assert.Equal("boom", reloaded.Summary);
        Assert.Equal(typeof(FakeNodeHandler).FullName, reloaded.HandlerType);
        Assert.Equal(beforeUtc, reloaded.CompletedTimeUtc!.Value, TimeSpan.FromSeconds(10));

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, attempt.ResultType);
        Assert.Equal("boom", attempt.ErrorSummary);
        Assert.Null(attempt.OutputSummary);

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Active, token.Status); // 不推进。
        Assert.Equal("node1", token.NodeId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Running, instance.Status); // 实例不一起终止(D4)。

        var tasks = await db.Queryable<WfTask>()
            .Where(t => t.NodeId == "node1" && t.InstanceId == s.InstanceId)
            .ToListAsync();
        Assert.Single(tasks); // 只有一开始真实进入 node1 建的那一件,没建兜底任务。

        var outbox = Assert.Single(await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).ToListAsync());
        var payload = JsonDocument.Parse(outbox.PayloadJson!).RootElement;
        Assert.Equal("failed", payload.GetProperty("status").GetString());
    }

    // ── N3/N4:未知异常受控收敛 / OCE 保持取消语义 ─────────────────────────────

    /// <summary>
    /// 未知非取消异常在 handler 调用边界收敛为受控 retryable 结果：tx2 正常提交一条 attempt、
    /// execution 进入 RetryScheduled 并释放租约，不把异常正文写入审计。有限预算与最终 Failed
    /// 的完整循环由 <see cref="WfNodeExecutionExceptionTests"/> 覆盖。
    /// </summary>
    [Fact]
    public async Task An_unknown_handler_exception_is_converted_to_a_retryable_attempt()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n3");
        var execution = await BuildExecutionAsync(db, s);

        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook)
        {
            OnExecute = () => throw new InvalidOperationException("crash"),
        };
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var ex = await Record.ExceptionAsync(
            () => dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.Equal(1, handler.CallCount); // OnExecute 在 CallCount++ 之后调用,值必然是 1(D8)。
        Assert.Null(ex);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, reloaded.Status);
        Assert.Equal(1, reloaded.Fence);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Null(reloaded.LeaseOwner);
        Assert.Null(reloaded.LeaseExpiresAtUtc);
        Assert.NotNull(reloaded.NextRetryAtUtc);
        Assert.Null(reloaded.CompletedTimeUtc);
        Assert.Null(reloaded.Summary);

        var attempt = Assert.Single(await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, attempt.ResultType);
        Assert.Equal(48032, attempt.ErrorCode);
        Assert.DoesNotContain("crash", attempt.ErrorSummary ?? "");
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Active, token.Status);
        Assert.Equal("node1", token.NodeId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Running, instance.Status);

    }

    /// <summary>
    /// 取消语义(Task 2 定案):handler 抛 <see cref="OperationCanceledException"/> 不归进任何结果分支,行为
    /// 与 N3 同形(Running 持租约、0 行 attempt/outbox)。用 <c>ThrowsAnyAsync</c> 不用 <c>ThrowsAsync</c>——
    /// xUnit 的 <c>ThrowsAsync&lt;T&gt;</c> 是精确类型匹配,<c>TaskCanceledException</c>(OCE 子类)会漏。
    /// 变异:加 <c>catch (OperationCanceledException) { result = TerminalFailure(...); }</c>(或
    /// <c>RetryableFailure</c>)→ 转红。N3 抓不到这个变异(它抛的不是 OCE)。
    /// </summary>
    [Fact]
    public async Task A_cancelled_handler_is_not_folded_into_any_result_branch()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n4");
        var execution = await BuildExecutionAsync(db, s);

        var handler = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook)
        {
            OnExecute = () => throw new OperationCanceledException(),
        };
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var beforeUtc = DateTime.UtcNow;
        var ex = await Record.ExceptionAsync(
            () => dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(1, reloaded.Fence);
        // P2-3:租约两列(N3 同款)——OCE 被拒绝归进任何结果分支,行必须仍持有 worker-a 的租约。
        Assert.Equal("worker-a", reloaded.LeaseOwner);
        Assert.Equal(beforeUtc.AddMinutes(5), reloaded.LeaseExpiresAtUtc!.Value, TimeSpan.FromSeconds(10));

        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        // P3-1:异常类型断在最后——先钉副作用,免得变异下 ThrowsAnyAsync 抢先抛出、把后面的行全部跳过。
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    // ── N5/N6/N7:崩溃恢复(租约过期用应用时间 UPDATE 模拟,D1/语义契约 lease/fence 定案)────────────

    /// <summary>
    /// 崩溃恢复 (a):worker-a 崩溃(N3 的形状)→ 租约打到过去 → worker-b 重新领取并跑完。Fence 1→2,
    /// <c>AttemptCount</c> 三处口径在一个 ≠1 的值(2)上同时钉住;<c>AttemptCount − count(attempt) == 1</c>
    /// = 崩溃次数(Task 4 定案,全仓首次落测)。变异:(a) 删
    /// <see cref="WfNodeExecutionStore.ClaimAsync"/> 领取谓词第三支的 <c>Status == Running</c> → worker-b
    /// 领不到、<c>RunAsync</c> 返回 null(崩溃恢复 (a) 的直接守门);(b) <c>AppendAsync</c> 的
    /// <c>AttemptNo = execution.AttemptCount</c> 改成常量 1;(c) <c>BuildContextAsync</c> 的
    /// <c>Attempt = execution.AttemptCount</c> 改成常量 1(T1 抓不到,那里的期望值本来就是 1)。
    /// </summary>
    [Fact]
    public async Task An_expired_lease_lets_another_worker_reclaim_and_finish_the_execution()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n5");
        var execution = await BuildExecutionAsync(db, s);

        var handlerA = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook)
        {
            OnExecute = () => throw new OperationCanceledException(),
        };
        var dispatcherA = new WfNodeExecutionDispatcher(db, [handlerA], engine, TimeProvider.System);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcherA.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None));

        var past = DateTime.UtcNow.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = past })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handlerB = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "b-ok"), WfNodeType.Webhook);
        var dispatcherB = new WfNodeExecutionDispatcher(db, [handlerB], engine, TimeProvider.System);
        var status = await dispatcherB.RunAsync(execution.Id, "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, status);

        Assert.Equal(1, handlerB.CallCount);
        Assert.Equal(2, handlerB.LastContext!.Attempt); // AttemptCount 三处口径的第三处,在一个 ≠1 的值上。

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(2, reloaded.Fence);
        Assert.Equal(2, reloaded.AttemptCount);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, reloaded.Status);
        Assert.Equal("worker-b", reloaded.LeaseOwner); // 终态回写不清租约列(陷阱 2)。

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(2, attempt.AttemptNo);
        Assert.Equal("b-ok", attempt.OutputSummary);

        Assert.Equal(1, reloaded.AttemptCount - attempts.Count); // 领了但没返回的次数 = 崩溃次数(Task 4 定案)。

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Completed, token.Status);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Approved, instance.Status);

        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    /// <summary>
    /// 崩溃恢复 (b),回写方向:老 owner(fence=1,经 <c>ClaimAsync</c> helper 领取,从未经 dispatcher 回写)
    /// 租约过期后,新 worker 经 <c>RunAsync</c> 真跑完 Succeeded(token Completed、instance Approved、
    /// 1 attempt、1 outbox)。老 owner 迟到的 fence=1 回写必须被拒绝,且不改动任何已推进的副作用——先断
    /// 副作用,最后才断异常类型 + 字面 48004。<b>N6 钉的是 DONE-CONDITION 的观察面不变量,不是单条 CAS
    /// 谓词</b>——单条谓词的鉴别力已由既有 T2/T3/A4/A5 承担;只删单条谓词时本测试仍绿(另一条自己挡住)。
    /// </summary>
    [Fact]
    public async Task A_crashed_workers_late_writeback_cannot_advance_the_execution_a_second_time()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n6");
        var execution = await BuildExecutionAsync(db, s);

        var now = DateTime.UtcNow;
        var first = await ClaimAsync(db, execution.Id, "worker-a", now);
        Assert.NotNull(first);
        Assert.Equal(1, first.Fence);

        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = now.AddMinutes(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handlerB = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "b-ok"), WfNodeType.Webhook);
        var dispatcherB = new WfNodeExecutionDispatcher(db, [handlerB], engine, TimeProvider.System);
        var statusB = await dispatcherB.RunAsync(execution.Id, "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, statusB);

        var afterB = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(2, afterB.Fence);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, afterB.Status);
        var attemptsAfterB = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        Assert.Single(attemptsAfterB);
        var tokenAfterB = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        var instanceAfterB = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Approved, instanceAfterB.Status);
        var instanceCompletedAfterB = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == s.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .CountAsync();
        Assert.Equal(1, instanceCompletedAfterB);

        // 老 owner 迟到回写:fence=1,ResolveExecutionOutcome 此刻会先算出 Cancelled(instance 已非
        // Running,陷阱 7),但 48004 来自其后的 CAS(Fence/Status 都不匹配),结论不变——不断言 outcome。
        var ex = await Record.ExceptionAsync(() => engine.ExecuteAsync(
            new NodeExecutionCompletedCmd
            {
                ExecutionId = execution.Id,
                Fence = 1,
                Result = WfNodeExecutionResult.Succeeded(summary: "a-late"),
                HandlerType = "test",
                StartedAtUtc = now,
                EndedAtUtc = now.AddSeconds(1),
            }));

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Succeeded, reloaded.Status);
        Assert.Equal(2, reloaded.Fence);
        Assert.Equal(2, reloaded.AttemptCount);
        Assert.Equal(afterB.CompletedTimeUtc, reloaded.CompletedTimeUtc);

        var attempts = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal(2, attempt.AttemptNo);

        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var token = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenAfterB.Status, token.Status);
        Assert.Equal(tokenAfterB.NodeId, token.NodeId);
        Assert.Equal(tokenAfterB.NodeVisitId, token.NodeVisitId);

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Approved, instance.Status);

        var instanceCompletedAfter = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == s.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .CountAsync();
        Assert.Equal(1, instanceCompletedAfter); // 「只推进一次」最直白的证据。

        var admin = Assert.IsType<AdminException>(ex);
        Assert.Equal(48004, (int)admin.Code);
    }

    /// <summary>
    /// 崩溃恢复 (b),领取方向:worker-a 经 <c>RunAsync</c> 跑完 Succeeded 后,终态回写不清租约列
    /// (<see cref="WorkflowEngine.ClaimExecutionWritebackAsync"/> 终态分支的 <c>SetColumns</c> 只写 5 列,
    /// 陷阱 2),此刻仍带着 worker-a 的租约——把它打到过去,worker-b 仍不得重新领取。本 Task 鉴别力最强的
    /// 一条:对现有 338 条全绿的变异——从 <see cref="WfNodeExecutionStore.ClaimAsync"/> 领取谓词第三支删掉
    /// <c>e.Status == WfNodeExecutionStatus.Running &amp;&amp;</c>(只留 <c>LeaseExpiresAtUtc &lt; nowUtc</c>)
    /// ——对既有 T11 无效,因为 T11 预置的行从未被领取过,<c>LeaseExpiresAtUtc</c> 是 <c>null</c>,
    /// <c>NULL &lt; now</c> 三值逻辑为假,T11 照绿。该变异下本测试会转红(<c>CallCount==1</c>、
    /// <c>Fence==2</c>、<c>AttemptCount==2</c>、attempt 2 行)。
    /// </summary>
    [Fact]
    public async Task A_completed_execution_is_never_reclaimed_even_after_its_lease_expires()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var s = await StartAsync(f, db, engine, "n7");
        var execution = await BuildExecutionAsync(db, s);

        var handlerA = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "a-ok"), WfNodeType.Webhook);
        var dispatcherA = new WfNodeExecutionDispatcher(db, [handlerA], engine, TimeProvider.System);
        var statusA = await dispatcherA.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, statusA);

        var past = DateTime.UtcNow.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = past })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handlerB = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook);
        var dispatcherB = new WfNodeExecutionDispatcher(db, [handlerB], engine, TimeProvider.System);
        var statusB = await dispatcherB.RunAsync(execution.Id, "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, handlerB.CallCount);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Succeeded, reloaded.Status);
        Assert.Equal(1, reloaded.Fence);
        Assert.Equal(1, reloaded.AttemptCount);

        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(WfInstanceStatus.Approved, instance.Status);

        var instanceCompletedCount = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == s.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .CountAsync();
        Assert.Equal(1, instanceCompletedCount);

        // P2-1:挪到最后——放最前时变异态下第一条就抛,后面 7 条一次都没跑到,失败消息只说
        // "返回了 Cancelled",曾把 exec 与协调者双双引向"handler 没被重复调用"的错误结论。
        Assert.Null(statusB);
    }

    // ────────────────────────── 脚手架 ──────────────────────────

    private sealed record Scaffold(
        long InstanceId,
        WfToken Token,
        long DefinitionVersionId,
        long AssigneeUserId,
        long StarterUserId,
        long PreexistingTaskId);

    /// <summary>
    /// 发布一个「start → node1(approval,assignee=user:[assigneeId]) → null」的版本 → 经
    /// <see cref="IWorkflowEngine.ExecuteAsync"/> 直接发起(不经 HTTP,<c>StartInstanceCmd</c> 不需要
    /// <c>wf_definition</c> 行)→ 取活跃 token。node1 真实建了一件待办(<c>PreexistingTaskId</c>),
    /// 供 T7/T8 从 <c>ManualFallback</c> 新建的任务里区分出来。
    /// </summary>
    private static async Task<Scaffold> StartAsync(
        WorkflowAppFactory f, ISqlSugarClient db, IWorkflowEngine engine, string tag)
    {
        var admin = await ClientFor(f, "superAdmin");
        var starterId = await AddUser(admin, $"disp-{tag}-starter");
        var assigneeId = await AddUser(admin, $"disp-{tag}-assignee");

        var version = new WfDefinitionVersion
        {
            // 随机而非固定 0——同一测试方法内可能多次调用 StartAsync(如 T6 的两段),
            // uk_wf_definition_version 建在 (DefinitionId, Version) 上,固定值会在第二次插入时撞唯一索引。
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = WfModelJson.Serialize(BuildModel(assigneeId)),
        };
        await db.Insertable(version).ExecuteCommandAsync();

        var result = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = starterId,
        });
        Assert.NotNull(result.CreatedTaskId);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == result.InstanceId && t.Status == WfTokenStatus.Active)
            .FirstAsync();

        return new Scaffold(result.InstanceId, token, version.Id, assigneeId, starterId, result.CreatedTaskId!.Value);
    }

    /// <summary>造一行 <c>WfNodeExecution</c>——用真 token/node/version 的值算 <c>ExecutionKey</c>。</summary>
    private static async Task<WfNodeExecution> BuildExecutionAsync(
        ISqlSugarClient db, Scaffold s, int maxAttempts = 3, string nodeId = "node1")
    {
        var scopeKey = WfIdentityHash.NormalizeScopeKey(null);
        var key = WfExecutionKey.Compute(scopeKey, s.InstanceId, s.Token.Id, s.Token.NodeVisitId, nodeId, s.DefinitionVersionId);
        var row = new WfNodeExecution
        {
            ExecutionKey = key,
            ScopeKey = scopeKey,
            InstanceId = s.InstanceId,
            TokenId = s.Token.Id,
            NodeVisitId = s.Token.NodeVisitId,
            NodeId = nodeId,
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = s.DefinitionVersionId,
            MaxAttempts = maxAttempts,
        };
        return await WfNodeExecutionStore.EnsureAsync(db, row, CancellationToken.None);
    }

    /// <summary>照抄产品代码的领取姿势(事务内)。</summary>
    private static async Task<WfNodeExecution?> ClaimAsync(
        ISqlSugarClient db, long executionId, string owner, DateTime nowUtc, TimeSpan? leaseDuration = null)
    {
        var tran = await db.Ado.UseTranAsync(() => WfNodeExecutionStore.ClaimAsync(
            db, executionId, owner, nowUtc, leaseDuration ?? TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.True(tran.IsSuccess);
        return tran.Data;
    }

    /// <summary>start → node1(approval,mode=any);<paramref name="assigneeUserId"/> 为 <c>null</c> 时 node1 不配 <c>assignee</c>。</summary>
    private static WfModel BuildModel(long? assigneeUserId) => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "node1",
                Type = WfNodeType.Approval,
                Props = assigneeUserId is null
                    ? null
                    : new WfNodeProps
                    {
                        Assignee = new WfAssignee
                        {
                            Provider = ApproverProviderKeys.User,
                            Params = new Dictionary<string, JsonElement>
                            {
                                ["userIds"] = JsonSerializer.SerializeToElement(new[] { assigneeUserId.Value }),
                            },
                        },
                        Mode = WfApprovalMode.Any,
                    },
                Next = null,
            },
        },
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
