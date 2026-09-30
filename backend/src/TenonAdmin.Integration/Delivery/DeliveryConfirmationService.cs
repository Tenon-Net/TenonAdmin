using SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IDeliveryConfirmationService"/> 默认实现:按投递标识读记录,以看到的栅栏 CAS 迁移到成功或失败并追加一条回调确认记录。
/// 可从任何未完结状态确认(含处理中:仍在进行的调用回写时成为迟到结果);已成功的重复回调视为已处理;
/// 已取消或已确认为相反结果的记录不自动改写(返回 <see cref="DeliveryConfirmOutcome.Rejected"/>,交人工查看)。
/// <para>投递标识来自对方,回调只能确认本端点负责的记录(<see cref="IsConfirmable"/>);其余一律按「没有这条记录」答复。</para>
/// </summary>
public class DeliveryConfirmationService(
    ISqlSugarClient db,
    DeliveryAlertPublisher alerts,
    TimeProvider time) : IDeliveryConfirmationService
{
    /// <summary>CAS 冲突时的重读次数。</summary>
    protected virtual int MaxRetries => 3;

    /// <inheritdoc />
    public virtual async Task<DeliveryConfirmOutcome> ConfirmAsync(DeliveryConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmation.DeliveryKey);
        if (confirmation.Adapters is not { Count: > 0 })
            throw new ArgumentException("回调确认须声明本端点负责的适配器(至少一个)。", nameof(confirmation));
        var key = confirmation.DeliveryKey.Trim();
        var succeeded = confirmation.Succeeded;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var row = await db.Queryable<IntegrationDelivery>().Where(d => d.DeliveryKey == key).FirstAsync(cancellationToken);
            if (row is null || !IsConfirmable(row, confirmation)) return DeliveryConfirmOutcome.NotFound;
            var target = succeeded ? DeliveryStatus.Succeeded : DeliveryStatus.Failed;
            if (row.Status == target) return DeliveryConfirmOutcome.AlreadyApplied;
            if (row.Status is DeliveryStatus.Cancelled || (row.Status == DeliveryStatus.Succeeded && !succeeded))
                return DeliveryConfirmOutcome.Rejected;

            var now = IntegrationAppState.FloorSeconds(time.GetUtcNow().UtcDateTime);
            var transition = DeliveryTransition.From(row) with
            {
                Status = target,
                NextAttemptAtUtc = now,
                CompletedAtUtc = succeeded ? now : null,
                VerifyBeforeSend = false,
                LastOutcome = succeeded ? OutboundOutcome.Succeeded : OutboundOutcome.Rejected,
                LastError = succeeded ? null : "remote_failed_callback",
                RemoteReference = confirmation.RemoteReference ?? row.RemoteReference,
                Reason = succeeded ? "callback_succeeded" : "remote_failed_callback",
            };
            var record = new IntegrationDeliveryAttempt
            {
                DeliveryId = row.Id,
                AttemptNo = row.AttemptCount,
                Kind = DeliveryAttemptKind.Confirm,
                Trigger = DeliveryAttemptTrigger.Callback,
                StartedAtUtc = now,
                FinishedAtUtc = now,
                Outcome = target.ToString(),
                Note = IntegrationText.Truncate(confirmation.Note, 256),
            };
            if (await DeliveryStore.ApplyWithAttemptAsync(db, row, transition, record, "回调确认未生效:记录已变化,将重读。", cancellationToken))
            {
                await alerts.PublishAsync(row, transition, CancellationToken.None);
                return DeliveryConfirmOutcome.Applied;
            }
        }
        throw new InvalidOperationException($"投递 {key} 的回调确认多次与并发更新冲突,请稍后重试。");
    }

    /// <summary>
    /// 回调能否确认这条记录:适配器须在回调声明的范围内、给了业务键时须一致,且记录真的发出过(发送次数 &gt; 0)——
    /// 从未发送的记录没有对方结果可言,伪造的回调不能让它跳过投递直接完结。
    /// </summary>
    protected virtual bool IsConfirmable(IntegrationDelivery row, DeliveryConfirmation confirmation) =>
        confirmation.Adapters.Any(a => string.Equals(a, row.Adapter, StringComparison.OrdinalIgnoreCase))
        && (confirmation.BusinessKey is null || string.Equals(confirmation.BusinessKey, row.BusinessKey, StringComparison.Ordinal))
        && row.AttemptCount > 0;
}
