using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_outbox</c> 的领取与回写(Task 8c)。入队仍只走 <see cref="WfOutboxStore.EnqueueAsync"/>;
/// 本类型负责状态机其余边,零 DI 注册,事务由调用方起。
/// <para><see cref="WfOutbox.AttemptCount"/> 就是 fence:回写必须带
/// <c>WHERE AttemptCount = @myAttemptCount AND Status = Dispatching</c>。迟到 owner 影响行数为 0,
/// 不抛异常——transport 已是 at-least-once,新 owner 会再投一次。</para>
/// <para>写入的 <c>DateTime</c> 与 <c>null</c> 必须先落局部变量再进 <c>SetColumns</c>,
/// 否则 zh-CN 下 SqlSugar 会把内联表达式格式化进 SQL。</para>
/// </summary>
public static class WfOutboxConsumerStore
{
    /// <summary>指数退避基数(秒),与 execution 回写同一策略。</summary>
    public const int RetryBaseSeconds = 30;

    /// <summary>transport 提供的 <c>RetryAfter</c> 上限;超过则回退到指数退避。</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// 领取:条件 UPDATE + 读回。可领取前提是 <see cref="WfOutboxStatus.Pending"/> 或
    /// <see cref="WfOutboxStatus.Dispatching"/>,且 <c>AvailableAtUtc &lt;= nowUtc</c>。
    /// 领到后 <c>Status=Dispatching</c>、<c>AttemptCount + 1</c>、<c>AvailableAtUtc = now + 可见性超时</c>。
    /// <para>必须在事务内才成立——UPDATE 与读回之间裸自动提交会被并发领取插一脚。</para>
    /// </summary>
    public static async Task<WfOutbox?> ClaimAsync(
        ISqlSugarClient db,
        long outboxId,
        DateTime nowUtc,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "可见性超时必须为正。");

        var availableAtUtc = nowUtc + visibilityTimeout;
        var dispatching = WfOutboxStatus.Dispatching;
        var pending = WfOutboxStatus.Pending;
        var claimed = await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox
            {
                Status = dispatching,
                AttemptCount = o.AttemptCount + 1,
                AvailableAtUtc = availableAtUtc,
            })
            .Where(o => o.Id == outboxId)
            .Where(o => (o.Status == pending || o.Status == dispatching) && o.AvailableAtUtc <= nowUtc)
            .ExecuteCommandAsync();
        if (claimed != 1) return null;

        return await db.Queryable<WfOutbox>()
            .Where(o => o.Id == outboxId)
            .FirstAsync();
    }

    /// <summary>
    /// 投递成功:CAS 进入 <see cref="WfOutboxStatus.Dispatched"/>。影响 0 行 = 迟到回写被挡住。
    /// </summary>
    public static Task<bool> CompleteAsync(
        ISqlSugarClient db,
        long outboxId,
        int attemptCount,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        WriteDispatchingAsync(
            db,
            outboxId,
            attemptCount,
            WfOutboxStatus.Dispatched,
            completedAtUtc: nowUtc,
            lastError: null,
            availableAtUtc: nowUtc,
            cancellationToken);

    /// <summary>
    /// 可重试失败:CAS 回到 <see cref="WfOutboxStatus.Pending"/>,并把 <c>AvailableAtUtc</c> 推到退避时刻。
    /// </summary>
    public static Task<bool> ScheduleRetryAsync(
        ISqlSugarClient db,
        long outboxId,
        int attemptCount,
        DateTime availableAtUtc,
        string? lastError,
        CancellationToken cancellationToken) =>
        WriteDispatchingAsync(
            db,
            outboxId,
            attemptCount,
            WfOutboxStatus.Pending,
            completedAtUtc: null,
            lastError: WfNodeExecutionAttemptStore.Truncate(lastError),
            availableAtUtc: availableAtUtc,
            cancellationToken);

    /// <summary>
    /// 永久失败/预算耗尽:CAS 进入 <see cref="WfOutboxStatus.Failed"/> 死信。
    /// </summary>
    public static Task<bool> FailAsync(
        ISqlSugarClient db,
        long outboxId,
        int attemptCount,
        DateTime nowUtc,
        string? lastError,
        CancellationToken cancellationToken) =>
        WriteDispatchingAsync(
            db,
            outboxId,
            attemptCount,
            WfOutboxStatus.Failed,
            completedAtUtc: nowUtc,
            lastError: WfNodeExecutionAttemptStore.Truncate(lastError),
            availableAtUtc: nowUtc,
            cancellationToken);

    /// <summary>
    /// 人工重放死信:<c>Failed → Pending</c>,<c>AttemptCount</c> 归零,<c>AvailableAtUtc</c> 立即可领。
    /// 只认 <see cref="WfOutboxStatus.Failed"/>;并发第二人影响 0 行。
    /// <para><c>AvailableAtUtc</c> 先落到秒边界:MySQL <c>datetime</c> 常无小数秒,直接写
    /// <c>nowUtc</c> 可能被四舍五入到下一秒,紧随其后的 Claim(<c>AvailableAtUtc &lt;= now</c>) 会 miss。</para>
    /// </summary>
    public static async Task<bool> ReplayFailedAsync(
        ISqlSugarClient db,
        long outboxId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = WfOutboxStatus.Pending;
        var failed = WfOutboxStatus.Failed;
        string? noError = null;
        DateTime? noCompleted = null;
        var dueAtUtc = FloorUtcSeconds(nowUtc);
        var affected = await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox
            {
                Status = pending,
                AttemptCount = 0,
                AvailableAtUtc = dueAtUtc,
                LastError = noError,
                CompletedAtUtc = noCompleted,
            })
            .Where(o => o.Id == outboxId && o.Status == failed)
            .ExecuteCommandAsync();
        return affected == 1;
    }

    /// <summary>UTC 时刻向下取整到秒,供写入无小数秒的 datetime 列且仍满足「立即可领」。</summary>
    internal static DateTime FloorUtcSeconds(DateTime utc) =>
        new(utc.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>
    /// transport <c>RetryAfter</c> 在 <c>(0, 24h]</c> 内则用它,否则
    /// <c>30s &lt;&lt; min(AttemptCount - 1, 5)</c>。上下界是 trust boundary,不是优化。
    /// </summary>
    public static TimeSpan ResolveRetryDelay(int attemptCount, TimeSpan? retryAfter)
    {
        if (retryAfter is { } value && value > TimeSpan.Zero && value <= MaxRetryAfter)
            return value;

        var shift = Math.Min(Math.Max(attemptCount - 1, 0), 5);
        return TimeSpan.FromSeconds(RetryBaseSeconds << shift);
    }

    private static async Task<bool> WriteDispatchingAsync(
        ISqlSugarClient db,
        long outboxId,
        int attemptCount,
        WfOutboxStatus status,
        DateTime? completedAtUtc,
        string? lastError,
        DateTime availableAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatching = WfOutboxStatus.Dispatching;
        var affected = await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox
            {
                Status = status,
                AvailableAtUtc = availableAtUtc,
                CompletedAtUtc = completedAtUtc,
                LastError = lastError,
            })
            .Where(o => o.Id == outboxId && o.AttemptCount == attemptCount && o.Status == dispatching)
            .ExecuteCommandAsync();
        return affected == 1;
    }
}
