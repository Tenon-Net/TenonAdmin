using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_outbox</c> 的入队(M3a-1 Task 5)。<b>只暴露 <see cref="EnqueueAsync"/> 一个方法</b>——领取/回写/退避
/// 归消费者任务(<see cref="WfOutboxStatus"/> 的状态图)。<c>public</c> 而不是 <c>internal</c>——全仓无
/// <c>InternalsVisibleTo</c>,与 <see cref="WfNodeExecutionStore"/>/<see cref="WfNodeExecutionAttemptStore"/>
/// 同为 <c>public static</c>。<b>零 DI 注册</b>,调用方(Task 6 的回写短事务)直接经 <c>ISqlSugarClient</c>
/// 调用,<b>事务由调用方起</b>,本方法不自开事务。
/// <para><b>签名里没有 <c>messageKey</c> 形参是刻意的</b>——与 <see cref="WfNodeExecutionAttemptStore.AppendAsync"/>
/// 拿掉 <c>attemptNo</c> 形参同款手法:<see cref="WfOutbox.MessageKey"/> 由 <c>execution</c> 的
/// <see cref="WfNodeExecution.ExecutionKey"/> 与 <c>messageType</c> 在方法体内拼出,调用方没有
/// 机会传错一个不匹配的 key。</para>
/// <para><b>按 <see cref="WfOutbox.MessageKey"/> 幂等 ensure-insert</b>:先查,存在则原样返回既有行
/// (既有 payload 胜出),否则插入并返回。<b>唯一索引撞了原样抛出,不写 try/catch</b>——与
/// <see cref="WfNodeExecutionStore.EnsureAsync"/>/<see cref="WfNodeExecutionAttemptStore.AppendAsync"/> 同款
/// 理由:半吊子的 catch 在 PostgreSQL 上更糟(事务已 aborted,<c>25P02</c>);真正的「认赢家」恢复要
/// savepoint,归有并发创建方的那个任务。</para>
/// </summary>
public static class WfOutboxStore
{
    /// <summary>execution 执行完成通知(Task 6 首个使用方)。</summary>
    public const string MessageTypeNodeExecutionCompleted = "wf.node-execution.completed";

    /// <summary>
    /// 按 <c>{execution.ExecutionKey}:{messageType}</c> 幂等 ensure-insert 一行 <see cref="WfOutbox"/>。
    /// </summary>
    /// <param name="db">SqlSugar 客户端;事务由调用方起,本方法不自开事务。</param>
    /// <param name="execution"><see cref="WfOutbox.ExecutionId"/> 与 <see cref="WfOutbox.MessageKey"/> 前缀取自同一个对象,杜绝错配。</param>
    /// <param name="messageType">消息契约名;<c>Trim()</c> 后不得为空白,不得含 <c>':'</c>(key 分隔符)。</param>
    /// <param name="payloadJson">待投递正文全文,不截断。</param>
    /// <param name="nowUtc"><see cref="WfOutbox.AvailableAtUtc"/> 取值;不在方法体内读 <c>DateTime.UtcNow</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<WfOutbox> EnqueueAsync(
        ISqlSugarClient db,
        WfNodeExecution execution,
        string messageType,
        string? payloadJson,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedType = NormalizeMessageType(messageType);
        var messageKey = $"{execution.ExecutionKey}:{normalizedType}";

        var existing = await db.Queryable<WfOutbox>()
            .Where(o => o.MessageKey == messageKey)
            .FirstAsync();
        if (existing is not null) return existing;

        var row = new WfOutbox
        {
            ExecutionId = execution.Id,
            MessageType = normalizedType,
            MessageKey = messageKey,
            PayloadJson = payloadJson,
            AvailableAtUtc = nowUtc,
        };

        await db.Insertable(row).ExecuteCommandAsync(); // Id 由审计 AOP 填雪花
        return row;
    }

    private static string NormalizeMessageType(string messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        var trimmed = messageType.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("messageType 不得为空白。", nameof(messageType));
        if (trimmed.Contains(':'))
            throw new ArgumentException("messageType 不得含 ':'(它是 MessageKey 的分隔符)。", nameof(messageType));
        return trimmed;
    }
}
