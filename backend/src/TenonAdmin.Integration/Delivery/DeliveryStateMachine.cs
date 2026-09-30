namespace TenonAdmin.Integration;

/// <summary>适配器能力快照。</summary>
/// <param name="Registered">适配器仍已注册</param>
/// <param name="SupportsIdempotency">对方按幂等标识去重</param>
/// <param name="SupportsQuery">能按幂等标识查询对方结果</param>
public readonly record struct DeliveryAdapterCapabilities(bool Registered, bool SupportsIdempotency, bool SupportsQuery)
{
    /// <summary>未注册(无法调用)。</summary>
    public static DeliveryAdapterCapabilities Missing => new(false, false, false);

    /// <summary>取适配器的能力。</summary>
    public static DeliveryAdapterCapabilities Of(IDeliveryAdapter? adapter) =>
        adapter is null ? Missing : new(true, adapter.SupportsIdempotency, adapter.SupportsQuery);
}

/// <summary>查询发生的场合(决定「对方没有记录」等结果怎么解释)。</summary>
public enum DeliveryQueryPhase
{
    /// <summary>上次发送结果未知,发送前先核实</summary>
    Verify = 0,

    /// <summary>异步受理后轮询最终结果</summary>
    ConfirmPoll = 1,

    /// <summary>租约过期后的中断恢复</summary>
    Recovery = 2,

    /// <summary>管理员手动查询</summary>
    Manual = 3,
}

/// <summary>一次状态迁移后记录应有的值(回写时整体写入,栅栏另行 +1)。</summary>
public sealed record DeliveryTransition
{
    public required DeliveryStatus Status { get; init; }
    public required DateTime NextAttemptAtUtc { get; init; }
    public required DateTime DeadlineAtUtc { get; init; }
    public int BudgetStartAttempt { get; init; }
    public bool VerifyBeforeSend { get; init; }
    public DateTime? ConfirmDeadlineAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public OutboundOutcome? LastOutcome { get; init; }
    public string? LastError { get; init; }
    public string? RemoteReference { get; init; }
    public long? ResolvedBy { get; init; }
    public string? ResolutionNote { get; init; }

    /// <summary>原因短码(告警与尝试记录用,不入记录列)。</summary>
    public string? Reason { get; init; }

    /// <summary>是否进入需要人工处理的状态(触发告警)。</summary>
    public bool NeedsAttention => Status is DeliveryStatus.NeedsReconciliation or DeliveryStatus.Exhausted or DeliveryStatus.Failed;

    /// <summary>以当前记录为起点(保持原值)。</summary>
    public static DeliveryTransition From(IntegrationDelivery row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new DeliveryTransition
        {
            Status = row.Status,
            NextAttemptAtUtc = row.NextAttemptAtUtc,
            DeadlineAtUtc = row.DeadlineAtUtc,
            BudgetStartAttempt = row.BudgetStartAttempt,
            VerifyBeforeSend = row.VerifyBeforeSend,
            ConfirmDeadlineAtUtc = row.ConfirmDeadlineAtUtc,
            CompletedAtUtc = row.CompletedAtUtc,
            LastOutcome = row.LastOutcome,
            LastError = row.LastError,
            RemoteReference = row.RemoteReference,
            ResolvedBy = row.ResolvedBy,
            ResolutionNote = row.ResolutionNote,
        };
    }
}

/// <summary>
/// 可靠投递的迁移规则(实现契约 §8.3–§8.6),纯函数、不访问数据库。
/// <list type="bullet">
/// <item>成功 → 成功;受理 → 待确认(可查询则按间隔轮询,否则等回调到确认时限);认证失败/拒绝 → 失败;</item>
/// <item>未发出 → 暂时性的(网络、限流)预算内按指数退避(尊重 <c>Retry-After</c>)回到待处理,否则耗尽;
/// 非暂时性的(凭据缺失、目标被拦截等配置问题)不自动重试,直接耗尽;</item>
/// <item>结果未知 → 对方去重:按未发出处理;对方可查询:先查询再决定;两者皆无:待核对。<b>从不对结果未知的调用盲目重发。</b></item>
/// </list>
/// </summary>
public static class DeliveryStateMachine
{
    /// <summary><c>Retry-After</c> 的可信上限。</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);

    /// <summary>第 n 次失败后的退避:基数 × 2^(n-1),封顶;对方给的 <c>Retry-After</c> 更长则用它。</summary>
    public static TimeSpan Backoff(int sendsInBudget, TimeSpan? retryAfter, IntegrationDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var exponent = Math.Clamp(sendsInBudget - 1, 0, 30);
        var seconds = Math.Min(options.BackoffBaseSeconds * Math.Pow(2, exponent), options.BackoffMaxSeconds);
        var delay = TimeSpan.FromSeconds(seconds);
        if (retryAfter is { } hint && hint > delay)
            delay = hint > MaxRetryAfter ? MaxRetryAfter : hint;
        return delay;
    }

    /// <summary>本轮预算内已发送次数。</summary>
    public static int SendsInBudget(IntegrationDelivery row) => row.AttemptCount - row.BudgetStartAttempt;

    /// <summary>一次发送之后的迁移(<paramref name="row"/> 为领取后的快照,发送次数已含本次)。</summary>
    public static DeliveryTransition AfterSend(
        IntegrationDelivery row, DeliveryAdapterCapabilities capabilities, DeliverySendResult result, DateTime nowUtc, IntegrationDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(result);
        var start = DeliveryTransition.From(row) with
        {
            LastOutcome = result.Outcome,
            LastError = Summary(result.Reason, result.ErrorSummary),
            RemoteReference = result.RemoteReference ?? row.RemoteReference,
            VerifyBeforeSend = false,
        };
        return result.Outcome switch
        {
            OutboundOutcome.Succeeded => Succeeded(start, nowUtc),
            OutboundOutcome.Accepted => Awaiting(start, row, capabilities, nowUtc, options, freshWindow: true),
            OutboundOutcome.AuthenticationFailed or OutboundOutcome.Rejected => start with { Status = DeliveryStatus.Failed, NextAttemptAtUtc = nowUtc, Reason = result.Reason ?? "rejected" },
            // 非暂时性的未发出(凭据缺失、目标被拦截等配置问题)重试也不会好:不耗掉整轮预算,直接耗尽交人工(什么都没发出,可人工重试)
            OutboundOutcome.NotSent when !result.Transient =>
                start with { Status = DeliveryStatus.Exhausted, NextAttemptAtUtc = nowUtc, Reason = result.Reason ?? "not_sent" },
            OutboundOutcome.NotSent => Retry(start, row, result.RetryAfter, nowUtc, options, result.Reason ?? "not_sent"),
            // 结果未知(含调用方取消):对方可能已执行
            _ when capabilities.SupportsIdempotency => Retry(start, row, result.RetryAfter, nowUtc, options, result.Reason ?? "unknown"),
            _ when capabilities.SupportsQuery => Verify(start, row, nowUtc, options, result.Reason ?? "unknown"),
            _ => start with { Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = nowUtc, Reason = result.Reason ?? "unknown" },
        };
    }

    /// <summary>
    /// 一次查询之后的迁移。对方已完成 → 成功;已受理 → 待确认;明确失败 → 失败;没有记录 → 可安全发送(受理后确认阶段则转待核对);
    /// 查询本身失败 → 期限内稍后再查,否则待核对。手动查询失败时返回 null(状态不变,只记尝试)。
    /// </summary>
    public static DeliveryTransition? AfterQuery(
        IntegrationDelivery row, DeliveryAdapterCapabilities capabilities, DeliveryQueryResult result, DeliveryQueryPhase phase,
        DateTime nowUtc, IntegrationDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(result);
        var start = DeliveryTransition.From(row) with
        {
            RemoteReference = result.RemoteReference ?? row.RemoteReference,
            VerifyBeforeSend = false,
        };
        var awaitingPhase = phase == DeliveryQueryPhase.ConfirmPoll
            || (phase == DeliveryQueryPhase.Manual && row.Status == DeliveryStatus.AwaitingConfirmation);
        switch (result.State)
        {
            case RemoteDeliveryState.Succeeded:
                return Succeeded(start with { LastOutcome = OutboundOutcome.Succeeded, LastError = null }, nowUtc);
            case RemoteDeliveryState.Accepted:
                return Awaiting(start with { LastOutcome = OutboundOutcome.Accepted, LastError = null }, row, capabilities, nowUtc, options,
                    freshWindow: phase == DeliveryQueryPhase.Manual);
            case RemoteDeliveryState.Failed:
                return start with { Status = DeliveryStatus.Failed, NextAttemptAtUtc = nowUtc, LastError = Summary(result.Reason ?? "remote_failed", null), Reason = "remote_failed" };
            case RemoteDeliveryState.NotFound when awaitingPhase:
                // 曾经受理、现在却查不到:与已知事实矛盾,交人工
                return start with { Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = nowUtc, LastError = "accepted_but_missing", Reason = "accepted_but_missing" };
            case RemoteDeliveryState.NotFound when phase == DeliveryQueryPhase.Manual:
                return ResetBudget(start, row, nowUtc, options) with { Status = DeliveryStatus.Pending, LastError = "remote_not_found", Reason = "remote_not_found" };
            case RemoteDeliveryState.NotFound:
                return start with { Status = DeliveryStatus.Pending, NextAttemptAtUtc = nowUtc, LastError = "remote_not_found", Reason = "remote_not_found" };
            default:
                if (phase == DeliveryQueryPhase.Manual) return null;
                if (awaitingPhase)
                {
                    var poll = nowUtc.AddSeconds(options.ConfirmPollSeconds);
                    return row.ConfirmDeadlineAtUtc is { } confirmBy && poll < confirmBy
                        ? start with { Status = DeliveryStatus.AwaitingConfirmation, NextAttemptAtUtc = poll, LastError = Summary(result.Reason ?? "query_failed", null) }
                        : start with { Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = nowUtc, LastError = "confirm_unverified", Reason = "confirm_unverified" };
                }
                return Verify(start, row, nowUtc, options, result.Reason ?? "query_failed");
        }
    }

    /// <summary>服务端判定当前可执行的人工操作。<paramref name="hasUnsafeUnknown"/>:存在对方不去重的「结果未知」发送。</summary>
    public static IReadOnlyList<string> AllowedActions(IntegrationDelivery row, DeliveryAdapterCapabilities capabilities, bool hasUnsafeUnknown)
    {
        ArgumentNullException.ThrowIfNull(row);
        var status = row.Status;
        if (status is DeliveryStatus.Dispatching or DeliveryStatus.Succeeded or DeliveryStatus.Cancelled) return [];
        var actions = new List<string>();
        var canCall = capabilities.Registered;
        var retrySafe = status == DeliveryStatus.Failed
            || (status == DeliveryStatus.Exhausted && !row.VerifyBeforeSend && (capabilities.SupportsIdempotency || !hasUnsafeUnknown));
        if (canCall && retrySafe) actions.Add(DeliveryActions.Retry);
        if (canCall && capabilities.SupportsQuery && status is DeliveryStatus.NeedsReconciliation or DeliveryStatus.Exhausted
                or DeliveryStatus.AwaitingConfirmation or DeliveryStatus.Failed)
            actions.Add(DeliveryActions.Query);
        if (status is DeliveryStatus.NeedsReconciliation or DeliveryStatus.AwaitingConfirmation or DeliveryStatus.Exhausted)
            actions.Add(DeliveryActions.ConfirmSucceeded);
        if (canCall && status == DeliveryStatus.NeedsReconciliation) actions.Add(DeliveryActions.ConfirmNotExecuted);
        actions.Add(DeliveryActions.Cancel);
        return actions;
    }

    /// <summary>
    /// 人工重新发送前重置预算,开始新一轮发送:发送次数从当前起算,截止时刻顺延一个默认时限,立即可处理;
    /// 上一轮的确认时限与对方引用不再适用,一并清空(否则新一轮会继承早已过期的时限,中断恢复也会把重发误当确认轮询)。
    /// </summary>
    public static DeliveryTransition ResetBudget(DeliveryTransition start, IntegrationDelivery row, DateTime nowUtc, IntegrationDeliveryOptions options) =>
        start with
        {
            Status = DeliveryStatus.Pending,
            BudgetStartAttempt = row.AttemptCount,
            DeadlineAtUtc = nowUtc.AddHours(options.MaxAgeHours),
            NextAttemptAtUtc = nowUtc,
            VerifyBeforeSend = false,
            CompletedAtUtc = null,
            ConfirmDeadlineAtUtc = null,
            RemoteReference = null,
        };

    private static DeliveryTransition Succeeded(DeliveryTransition start, DateTime nowUtc) =>
        start with { Status = DeliveryStatus.Succeeded, CompletedAtUtc = nowUtc, NextAttemptAtUtc = nowUtc, LastError = null };

    /// <summary>
    /// 进入待确认。<paramref name="freshWindow"/>:发送被受理、人工查询得知受理时从现在起重新计时;
    /// 自动查询(轮询、核实、恢复)沿用本轮已有的确认时限,没有才新开。
    /// </summary>
    private static DeliveryTransition Awaiting(
        DeliveryTransition start, IntegrationDelivery row, DeliveryAdapterCapabilities capabilities, DateTime nowUtc, IntegrationDeliveryOptions options,
        bool freshWindow)
    {
        var confirmBy = (freshWindow ? null : row.ConfirmDeadlineAtUtc) ?? nowUtc.AddHours(options.ConfirmDeadlineHours);
        // 可查询则按间隔轮询;否则只等回调,到确认时限再转待核对。受理不是错误,清空上次错误
        var next = capabilities.SupportsQuery ? nowUtc.AddSeconds(options.ConfirmPollSeconds) : confirmBy;
        return start with
        {
            Status = DeliveryStatus.AwaitingConfirmation, ConfirmDeadlineAtUtc = confirmBy, NextAttemptAtUtc = next < confirmBy ? next : confirmBy, LastError = null,
        };
    }

    private static DeliveryTransition Retry(
        DeliveryTransition start, IntegrationDelivery row, TimeSpan? retryAfter, DateTime nowUtc, IntegrationDeliveryOptions options, string reason)
    {
        var sends = SendsInBudget(row);
        var next = nowUtc + Backoff(sends, retryAfter, options);
        return sends >= row.MaxAttempts || next >= row.DeadlineAtUtc
            ? start with { Status = DeliveryStatus.Exhausted, NextAttemptAtUtc = nowUtc, Reason = sends >= row.MaxAttempts ? "max_attempts" : "deadline" }
            : start with { Status = DeliveryStatus.Pending, NextAttemptAtUtc = next, Reason = reason };
    }

    private static DeliveryTransition Verify(DeliveryTransition start, IntegrationDelivery row, DateTime nowUtc, IntegrationDeliveryOptions options, string reason)
    {
        var next = nowUtc + Backoff(Math.Max(1, SendsInBudget(row)), null, options);
        return next < row.DeadlineAtUtc
            ? start with { Status = DeliveryStatus.Pending, VerifyBeforeSend = true, NextAttemptAtUtc = next, Reason = reason }
            : start with { Status = DeliveryStatus.NeedsReconciliation, VerifyBeforeSend = true, NextAttemptAtUtc = nowUtc, Reason = "verify_deadline" };
    }

    private static string? Summary(string? reason, string? summary) =>
        IntegrationText.Truncate(IntegrationText.JoinReason(reason, summary), 500);
}
