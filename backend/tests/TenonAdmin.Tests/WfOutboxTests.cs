using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <c>wf_outbox</c> 的入队契约测试(M3a-1 Task 5)。用 <see cref="WorkflowAppFactory"/> 从
/// <c>ISqlSugarClient</c> 直接读写,姿势同 <see cref="WfNodeExecutionAttemptTests"/>——不经引擎,本 Task
/// 交付的是存储层,回写短事务与消费逻辑归 Task 6/后续任务。
/// </summary>
public class WfOutboxTests
{
    /// <summary>#1 入队后行立即 Pending 且可投,其余列取初值。</summary>
    [Fact]
    public async Task Enqueued_row_starts_pending_and_immediately_available()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var nowUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var enqueued = await WfOutboxStore.EnqueueAsync(
            db, execution, WfOutboxStore.MessageTypeNodeExecutionCompleted, "{\"ok\":true}", nowUtc, CancellationToken.None);

        var row = await db.Queryable<WfOutbox>().Where(o => o.Id == enqueued.Id).FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(WfOutboxStatus.Pending, row!.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(nowUtc, row.AvailableAtUtc);
        Assert.Null(row.CompletedAtUtc);
        Assert.Null(row.LastError);
        Assert.Equal(execution.Id, row.ExecutionId);
        Assert.Equal(WfOutboxStore.MessageTypeNodeExecutionCompleted, row.MessageType);
    }

    /// <summary>#2 <c>MessageKey</c> = <c>{ExecutionKey}:{MessageType}</c>;换一个 messageType 产出新行。</summary>
    [Fact]
    public async Task Message_key_is_the_execution_key_joined_with_the_message_type()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var nowUtc = DateTime.UtcNow;
        const string messageType = "wf.node-execution.completed";
        var first = await WfOutboxStore.EnqueueAsync(db, execution, messageType, null, nowUtc, CancellationToken.None);

        Assert.Equal(execution.ExecutionKey + ":" + messageType, first.MessageKey);

        const string otherType = "wf.node-execution.webhook-sent";
        var second = await WfOutboxStore.EnqueueAsync(db, execution, otherType, null, nowUtc, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
    }

    /// <summary>#3 同 execution + 同 messageType 入队两次,幂等——第二次返回既有行,payload 是第一次那份。</summary>
    [Fact]
    public async Task Enqueue_is_idempotent_by_message_key()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var nowUtc = DateTime.UtcNow;
        const string messageType = "wf.node-execution.completed";
        var first = await WfOutboxStore.EnqueueAsync(db, execution, messageType, "{\"v\":1}", nowUtc, CancellationToken.None);
        var second = await WfOutboxStore.EnqueueAsync(db, execution, messageType, "{\"v\":2}", nowUtc, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());

        var row = await db.Queryable<WfOutbox>().Where(o => o.Id == first.Id).FirstAsync();
        Assert.Equal("{\"v\":1}", row!.PayloadJson);
    }

    /// <summary>#4 唯一索引 <c>MessageKey</c> 真的挡住重复(绕过 store 直接插入)。</summary>
    [Fact]
    public async Task Duplicate_message_key_is_rejected_by_the_unique_index()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var executionA = NewExecution(UniqueKey());
        var executionB = NewExecution(UniqueKey());
        await db.Insertable(executionA).ExecuteCommandAsync();
        await db.Insertable(executionB).ExecuteCommandAsync();

        const string sharedKey = "shared-key-for-uniqueness-test";
        var nowUtc = DateTime.UtcNow;
        await db.Insertable(NewOutbox(executionA.Id, sharedKey, nowUtc)).ExecuteCommandAsync();

        Exception? failure = null;
        try
        {
            await db.Insertable(NewOutbox(executionB.Id, sharedKey, nowUtc)).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.Equal(1, await db.Queryable<WfOutbox>().Where(o => o.MessageKey == sharedKey).CountAsync());
    }

    /// <summary>#5 含中文的长正文原样往返(不截断)。</summary>
    [Fact]
    public async Task Payload_body_survives_a_round_trip_intact()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var payload = "{\"备注\":\"" + new string('审', 16_000) + "\"}"; // 约 32KB,含中文
        var enqueued = await WfOutboxStore.EnqueueAsync(
            db, execution, "wf.node-execution.completed", payload, DateTime.UtcNow, CancellationToken.None);

        var row = await db.Queryable<WfOutbox>().Where(o => o.Id == enqueued.Id).FirstAsync();
        Assert.Equal(payload, row!.PayloadJson);
    }

    /// <summary>#6 <c>EnqueueAsync</c> 处在被回滚的事务里 → 一行不留(store 不自开事务)。</summary>
    [Fact]
    public async Task An_enqueue_inside_a_rolled_back_transaction_leaves_no_trace()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var execution = NewExecution(UniqueKey());
        await db.Insertable(execution).ExecuteCommandAsync();

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            await WfOutboxStore.EnqueueAsync(
                db, execution, "wf.node-execution.completed", "{}", DateTime.UtcNow, CancellationToken.None);
            throw new InvalidOperationException("强制回滚,验证入队不留痕。");
        });

        Assert.False(tran.IsSuccess);

        Assert.Equal(0, await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == execution.Id)
            .CountAsync());
    }

    /// <summary>#7 两个不同 execution 用同一 messageType 入队 → 各自一行,MessageKey 互不相同。</summary>
    [Fact]
    public async Task Two_executions_can_enqueue_the_same_message_type()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var executionA = NewExecution(UniqueKey());
        var executionB = NewExecution(UniqueKey());
        await db.Insertable(executionA).ExecuteCommandAsync();
        await db.Insertable(executionB).ExecuteCommandAsync();

        const string messageType = "wf.node-execution.completed";
        var nowUtc = DateTime.UtcNow;
        var rowA = await WfOutboxStore.EnqueueAsync(db, executionA, messageType, null, nowUtc, CancellationToken.None);
        var rowB = await WfOutboxStore.EnqueueAsync(db, executionB, messageType, null, nowUtc, CancellationToken.None);

        Assert.NotEqual(rowA.MessageKey, rowB.MessageKey);
        Assert.Equal(executionA.ExecutionKey + ":" + messageType, rowA.MessageKey);
        Assert.Equal(executionB.ExecutionKey + ":" + messageType, rowB.MessageKey);

        var rows = await db.Queryable<WfOutbox>()
            .Where(o => o.ExecutionId == executionA.Id || o.ExecutionId == executionB.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ExecutionId == executionA.Id);
        Assert.Contains(rows, r => r.ExecutionId == executionB.Id);
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

    private static WfOutbox NewOutbox(long executionId, string messageKey, DateTime nowUtc) => new()
    {
        ExecutionId = executionId,
        MessageType = "wf.node-execution.completed",
        MessageKey = messageKey,
        AvailableAtUtc = nowUtc,
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
