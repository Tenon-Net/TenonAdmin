using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfNodeExecutionDispatcher"/> 的 12 条契约测试(M3a-1 Task 6)——「领取 → 调 handler → 落结果」,
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

        var ex = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(cmd));
        Assert.Equal(48004, (int)ex.Code);

        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var tokenAfterSecond = await db.Queryable<WfToken>().Where(t => t.Id == s.Token.Id).FirstAsync();
        Assert.Equal(tokenAfterFirst.NodeId, tokenAfterSecond.NodeId);
        Assert.Equal(tokenAfterFirst.NodeVisitId, tokenAfterSecond.NodeVisitId);

        var instanceAfterSecond = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>().Where(i => i.Id == s.InstanceId).FirstAsync();
        Assert.Equal(instanceAfterFirst.Status, instanceAfterSecond.Status);
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

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.Failed, status);

        var reloaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Failed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedTimeUtc);
        Assert.Equal(48002, reloaded.ErrorCode);

        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    /// <summary>
    /// 退避上下界钳制。两段各自独立的 execution(避免同 key 撞成同一行):(a) <c>RetryAfter = 0</c> → 忽略,
    /// 退到 30s 基线(<c>AttemptCount = 1</c> → <c>30 &lt;&lt; 0</c>);(b) <c>RetryAfter = 3650 天</c> → 钳到 24h。
    /// 变异:<c>ResolveRetryDelay</c> 首行退化成 <c>return result.RetryAfter ?? 默认;</c>(去掉钳制)。
    /// </summary>
    [Fact]
    public async Task Handler_supplied_retry_delay_is_clamped_at_both_ends()
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
        Assert.Equal(WorkflowErrorCode.NodeTypeUnsupported, attempt.ErrorCode);
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

        var handler1 = new FakeNodeHandler(WfNodeExecutionResult.RetryableFailure(summary: "first"), WfNodeType.Webhook);
        var dispatcher1 = new WfNodeExecutionDispatcher(db, [handler1], engine, TimeProvider.System);
        var status1 = await dispatcher1.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, status1);

        // 直接把重试时刻推到过去,不建 FakeTimeProvider(语义契约 Task 3 定案原文的手法)。
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { NextRetryAtUtc = DateTime.UtcNow.AddMinutes(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        var handler2 = new FakeNodeHandler(WfNodeExecutionResult.Succeeded(summary: "second"), WfNodeType.Webhook);
        var dispatcher2 = new WfNodeExecutionDispatcher(db, [handler2], engine, TimeProvider.System);
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
