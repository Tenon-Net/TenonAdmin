using SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 投递记录的条件更新(实现契约 §8.2)。<see cref="IntegrationDelivery.Fence"/> 是唯一的并发栅栏:
/// 领取、回写、恢复、人工操作与回调确认都以「我看到的栅栏值」做 CAS 并把栅栏 +1,影响 0 行即说明记录已被他人改动。
/// <para>写入的时间与 null 一律先落局部变量再进 <c>SetColumns</c>(否则某些区域设置下 SqlSugar 会把内联表达式格式化进 SQL,与工作流 outbox 同一约束)。</para>
/// </summary>
public static class DeliveryStore
{
    /// <summary>
    /// 领取:<c>WHERE Id AND Fence = 看到的值 AND Status = 看到的状态</c> → 处理中、栅栏 +1、租约;发送类动作发送次数 +1。
    /// 单条语句自动提交即可(不读回:新值都由调用方算出)。
    /// </summary>
    public static async Task<bool> ClaimAsync(
        ISqlSugarClient db, IntegrationDelivery seen, DateTime leaseUntilUtc, string owner, bool send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(seen);
        var id = seen.Id;
        var fence = seen.Fence;
        var status = seen.Status;
        var newFence = fence + 1;
        var attempts = seen.AttemptCount + (send ? 1 : 0);
        var dispatching = DeliveryStatus.Dispatching;
        DateTime? lease = leaseUntilUtc;
        var leaseOwner = IntegrationText.Truncate(owner, 128);
        var affected = await db.Updateable<IntegrationDelivery>()
            .SetColumns(d => new IntegrationDelivery
            {
                Status = dispatching,
                Fence = newFence,
                AttemptCount = attempts,
                LeaseUntilUtc = lease,
                LeaseOwner = leaseOwner,
            })
            .Where(d => d.Id == id && d.Fence == fence && d.Status == status)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1) return false;
        seen.Status = dispatching;
        seen.Fence = newFence;
        seen.AttemptCount = attempts;
        seen.LeaseUntilUtc = lease;
        seen.LeaseOwner = leaseOwner;
        return true;
    }

    /// <summary>
    /// 按栅栏把记录迁移到 <paramref name="transition"/>:<c>WHERE Id AND Fence = expectedFence AND Status = expectedStatus</c>,
    /// 栅栏 +1、清空租约。返回是否生效。
    /// </summary>
    public static async Task<bool> ApplyAsync(
        ISqlSugarClient db, long id, long expectedFence, DeliveryStatus expectedStatus, DeliveryTransition transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transition);
        var newFence = expectedFence + 1;
        var status = transition.Status;
        var next = transition.NextAttemptAtUtc;
        var deadline = transition.DeadlineAtUtc;
        var budget = transition.BudgetStartAttempt;
        var verify = transition.VerifyBeforeSend;
        var confirmBy = transition.ConfirmDeadlineAtUtc;
        var completed = transition.CompletedAtUtc;
        var lastOutcome = transition.LastOutcome;
        var lastError = IntegrationText.Truncate(transition.LastError, 512);
        var remote = IntegrationText.Truncate(transition.RemoteReference, 128);
        var resolvedBy = transition.ResolvedBy;
        var note = IntegrationText.Truncate(transition.ResolutionNote, 256);
        DateTime? noLease = null;
        string? noOwner = null;
        var affected = await db.Updateable<IntegrationDelivery>()
            .SetColumns(d => new IntegrationDelivery
            {
                Status = status,
                Fence = newFence,
                NextAttemptAtUtc = next,
                DeadlineAtUtc = deadline,
                BudgetStartAttempt = budget,
                VerifyBeforeSend = verify,
                ConfirmDeadlineAtUtc = confirmBy,
                CompletedAtUtc = completed,
                LastOutcome = lastOutcome,
                LastError = lastError,
                RemoteReference = remote,
                ResolvedBy = resolvedBy,
                ResolutionNote = note,
                LeaseUntilUtc = noLease,
                LeaseOwner = noOwner,
            })
            .Where(d => d.Id == id && d.Fence == expectedFence && d.Status == expectedStatus)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    /// <summary>在一个短事务内按栅栏迁移并追加尝试记录;迁移未生效时尝试记录照常追加(标注为迟到或冲突)。返回迁移是否生效。</summary>
    public static async Task<bool> ApplyWithAttemptAsync(
        ISqlSugarClient db, IntegrationDelivery seen, DeliveryTransition? transition, IntegrationDeliveryAttempt attempt,
        string lateNote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(attempt);
        var applied = false;
        var result = await db.Ado.UseTranAsync(async () =>
        {
            applied = transition is not null && await ApplyAsync(db, seen.Id, seen.Fence, seen.Status, transition, cancellationToken);
            if (transition is not null && !applied)
                attempt.Note = IntegrationText.Truncate(string.IsNullOrEmpty(attempt.Note) ? lateNote : $"{lateNote} {attempt.Note}", 256);
            attempt.ErrorSummary = IntegrationText.Truncate(attempt.ErrorSummary, 512);
            await db.Insertable(attempt).ExecuteCommandAsync(cancellationToken);
        });
        if (!result.IsSuccess) throw result.ErrorException;
        return applied;
    }
}
