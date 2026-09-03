using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 四库持久化契约(M3a-1 Task 9)——<c>wf_node_execution</c>/<c>wf_node_execution_attempt</c>/<c>wf_outbox</c>
/// 三张新表 + Task 1 给 <c>wf_history</c>/<c>wf_instance</c> 加的四个非空列,同一套用例经 <see cref="TestDb"/>
/// 在 CI 的 sqlite / mysql / postgres / sqlserver 四条腿上各跑一遍。姿势同 <see cref="WfPersistenceContractTests"/>
/// (M2c Task 8)。<b>本文件只写现有用例覆盖不到的东西</b>——<see cref="WfNodeExecutionClaimTests"/>/
/// <see cref="WfNodeExecutionAttemptTests"/>/<see cref="WfOutboxTests"/> 已经把 C# 侧业务逻辑覆盖过一遍,
/// 这里只钉「问数据库自己」的那部分:唯一索引/建表、列宽与编码、affected-rows 的四库语义、事务边界、
/// <c>*Utc</c> 精度、枚举数值语义、存量库升级。
/// <para><b>⚠ 射程声明(和 <see cref="WfPersistenceContractTests"/> 同款纪律,必须遵守)</b>:
/// ①列宽/中文/<c>CodeFirst_BigString</c> 三类断言(E2/E3/E4)在 <b>SQLite 腿是恒真断言</b>——SQLite 的类型
/// 亲和性不执行列宽也不区分 Unicode(M2c Round 24 实测:把 <c>Length</c> 改成 200 依旧全绿)。②E12 的截断
/// 防线只在 mysql(严格模式)/postgres/sqlserver 三腿具备判别力,SQLite 照单全收。③E8 的 PG 事务中止语义
/// (<c>25P02</c>)只在 postgres 腿能观察到,SQLite/MySQL/SqlServer 上加不加 savepoint 都是绿的。
/// ④E11 在 PG/SqlServer 上「有行的表 <c>ADD COLUMN NOT NULL</c>」的真实行为是<b>本条测试存在的全部理由</b>,
/// 但本机只有 SQLite,证不了。<b>`## DONE-CONDITION` 里的「四库」指四条 CI 腿都跑同一套用例,不是「本机就能
/// 证明四库都对」</b>——报告里凡涉及这几条断言,一律写「SQLite 腿绿,另三腿未跑」,不许写成「四库跑绿」。</para>
/// <para><b>本文件已进 <c>.github/workflows/backend-ci.yml</c> 的 sqlserver push/PR <c>TEST_FILTER</c> 白名单</b>
/// (与 <see cref="WfPersistenceContractTests"/> 并列,是名单里第二个 <c>Wf*</c> 类)——每加一条测试就给该腿
/// 多加一个隔离库(~20s/库),动手前想清楚这条测试是否真的问了数据库自己,而不是 C# 侧业务逻辑。</para>
/// </summary>
public class WfNodeExecutionContractTests
{
    // ────────────────────────── A. 唯一性与建表 ──────────────────────────

    /// <summary>
    /// E1 三张新表的唯一索引真被 CodeFirst 建了出来:<c>uk_wf_node_exec_key</c>(同 <c>ExecutionKey</c>)、
    /// <c>uk_wf_outbox_message_key</c>(同 <c>MessageKey</c>)、<c>uk_wf_node_exec_attempt_no</c>
    /// (同 <c>(ExecutionId, AttemptNo)</c>)。三次都在<b>自动提交</b>下撞,不构造真并发(T4:PG 上语句报错会
    /// 中止整事务,自动提交没有这个问题)。<b>不断异常类型</b>(T3:四库驱动异常类型各异)——只断「抛了」+
    /// 「库里只剩一行」,行数那句是关键:没有它,「索引压根没建出来但插入因别的原因失败」也能让测试绿。
    /// </summary>
    [Fact]
    public async Task The_three_execution_tables_enforce_their_unique_indexes()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        // ── wf_node_execution: uk_wf_node_exec_key ──
        var execKey = UniqueKey();
        await db.Insertable(NewExecution(execKey)).ExecuteCommandAsync();
        var execFailure = await CatchAsync(() => db.Insertable(NewExecution(execKey)).ExecuteCommandAsync());
        Assert.NotNull(execFailure);
        Assert.Equal(1, await db.Queryable<WfNodeExecution>().Where(e => e.ExecutionKey == execKey).CountAsync());

        // ── wf_outbox: uk_wf_outbox_message_key ──
        var executionA = NewExecution(UniqueKey());
        var executionB = NewExecution(UniqueKey());
        await db.Insertable(executionA).ExecuteCommandAsync();
        await db.Insertable(executionB).ExecuteCommandAsync();
        var sharedMessageKey = "e1-shared-message-key-" + Guid.NewGuid().ToString("N");
        await db.Insertable(NewOutbox(executionA.Id, sharedMessageKey)).ExecuteCommandAsync();
        var outboxFailure = await CatchAsync(() => db.Insertable(NewOutbox(executionB.Id, sharedMessageKey)).ExecuteCommandAsync());
        Assert.NotNull(outboxFailure);
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.MessageKey == sharedMessageKey).CountAsync());

        // ── wf_node_execution_attempt: uk_wf_node_exec_attempt_no ──
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();
        await db.Insertable(NewAttempt(execution.Id, 1)).ExecuteCommandAsync();
        var attemptFailure = await CatchAsync(() => db.Insertable(NewAttempt(execution.Id, 1)).ExecuteCommandAsync());
        Assert.NotNull(attemptFailure);
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == execution.Id && a.AttemptNo == 1).CountAsync());
    }

    /// <summary>
    /// E13 三张新表的每一列真被当前腿的 CodeFirst 建了出来(不查类型/宽度/可空性,那些由 E2/E3 从行为侧覆盖,
    /// R6)。钉的是「某方言下 T-SQL DDL 少建了一列」这类只在运行期以怪异错误现形的事故。姿势照
    /// <see cref="CodeFirstNullableUpgradeTests"/> 的 <c>GetColumnInfosByTableName</c>。
    /// </summary>
    [Fact]
    public async Task The_three_execution_tables_are_created_with_every_declared_column()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        AssertAllColumnsExist(db, "wf_node_execution", typeof(WfNodeExecution));
        AssertAllColumnsExist(db, "wf_node_execution_attempt", typeof(WfNodeExecutionAttempt));
        AssertAllColumnsExist(db, "wf_outbox", typeof(WfOutbox));
    }

    // ────────────────────────── B. 列宽与编码 ──────────────────────────

    /// <summary>
    /// E2 <c>wf_node_execution</c>/<c>wf_node_execution_attempt</c> 声明宽度的列写满宽 → 读回逐字相等,
    /// 可以放中文的列用中文填满并断不含 <c>?</c>(SqlServer 非 Unicode 列的经典症状,nightly #25)。
    /// <para><b>射程</b>:本条在 <b>SQLite 腿是恒真断言</b>(T6)——SQLite 的类型亲和性不执行列宽/编码约束。
    /// 真正的钉子在 mysql / postgres / sqlserver 三腿。</para>
    /// </summary>
    [Fact]
    public async Task Execution_and_attempt_columns_hold_their_declared_width_in_chinese()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        // ExecutionKey(64)/OutputHash(64) 是 hex,用 ASCII 填满;其余能放中文的列用中文填满。
        var executionKey = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..64];
        var scopeKey = new string('域', 64);
        var nodeId = new string('节', 64);
        var leaseOwner = new string('工', 128);
        var handlerType = new string('理', 256);
        var summary = new string('要', 512);
        var outputHash = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..64];

        var row = NewExecution(executionKey);
        row.ScopeKey = scopeKey;
        row.NodeId = nodeId;
        await db.Insertable(row).ExecuteCommandAsync();

        // LeaseOwner 走真实领取路径(128 宽的 owner 标识)。
        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, row.Id, leaseOwner, DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        // HandlerType/Summary/OutputHash 本轮的唯一写入点是引擎回写短事务(WorkflowEngine.ClaimExecutionWritebackAsync),
        // 本文件不经引擎——裸 SetColumns 直接复刻那三列的宽度契约,先落局部变量(T1:zh-CN 下内联常量会被区域格式化)。
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { HandlerType = handlerType, Summary = summary, OutputHash = outputHash })
            .Where(e => e.Id == row.Id)
            .ExecuteCommandAsync();

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == row.Id).FirstAsync();
        Assert.Equal(executionKey, loaded.ExecutionKey);
        Assert.Equal(scopeKey, loaded.ScopeKey);
        Assert.Equal(nodeId, loaded.NodeId);
        Assert.Equal(leaseOwner, loaded.LeaseOwner);
        Assert.Equal(handlerType, loaded.HandlerType);
        Assert.Equal(summary, loaded.Summary);
        Assert.Equal(outputHash, loaded.OutputHash);
        Assert.DoesNotContain('?', loaded.ScopeKey);
        Assert.DoesNotContain('?', loaded.NodeId);
        Assert.DoesNotContain('?', loaded.LeaseOwner!);
        Assert.DoesNotContain('?', loaded.HandlerType!);
        Assert.DoesNotContain('?', loaded.Summary!);

        // attempt 的 OutputSummary/ErrorSummary(512,SummaryMaxLength)与 OutputHash(64,SHA-256 hex 天然定长)。
        const string succeededOutputJson = "{\"a\":1}";
        var expectedSucceededOutputSummary = new string('结', 512);
        var succeededAttempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, claimed, WfNodeExecutionResult.Succeeded(outputJson: succeededOutputJson, summary: expectedSucceededOutputSummary),
            DateTime.UtcNow, DateTime.UtcNow, CancellationToken.None);
        var expectedSucceededOutputHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(succeededOutputJson)));
        var loadedSucceededAttempt = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.Id == succeededAttempt.Id).FirstAsync();
        Assert.Equal(512, loadedSucceededAttempt.OutputSummary!.Length);
        Assert.Equal(expectedSucceededOutputSummary, loadedSucceededAttempt.OutputSummary);
        Assert.Equal(expectedSucceededOutputHash, loadedSucceededAttempt.OutputHash);
        Assert.DoesNotContain('?', loadedSucceededAttempt.OutputSummary);

        var failedExecution = NewExecution(UniqueKey());
        await db.Insertable(failedExecution).ExecuteCommandAsync();
        var failedClaim = await WfNodeExecutionStore.ClaimAsync(
            db, failedExecution.Id, "worker-e2", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(failedClaim);
        var expectedFailedErrorSummary = new string('错', 512);
        var failedAttempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, failedClaim, WfNodeExecutionResult.RetryableFailure(errorCode: 48001, summary: expectedFailedErrorSummary),
            DateTime.UtcNow, DateTime.UtcNow, CancellationToken.None);
        var loadedFailedAttempt = await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.Id == failedAttempt.Id).FirstAsync();
        Assert.Equal(512, loadedFailedAttempt.ErrorSummary!.Length);
        Assert.Equal(expectedFailedErrorSummary, loadedFailedAttempt.ErrorSummary);
        Assert.Null(loadedFailedAttempt.OutputHash);
        Assert.DoesNotContain('?', loadedFailedAttempt.ErrorSummary);
    }

    /// <summary>
    /// E3 <c>wf_outbox</c> 的 <c>MessageType</c>(64)/<c>MessageKey</c>(128)/<c>LastError</c>(512)写满宽,
    /// 中文不变 <c>?</c>。<c>MessageKey</c> 直插(绕开 <see cref="WfOutboxStore.EnqueueAsync"/>,因为它按
    /// <c>{ExecutionKey}:{MessageType}</c> 派生 key,派生值不受本条控制)。
    /// <para><b>射程</b>:同 E2,本条在 <b>SQLite 腿是恒真断言</b>(T6)。</para>
    /// </summary>
    [Fact]
    public async Task Outbox_columns_hold_their_declared_width_in_chinese()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var messageType = new string('型', 64);
        var messageKey = new string('键', 128);
        var lastError = new string('误', 512);

        var row = new WfOutbox
        {
            ExecutionId = execution.Id,
            MessageType = messageType,
            MessageKey = messageKey,
            AvailableAtUtc = DateTime.UtcNow,
        };
        await db.Insertable(row).ExecuteCommandAsync();

        // LastError 本轮零写入点(消费者任务归属),裸 SetColumns 复刻它的宽度契约,先落局部变量(T1)。
        await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox { LastError = lastError })
            .Where(o => o.Id == row.Id)
            .ExecuteCommandAsync();

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(messageType, loaded.MessageType);
        Assert.Equal(messageKey, loaded.MessageKey);
        Assert.Equal(lastError, loaded.LastError);
        Assert.DoesNotContain('?', loaded.MessageType);
        Assert.DoesNotContain('?', loaded.MessageKey);
        Assert.DoesNotContain('?', loaded.LastError!);
    }

    /// <summary>
    /// E4 <c>PayloadJson</c>(<see cref="StaticConfig.CodeFirst_BigString"/>)长文本 + 中文原样往返
    /// (Task 5 R5 挂账)。<b>禁止</b>改成裸 <c>ColumnDataType = "text"</c>——SqlServer 上非 Unicode,
    /// 中文读回变 <c>???</c>(T13,nightly #25 先例)。
    /// <para><b>射程</b>:同 E2/E3,本条在 <b>SQLite 腿是恒真断言</b>(T6)。</para>
    /// </summary>
    [Fact]
    public async Task Outbox_payload_round_trips_chinese_and_long_bodies()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var payload = $$"""{"备注":"{{string.Concat(Enumerable.Repeat("节点执行完成通知载荷中文正文", 900))}}"}""";
        Assert.True(payload.Length > 8000, "载荷不够长就钉不住 CodeFirst_BigString 列。");

        var enqueued = await WfOutboxStore.EnqueueAsync(
            db, execution, WfOutboxStore.MessageTypeNodeExecutionCompleted, payload, DateTime.UtcNow, CancellationToken.None);

        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == enqueued.Id).FirstAsync();
        Assert.Equal(payload, loaded.PayloadJson);
        Assert.DoesNotContain('?', loaded.PayloadJson!);
    }

    /// <summary>
    /// E12 超长(600 字中文)摘要在到达数据库<b>之前</b>已被 C# 侧截断到 512(结 Task 4 挂给本 Task 的
    /// 「截断在真会抛的库上的效果」)。
    /// <para><b>射程</b>:本条在 <b>SQLite 腿不具判别力</b>(T8)——去掉 <see cref="WfNodeExecutionAttemptStore.Truncate"/>
    /// 后 SQLite 照单全收,只有 mysql(严格模式)/postgres/sqlserver 三腿会红。</para>
    /// </summary>
    [Fact]
    public async Task A_summary_longer_than_the_column_is_truncated_before_it_reaches_the_database()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();
        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-e12", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        var overlong = new string('长', 600);
        var attempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, claimed, WfNodeExecutionResult.Succeeded(summary: overlong), DateTime.UtcNow, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(512, attempt.OutputSummary!.Length);
        Assert.Equal(overlong[..512], attempt.OutputSummary);

        var loaded = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.Id == attempt.Id).FirstAsync();
        Assert.Equal(512, loaded.OutputSummary!.Length);
        Assert.Equal(overlong[..512], loaded.OutputSummary);
    }

    // ────────────────────────── C. CAS 与 affected-rows ──────────────────────────

    /// <summary>
    /// E5 <see cref="WfNodeExecutionStore.ClaimAsync"/> 的条件 UPDATE 在四库上报告一致的 affected-rows,
    /// 外加一个 matched-vs-changed 探针(MySQL 的 <c>UseAffectedRows</c> 分歧口径,D5)。
    /// 时间边界用<b>整分钟</b>(T2:MySQL 裸 <c>DateTime</c> 秒精度截断,毫秒级边界会在 mysql 腿翻)。
    /// </summary>
    [Fact]
    public async Task The_claim_update_reports_the_same_affected_rows_on_every_dialect()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();
        var now = DateTime.UtcNow;

        // ① 命中 → 非 null
        var first = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-a", now, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(first);

        // ② 租约窗口内再领 → null,nowUtc 用整分钟推进(T2)
        var stillLeased = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-b", now.AddMinutes(1), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(stillLeased);

        // ③ 租约过期后再领 → 非 null 且 Fence 递增
        var expiredAt = now.AddMinutes(-1);
        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = expiredAt })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();
        var reclaimAt = now.AddMinutes(6);
        var reclaimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-c", reclaimAt, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(reclaimed);
        Assert.Equal(2, reclaimed.Fence);

        // ④ matched-vs-changed 探针:把列设成它当前已有的值(值不变),affected 必须仍是 1(matched 口径)。
        var sameOwner = reclaimed.LeaseOwner;
        var affected = await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseOwner = sameOwner })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();
        Assert.Equal(1, affected);
    }

    /// <summary>
    /// E6 复刻 <c>WorkflowEngine.ClaimExecutionWritebackAsync</c>(<c>WorkflowEngine.cs:1370</c>)的双谓词
    /// <c>Fence == fence &amp;&amp; Status == Running</c>,裸 <c>Updateable</c>,不经引擎——三条各自证明双谓词里
    /// 的一个谓词真在起作用。
    /// </summary>
    [Fact]
    public async Task The_fence_writeback_cas_reports_the_same_affected_rows_on_every_dialect()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        async Task<WfNodeExecution> ClaimedRow()
        {
            var execution = NewExecution(UniqueKey());
            await db.Insertable(execution).ExecuteCommandAsync();
            var claimed = await WfNodeExecutionStore.ClaimAsync(
                db, execution.Id, "worker-e6", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
            Assert.NotNull(claimed);
            return claimed;
        }

        async Task<int> WritebackCas(long executionId, long fence)
        {
            var succeeded = WfNodeExecutionStatus.Succeeded;
            var running = WfNodeExecutionStatus.Running;
            return await db.Updateable<WfNodeExecution>()
                .SetColumns(e => new WfNodeExecution { Status = succeeded })
                .Where(e => e.Id == executionId && e.Fence == fence && e.Status == running)
                .ExecuteCommandAsync();
        }

        // ① 同 fence + Running → 1
        var rowA = await ClaimedRow();
        Assert.Equal(1, await WritebackCas(rowA.Id, rowA.Fence));

        // ② 陈旧 fence(fence - 1)→ 0
        var rowB = await ClaimedRow();
        Assert.Equal(0, await WritebackCas(rowB.Id, rowB.Fence - 1));

        // ③ 同 fence 但行已被改成非 Running → 0(先用正确 fence 把它推成终态,再用同一个 fence 重放)
        var rowC = await ClaimedRow();
        Assert.Equal(1, await WritebackCas(rowC.Id, rowC.Fence));
        Assert.Equal(0, await WritebackCas(rowC.Id, rowC.Fence));
    }

    // ────────────────────────── D. 事务边界 ──────────────────────────

    /// <summary>
    /// E7 一个短事务里依次做 execution 的 fence CAS 回写 + attempt 插入 + outbox 入队,再强制回滚——
    /// 断三张表在事务外一行都没留(「不残留半推进状态」的字面契约,现有三条各自只回滚一张表)。
    /// </summary>
    [Fact]
    public async Task A_rolled_back_writeback_leaves_no_half_advanced_state_in_any_of_the_three_tables()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();
        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-e7", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        var inTransactionAttemptCount = -1;
        var inTransactionOutboxCount = -1;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var succeeded = WfNodeExecutionStatus.Succeeded;
            var running = WfNodeExecutionStatus.Running;
            var affected = await db.Updateable<WfNodeExecution>()
                .SetColumns(e => new WfNodeExecution { Status = succeeded })
                .Where(e => e.Id == claimed.Id && e.Fence == claimed.Fence && e.Status == running)
                .ExecuteCommandAsync();
            Assert.Equal(1, affected);

            var now = DateTime.UtcNow;
            await WfNodeExecutionAttemptStore.AppendAsync(
                db, claimed, WfNodeExecutionResult.Succeeded(summary: "e7"), now, now, CancellationToken.None);
            await WfOutboxStore.EnqueueAsync(
                db, claimed, WfOutboxStore.MessageTypeNodeExecutionCompleted, "{}", now, CancellationToken.None);

            inTransactionAttemptCount = await db.Queryable<WfNodeExecutionAttempt>()
                .Where(a => a.ExecutionId == claimed.Id).CountAsync();
            inTransactionOutboxCount = await db.Queryable<WfOutbox>()
                .Where(o => o.ExecutionId == claimed.Id).CountAsync();
            Assert.Equal(1, inTransactionAttemptCount);
            Assert.Equal(1, inTransactionOutboxCount);

            throw new InvalidOperationException("强制回滚,验证三表同事务不残留半推进状态。");
        });

        Assert.False(tran.IsSuccess);
        Assert.Equal(1, inTransactionAttemptCount);
        Assert.Equal(1, inTransactionOutboxCount);

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == claimed.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, loaded.Status);
        Assert.Equal(claimed.AttemptCount, loaded.AttemptCount);
        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == claimed.Id).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == claimed.Id).CountAsync());
    }

    /// <summary>
    /// E8 事务内先成功回写 execution,再插一行撞唯一键的 attempt(三个 store 都不 catch,见类注释)→
    /// 整个事务回滚,execution 的状态也回到回写前的值。PG 专项:那里事务是被 <c>25P02</c> 中止的,其余三库是
    /// 我们主动回滚的,可观测结果必须一致。<b>冲突后绝不在同一事务内继续查库</b>(T4)——所有断言都在
    /// <c>UseTranAsync</c> 之外。
    /// </summary>
    [Fact]
    public async Task A_unique_conflict_inside_the_transaction_rolls_the_whole_writeback_back()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();
        var claimed = await WfNodeExecutionStore.ClaimAsync(
            db, execution.Id, "worker-e8", DateTime.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimed);

        // 预先占住 AttemptNo = 1(= claimed.AttemptCount),事务内 AppendAsync 必撞唯一键。
        await db.Insertable(NewAttempt(claimed.Id, claimed.AttemptCount)).ExecuteCommandAsync();

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var succeeded = WfNodeExecutionStatus.Succeeded;
            var running = WfNodeExecutionStatus.Running;
            var affected = await db.Updateable<WfNodeExecution>()
                .SetColumns(e => new WfNodeExecution { Status = succeeded })
                .Where(e => e.Id == claimed.Id && e.Fence == claimed.Fence && e.Status == running)
                .ExecuteCommandAsync();
            Assert.Equal(1, affected);

            var now = DateTime.UtcNow;
            await WfNodeExecutionAttemptStore.AppendAsync(
                db, claimed, WfNodeExecutionResult.Succeeded(summary: "e8"), now, now, CancellationToken.None);
        });

        Assert.False(tran.IsSuccess);
        Assert.NotNull(tran.ErrorException);

        var loaded = await db.Queryable<WfNodeExecution>().Where(e => e.Id == claimed.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, loaded.Status);
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == claimed.Id && a.AttemptNo == claimed.AttemptCount).CountAsync());
    }

    // ────────────────────────── E. 时间与枚举 ──────────────────────────

    /// <summary>
    /// E9 三张表的全部 <c>*Utc</c> 列写入一个<b>整秒</b>的已知 UTC 值,读回不移位(<c>TimeSpan.FromSeconds(1)</c>
    /// 容差,T2)。<b>不断 <c>Kind</c></b>(T9:SqlSugar 读回一律 <c>Unspecified</c>)。钉的是「某腿把列建成带
    /// 时区类型 / 连接时区把值移走」这类事故,不碰 <c>CreateTime</c>/<c>UpdateTime</c>(local,不得与 <c>*Utc</c>
    /// 列比较)。
    /// </summary>
    [Fact]
    public async Task Utc_columns_round_trip_without_a_timezone_shift()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var expected = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Utc);

        var execution = NewExecution(UniqueKey());
        execution.NextRetryAtUtc = expected;
        execution.DeadlineAtUtc = expected;
        execution.LeaseExpiresAtUtc = expected;
        execution.CompletedTimeUtc = expected;
        await db.Insertable(execution).ExecuteCommandAsync();

        var attempt = NewAttempt(execution.Id, 1);
        attempt.StartedAtUtc = expected;
        attempt.EndedAtUtc = expected;
        await db.Insertable(attempt).ExecuteCommandAsync();

        var outbox = new WfOutbox
        {
            ExecutionId = execution.Id,
            MessageType = "wf.e9",
            MessageKey = UniqueKey(),
            AvailableAtUtc = expected,
        };
        await db.Insertable(outbox).ExecuteCommandAsync();

        var loadedExecution = await db.Queryable<WfNodeExecution>().Where(e => e.Id == execution.Id).FirstAsync();
        var tolerance = TimeSpan.FromSeconds(1);
        Assert.Equal(expected, loadedExecution.NextRetryAtUtc!.Value, tolerance);
        Assert.Equal(expected, loadedExecution.DeadlineAtUtc!.Value, tolerance);
        Assert.Equal(expected, loadedExecution.LeaseExpiresAtUtc!.Value, tolerance);
        Assert.Equal(expected, loadedExecution.CompletedTimeUtc!.Value, tolerance);

        var loadedAttempt = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.Id == attempt.Id).FirstAsync();
        Assert.Equal(expected, loadedAttempt.StartedAtUtc, tolerance);
        Assert.Equal(expected, loadedAttempt.EndedAtUtc, tolerance);

        var loadedOutbox = await db.Queryable<WfOutbox>().Where(o => o.Id == outbox.Id).FirstAsync();
        Assert.Equal(expected, loadedOutbox.AvailableAtUtc, tolerance);
    }

    /// <summary>
    /// E10 三个持久化枚举(<see cref="WfNodeExecutionStatus"/>/<see cref="WfNodeExecutionResultType"/>/
    /// <see cref="WfOutboxStatus"/>)以数值语义存储:插入已知成员后用<b>数据库侧谓词</b>命中,再用一个不该
    /// 命中的成员断 0。<b>鉴别力如实说明</b>(R5):证明列以数值语义参与 SQL 比较、值与 C# 侧一致,
    /// <b>不证明列的物理类型</b>——C# 侧数值本身已由 <c>WfNodeHandlerContractTests</c> 钉住。
    /// </summary>
    [Fact]
    public async Task Persisted_enums_keep_their_declared_numeric_values()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        var execution = NewExecution(UniqueKey());
        execution.Status = WfNodeExecutionStatus.ManualFallback;
        await db.Insertable(execution).ExecuteCommandAsync();
        Assert.Equal(1, await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == execution.Id && e.Status == WfNodeExecutionStatus.ManualFallback).CountAsync());
        Assert.Equal(0, await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == execution.Id && e.Status == WfNodeExecutionStatus.Succeeded).CountAsync());

        var attempt = NewAttempt(execution.Id, 1);
        attempt.ResultType = WfNodeExecutionResultType.ManualFallback;
        await db.Insertable(attempt).ExecuteCommandAsync();
        Assert.Equal(1, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.Id == attempt.Id && a.ResultType == WfNodeExecutionResultType.ManualFallback).CountAsync());
        Assert.Equal(0, await db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.Id == attempt.Id && a.ResultType == WfNodeExecutionResultType.Succeeded).CountAsync());

        var outbox = new WfOutbox
        {
            ExecutionId = execution.Id,
            MessageType = "wf.e10",
            MessageKey = UniqueKey(),
            AvailableAtUtc = DateTime.UtcNow,
            Status = WfOutboxStatus.Failed,
        };
        await db.Insertable(outbox).ExecuteCommandAsync();
        Assert.Equal(1, await db.Queryable<WfOutbox>()
            .Where(o => o.Id == outbox.Id && o.Status == WfOutboxStatus.Failed).CountAsync());
        Assert.Equal(0, await db.Queryable<WfOutbox>()
            .Where(o => o.Id == outbox.Id && o.Status == WfOutboxStatus.Dispatched).CountAsync());
    }

    // ────────────────────────── F. 存量库升级 ──────────────────────────

    /// <summary>
    /// E11 结 Task 1 的 R2:同一宿主内,①先插有数据的 <c>wf_instance</c>/<c>wf_history</c> 行,②
    /// <c>DropColumn</c> 砍掉 Task 1 加的四个非空列(退化成「M3a-1 上线前的老库」),③紧接着
    /// <c>InitTables</c> 补列(中间不插任何查询——T14:实体属性还在,砍列后任何 <c>Queryable</c> 都会失败),
    /// ④断四列都回来了,且<b>旧行</b>读到 <c>DefaultValue</c> 对应的值。<b>这是本 Task 唯一真正的新方言面</b>:
    /// CI 四条腿全是空库,<c>CREATE TABLE</c> 路径根本不读 <c>DefaultValue</c>;「有行的表上
    /// <c>ADD COLUMN NOT NULL</c>」这条路今天四条腿一条都走不到。<c>WorkflowAppFactory.DbPath</c> 是只读属性
    /// (T10),故不能像 <see cref="CodeFirstNullableUpgradeTests"/> 那样起第二个宿主,改用同宿主内直调。
    /// </summary>
    [Fact]
    public async Task Legacy_rows_survive_re_adding_the_not_null_history_columns()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;

        // 1) 直插一行 WfInstance + 一行 WfHistory(表已有数据)。WfInstance 是 DataEntity/IOrgScoped——
        // 全局数据范围过滤器只作用于 SELECT,插入不受影响。
        var instance = new WfInstance
        {
            DefinitionVersionId = 1L,
            StarterUserId = 1L,
        };
        await db.Insertable(instance).ExecuteCommandAsync();
        var history = new WfHistory
        {
            InstanceId = instance.Id,
            EventType = WfHistoryEventType.InstanceStarted,
        };
        await db.Insertable(history).ExecuteCommandAsync();

        // 2) 砍掉 Task 1 加的四个非空列,退化成升级前的老库。
        db.DbMaintenance.DropColumn("wf_history", "Sequence");
        db.DbMaintenance.DropColumn("wf_history", "ActorType");
        db.DbMaintenance.DropColumn("wf_history", "PayloadVersion");
        db.DbMaintenance.DropColumn("wf_instance", "HistorySeq");

        // 3) 紧接着补列——中间不插任何查询(T14)。
        db.CodeFirst.InitTables(typeof(WfHistory), typeof(WfInstance));

        // 4) 四列都回来了。
        var historyCols = db.DbMaintenance.GetColumnInfosByTableName("wf_history", false)
            .Select(c => c.DbColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Sequence", historyCols);
        Assert.Contains("ActorType", historyCols);
        Assert.Contains("PayloadVersion", historyCols);
        var instanceCols = db.DbMaintenance.GetColumnInfosByTableName("wf_instance", false)
            .Select(c => c.DbColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("HistorySeq", instanceCols);

        // 旧行读到 DefaultValue 对应的值:Sequence=0、ActorType=Unknown(0)。
        var loadedHistory = await db.Queryable<WfHistory>().Where(h => h.Id == history.Id).FirstAsync();
        Assert.Equal(0, loadedHistory.Sequence);
        Assert.Equal(WfHistoryActorType.Unknown, loadedHistory.ActorType);

        // PayloadVersion(DefaultValue="1")是全仓唯一一个 DefaultValue != "0" 的列(grep 核实)——Sequence/
        // ActorType/HistorySeq 的 DefaultValue 恰好都是 "0",没法把「backfill 真写了 DefaultValue」与
        // 「backfill 没写、列直接读到 CLR/SQL 默认值 0」这两种可能区分开,PayloadVersion 是本仓第一处能
        // 拆开这两种解释的探针。★ 实测(本机 SQLite):旧行读到 <b>0</b>,不是声明的 <c>DefaultValue="1"</c>
        // ——与 plan 阶段基于 <c>WfInstance.Version</c> 类注释的假设(旧行读到 1)不一致,是主动申报的偏离。
        // <c>WfInstance.Version</c> 那段反编译注释本就写着「SQLite 例外……回填 UPDATE 照旧执行 → 旧行仍然
        // 读到 0」,只是 Version 的 DefaultValue 恰好也是 "0" 而从未被人注意到这句话对非零 DefaultValue
        // 意味着什么。mysql/postgres/sqlserver 三腿是否真的遵循 DefaultValue="1" 回填,本机没有这三种数据库,
        // 未验证——这里钉住当前已观测到的跨方言诊断探针结果,不是永久业务语义;
        // SQLite 读到 0 的事实不能用松散边界掩盖,其余方言仍交给 Task 10 在真机核实。
        Assert.Equal(0, loadedHistory.PayloadVersion);

        var loadedInstance = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instance.Id).FirstAsync();
        Assert.Equal(0, loadedInstance.HistorySeq);
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

    private static WfNodeExecutionAttempt NewAttempt(long executionId, int attemptNo) => new()
    {
        ExecutionId = executionId,
        AttemptNo = attemptNo,
        StartedAtUtc = DateTime.UtcNow,
        EndedAtUtc = DateTime.UtcNow,
        ResultType = WfNodeExecutionResultType.Succeeded,
        OutputSummary = "ok",
    };

    private static WfOutbox NewOutbox(long executionId, string messageKey) => new()
    {
        ExecutionId = executionId,
        MessageType = "wf.node-execution.completed",
        MessageKey = messageKey,
        AvailableAtUtc = DateTime.UtcNow,
    };

    private static string UniqueKey() => Guid.NewGuid().ToString("N");

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void AssertAllColumnsExist(ISqlSugarClient db, string tableName, Type entityType)
    {
        var actual = db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
            .Select(c => c.DbColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in entityType.GetProperties())
        {
            Assert.Contains(prop.Name, actual);
        }
    }

    /// <summary>宿主起来 + 建表;返回作用域与 SqlSugar 单例(不经引擎)。</summary>
    private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient(); // 触发宿主启动与 CodeFirst 建表
        var scope = f.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }
}
