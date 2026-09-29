using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IDeliveryDispatcher"/> 默认实现(实现契约 §8.2–§8.4)。每条记录三段:
/// <list type="number">
/// <item><b>领取</b>:单条条件更新(栅栏 +1、租约;发送类动作发送次数 +1),领取失败即被其他节点抢先,跳过;</item>
/// <item><b>调用</b>:在任何数据库事务之外调用适配器(发送或查询),适配器异常按结果未知处理;</item>
/// <item><b>回写</b>:短事务内按领取时的栅栏 CAS 迁移并追加尝试记录。影响 0 行说明记录已被恢复或他人处理:迟到结果只追加尝试记录,
/// 唯一例外是迟到的成功/受理证据可以把仍在待核对的记录推进。</item>
/// </list>
/// 租约过期的处理中记录<b>不</b>视为可安全重发:以新栅栏夺回后,对方去重 → 用同一幂等标识重发;对方可查询 → 先查询;两者皆无 → 待核对。
/// </summary>
public class DeliveryDispatcher(
    ISqlSugarClient db,
    IEnumerable<IDeliveryAdapter> adapters,
    DeliveryAlertPublisher alerts,
    IntegrationOptions options,
    TenonAdminOptions adminOptions,
    TimeProvider time,
    ILogger<DeliveryDispatcher> logger) : IDeliveryDispatcher
{
    private const string LateNote = "迟到的结果:记录已被恢复或他人处理,未覆盖当前状态。";

    /// <summary>领取者标识(仅供排障展示;正确性只靠栅栏)。</summary>
    protected virtual string NodeName =>
        string.IsNullOrWhiteSpace(adminOptions.Jobs.NodeName)
            ? $"{Environment.MachineName}#{Environment.ProcessId}"
            : adminOptions.Jobs.NodeName!;

    /// <inheritdoc />
    public virtual async Task<DeliveryDispatchSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var summary = new DeliveryDispatchSummary();
        var candidates = await LoadCandidatesAsync(time.GetUtcNow().UtcDateTime, cancellationToken);
        summary.Scanned = candidates.Count;
        foreach (var row in candidates)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                await ProcessAsync(row, summary, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // 一条记录的意外(如数据库瞬断)不拖垮整批;该记录留在原状态或租约中,下轮或恢复时再处理
                logger.LogError("处理投递记录失败。DeliveryId={DeliveryId} DeliveryKey={DeliveryKey} ExceptionType={ExceptionType}",
                    row.Id, row.DeliveryKey, ex.GetType().Name);
            }
        }
        return summary;
    }

    /// <summary>候选:到期的待处理/待确认记录,以及租约已过期的处理中记录(最早到期的在前)。</summary>
    protected virtual Task<List<IntegrationDelivery>> LoadCandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var pending = DeliveryStatus.Pending;
        var awaiting = DeliveryStatus.AwaitingConfirmation;
        var dispatching = DeliveryStatus.Dispatching;
        return db.Queryable<IntegrationDelivery>()
            .Where(d => ((d.Status == pending || d.Status == awaiting) && d.NextAttemptAtUtc <= nowUtc)
                || (d.Status == dispatching && d.LeaseUntilUtc < nowUtc))
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(options.Delivery.BatchSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>按记录状态与适配器能力决定动作并执行。</summary>
    protected virtual async Task ProcessAsync(IntegrationDelivery row, DeliveryDispatchSummary summary, CancellationToken cancellationToken)
    {
        var adapter = FindAdapter(row.Adapter);
        var capabilities = DeliveryAdapterCapabilities.Of(adapter);
        var now = Now();
        var d = options.Delivery;

        if (row.Status == DeliveryStatus.Dispatching)
        {
            await RecoverAsync(row, adapter, capabilities, summary, cancellationToken);
            return;
        }
        if (adapter is null)
        {
            await ExpireAsync(row, DeliveryTransition.From(row) with
            {
                Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = now, LastError = "adapter_missing", Reason = "adapter_missing",
            }, summary, cancellationToken);
            return;
        }
        if (row.Status == DeliveryStatus.AwaitingConfirmation)
        {
            if (row.ConfirmDeadlineAtUtc is { } confirmBy && confirmBy <= now)
                await ExpireAsync(row, DeliveryTransition.From(row) with
                {
                    Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = now, LastError = "confirm_timeout", Reason = "confirm_timeout",
                }, summary, cancellationToken);
            else if (capabilities.SupportsQuery)
                await QueryAsync(row, adapter, capabilities, DeliveryQueryPhase.ConfirmPoll, summary, cancellationToken);
            else
                await ExpireAsync(row, DeliveryTransition.From(row) with { NextAttemptAtUtc = row.ConfirmDeadlineAtUtc ?? now.AddHours(d.ConfirmDeadlineHours) },
                    summary, cancellationToken);
            return;
        }

        // Pending
        if (row.DeadlineAtUtc <= now)
        {
            // 截止时刻已过:上次结果未知(待核实)的交人工核对,否则耗尽
            var status = row.VerifyBeforeSend ? DeliveryStatus.NeedsReconciliation : DeliveryStatus.Exhausted;
            await ExpireAsync(row, DeliveryTransition.From(row) with { Status = status, NextAttemptAtUtc = now, Reason = "deadline", LastError = row.LastError ?? "deadline" },
                summary, cancellationToken);
            return;
        }
        if (row.VerifyBeforeSend)
        {
            if (capabilities.SupportsQuery)
                await QueryAsync(row, adapter, capabilities, DeliveryQueryPhase.Verify, summary, cancellationToken);
            else
                await ExpireAsync(row, DeliveryTransition.From(row) with { Status = DeliveryStatus.NeedsReconciliation, NextAttemptAtUtc = now, Reason = "unverifiable" },
                    summary, cancellationToken);
            return;
        }
        if (DeliveryStateMachine.SendsInBudget(row) >= row.MaxAttempts)
        {
            await ExpireAsync(row, DeliveryTransition.From(row) with { Status = DeliveryStatus.Exhausted, NextAttemptAtUtc = now, Reason = "max_attempts" },
                summary, cancellationToken);
            return;
        }
        await SendAsync(row, adapter, capabilities, summary, cancellationToken);
    }

    /// <summary>发送:领取(发送次数 +1)→ 事务外调用 → 按栅栏回写。</summary>
    protected virtual async Task SendAsync(
        IntegrationDelivery row, IDeliveryAdapter adapter, DeliveryAdapterCapabilities capabilities, DeliveryDispatchSummary summary,
        CancellationToken cancellationToken, bool alreadyClaimed = false)
    {
        if (!alreadyClaimed && !await ClaimAsync(row, send: true, cancellationToken)) return;
        if (!alreadyClaimed) summary.Claimed++;
        summary.Sent++;
        var started = time.GetUtcNow().UtcDateTime;
        DeliverySendResult result;
        try
        {
            result = await adapter.SendAsync(ContextOf(row), cancellationToken);
        }
        catch (Exception ex)
        {
            // 适配器自身异常:请求可能已发出,按结果未知处理(摘要只记异常类型,自定义代码的消息可能夹带敏感信息)
            logger.LogError("投递适配器 {Adapter} 发送时抛出异常,按结果未知处理。DeliveryKey={DeliveryKey} ExceptionType={ExceptionType}",
                adapter.Name, row.DeliveryKey, ex.GetType().Name);
            result = new DeliverySendResult(OutboundOutcome.Unknown, Reason: "adapter_error", ErrorSummary: ex.GetType().Name);
        }

        var now = Now();
        var transition = DeliveryStateMachine.AfterSend(row, capabilities, result, now, options.Delivery);
        var attempt = Attempt(row, DeliveryAttemptKind.Send, started, result.Outcome.ToString(), result.HttpStatus, result.CallId,
            IntegrationText.JoinReason(result.Reason, result.ErrorSummary));
        await WriteBackAsync(row, transition, attempt, lateSuccess: result.Outcome is OutboundOutcome.Succeeded or OutboundOutcome.Accepted,
            summary, cancellationToken);
    }

    /// <summary>查询:领取(发送次数不变)→ 事务外查询 → 按栅栏回写。</summary>
    protected virtual async Task QueryAsync(
        IntegrationDelivery row, IDeliveryAdapter adapter, DeliveryAdapterCapabilities capabilities, DeliveryQueryPhase phase,
        DeliveryDispatchSummary summary, CancellationToken cancellationToken, bool alreadyClaimed = false)
    {
        var originalStatus = row.Status;
        if (!alreadyClaimed && !await ClaimAsync(row, send: false, cancellationToken)) return;
        if (!alreadyClaimed) summary.Claimed++;
        summary.Queried++;
        var started = time.GetUtcNow().UtcDateTime;
        DeliveryQueryResult result;
        try
        {
            result = await adapter.QueryAsync(ContextOf(row), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError("投递适配器 {Adapter} 查询时抛出异常,按查询失败处理。DeliveryKey={DeliveryKey} ExceptionType={ExceptionType}",
                adapter.Name, row.DeliveryKey, ex.GetType().Name);
            result = new DeliveryQueryResult(RemoteDeliveryState.Unknown, Reason: "adapter_error");
        }

        // 领取已把 row 改成处理中(回写按它的栅栏与状态做 CAS);迁移则以领取前的状态为起点,规则不改写状态或返回 null 时即回到它
        var basis = row.ShallowCopy();
        basis.Status = originalStatus;
        var transition = DeliveryStateMachine.AfterQuery(basis, capabilities, result, phase, Now(), options.Delivery)
            ?? DeliveryTransition.From(basis);
        var attempt = Attempt(row, DeliveryAttemptKind.Query, started, result.State.ToString(), result.HttpStatus, result.CallId, result.Reason);
        await WriteBackAsync(row, transition, attempt, lateSuccess: result.State is RemoteDeliveryState.Succeeded or RemoteDeliveryState.Accepted,
            summary, cancellationToken);
    }

    /// <summary>
    /// 中断恢复:租约已过期的处理中记录——先以新栅栏夺回并追加一条恢复记录,再按适配器能力决定:
    /// 对方去重 → 同一幂等标识重发(预算已尽或截止时刻已过则耗尽);对方可查询 → 先查询;两者皆无 → 待核对。
    /// </summary>
    protected virtual async Task RecoverAsync(
        IntegrationDelivery row, IDeliveryAdapter? adapter, DeliveryAdapterCapabilities capabilities, DeliveryDispatchSummary summary,
        CancellationToken cancellationToken)
    {
        var now = Now();
        // 本轮已被受理(有确认时限)且可查询:中断的是确认轮询,继续查询而不是重发。确认时限只属于本轮发送,人工重发时即清空
        // (DeliveryStateMachine.ResetBudget),所以不会拿上一轮的时限把重发误判成确认轮询
        var confirming = adapter is not null && capabilities.SupportsQuery && row.ConfirmDeadlineAtUtc is not null;
        var resend = !confirming && adapter is not null && capabilities.SupportsIdempotency
            && DeliveryStateMachine.SendsInBudget(row) < row.MaxAttempts && row.DeadlineAtUtc > now;
        var previousOwner = row.LeaseOwner;
        if (!await ClaimAsync(row, send: resend, cancellationToken)) return;
        summary.Claimed++;
        summary.Recovered++;
        await db.Insertable(Attempt(row, DeliveryAttemptKind.Recovery, now, "lease_expired", null, null, null,
            note: IntegrationText.Truncate($"租约过期,夺回自 {previousOwner ?? "未知节点"}", 256))).ExecuteCommandAsync(CancellationToken.None);

        if (confirming)
        {
            await QueryAsync(row, adapter!, capabilities, DeliveryQueryPhase.ConfirmPoll, summary, cancellationToken, alreadyClaimed: true);
            return;
        }
        if (resend)
        {
            await SendAsync(row, adapter!, capabilities, summary, cancellationToken, alreadyClaimed: true);
            return;
        }
        if (adapter is not null && capabilities.SupportsQuery)
        {
            await QueryAsync(row, adapter, capabilities, DeliveryQueryPhase.Recovery, summary, cancellationToken, alreadyClaimed: true);
            return;
        }

        // 对方去重却没有重发:次数预算已尽,或预算仍有余但截止时刻已过
        var status = adapter is not null && capabilities.SupportsIdempotency ? DeliveryStatus.Exhausted : DeliveryStatus.NeedsReconciliation;
        var reason = adapter is null ? "adapter_missing"
            : status != DeliveryStatus.Exhausted ? "interrupted_unknown"
            : DeliveryStateMachine.SendsInBudget(row) >= row.MaxAttempts ? "max_attempts" : "deadline";
        var transition = DeliveryTransition.From(row) with
        {
            Status = status, NextAttemptAtUtc = now, LastOutcome = OutboundOutcome.Unknown, LastError = reason, Reason = reason,
        };
        await WriteBackAsync(row, transition, null, lateSuccess: false, summary, cancellationToken);
    }

    /// <summary>不发生调用的状态变更(截止、确认超时、次数上限、适配器缺失):按看到的栅栏直接迁移并记一条到期记录。</summary>
    protected virtual async Task ExpireAsync(IntegrationDelivery row, DeliveryTransition transition, DeliveryDispatchSummary summary, CancellationToken cancellationToken)
    {
        var changed = transition.Status != row.Status;
        var attempt = changed
            ? Attempt(row, DeliveryAttemptKind.Expired, time.GetUtcNow().UtcDateTime, transition.Status.ToString(), null, null, transition.Reason)
            : null;
        if (attempt is null)
        {
            // 仅改下次处理时刻(如只等回调的受理记录):不追加尝试记录
            if (await DeliveryStore.ApplyAsync(db, row.Id, row.Fence, row.Status, transition, CancellationToken.None)) summary.Claimed++;
            return;
        }
        if (!await DeliveryStore.ApplyWithAttemptAsync(db, row, transition, attempt, LateNote, CancellationToken.None)) return;
        summary.Claimed++;
        Count(transition, summary);
        await alerts.PublishAsync(row, transition, CancellationToken.None);
    }

    /// <summary>回写(短事务):按领取时的栅栏迁移 + 追加尝试记录;迟到的成功/受理证据可推进仍在待核对的记录。</summary>
    protected virtual async Task WriteBackAsync(
        IntegrationDelivery claimed, DeliveryTransition transition, IntegrationDeliveryAttempt? attempt, bool lateSuccess,
        DeliveryDispatchSummary summary, CancellationToken cancellationToken)
    {
        // 回写不跟随调用方取消:进程停机时也尽量把已拿到的结果落库,落不了则由租约过期后的恢复接手
        bool applied;
        if (attempt is null)
            applied = await DeliveryStore.ApplyAsync(db, claimed.Id, claimed.Fence, claimed.Status, transition, CancellationToken.None);
        else
            applied = await DeliveryStore.ApplyWithAttemptAsync(db, claimed, transition, attempt, LateNote, CancellationToken.None);

        if (applied)
        {
            Count(transition, summary);
            await alerts.PublishAsync(claimed, transition, CancellationToken.None);
            return;
        }

        summary.Late++;
        logger.LogInformation("投递回写被栅栏挡住(迟到结果)。DeliveryKey={DeliveryKey} Fence={Fence}", claimed.DeliveryKey, claimed.Fence);
        if (!lateSuccess) return;
        var current = await db.Queryable<IntegrationDelivery>().Where(d => d.Id == claimed.Id).FirstAsync(CancellationToken.None);
        if (current is null || current.Status != DeliveryStatus.NeedsReconciliation) return;
        var promote = transition with { BudgetStartAttempt = current.BudgetStartAttempt, DeadlineAtUtc = current.DeadlineAtUtc, ResolvedBy = current.ResolvedBy, ResolutionNote = current.ResolutionNote };
        if (promote.Status is DeliveryStatus.Succeeded or DeliveryStatus.AwaitingConfirmation
            && await DeliveryStore.ApplyAsync(db, current.Id, current.Fence, current.Status, promote, CancellationToken.None))
        {
            logger.LogInformation("迟到的成功/受理证据推进了待核对记录。DeliveryKey={DeliveryKey} → {Status}", claimed.DeliveryKey, promote.Status);
            Count(promote, summary);
        }
    }

    /// <summary>领取:成功后 <paramref name="row"/> 同步为领取后的值。</summary>
    protected virtual Task<bool> ClaimAsync(IntegrationDelivery row, bool send, CancellationToken cancellationToken) =>
        DeliveryStore.ClaimAsync(db, row, Now().AddSeconds(options.Delivery.LeaseSeconds), NodeName, send, cancellationToken);

    /// <summary>按名取适配器(不区分大小写)。</summary>
    protected virtual IDeliveryAdapter? FindAdapter(string name) => adapters.FindByName(name);

    /// <summary>当前 UTC 时刻(取整到秒,与各方言 datetime 精度一致)。</summary>
    protected DateTime Now() => IntegrationAppState.FloorSeconds(time.GetUtcNow().UtcDateTime);

    /// <summary>构造交给适配器的上下文。</summary>
    protected static DeliveryContext ContextOf(IntegrationDelivery row) => DeliveryContext.From(row);

    private IntegrationDeliveryAttempt Attempt(
        IntegrationDelivery row, DeliveryAttemptKind kind, DateTime startedUtc, string outcome, int? httpStatus, string? callId, string? summary,
        string? note = null) => new()
    {
        DeliveryId = row.Id,
        AttemptNo = row.AttemptCount,
        Kind = kind,
        Trigger = DeliveryAttemptTrigger.Worker,
        StartedAtUtc = IntegrationAppState.FloorSeconds(startedUtc),
        FinishedAtUtc = Now(),
        Outcome = IntegrationText.Truncate(outcome, 32) ?? "",
        HttpStatus = httpStatus,
        CallId = callId,
        ErrorSummary = summary,
        Note = note,
        NodeName = IntegrationText.Truncate(NodeName, 128),
    };

    private static void Count(DeliveryTransition transition, DeliveryDispatchSummary summary)
    {
        if (transition.Status == DeliveryStatus.Succeeded) summary.Succeeded++;
        else if (transition.NeedsAttention) summary.NeedAttention++;
    }
}
