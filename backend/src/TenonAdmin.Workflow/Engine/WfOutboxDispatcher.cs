using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// outbox 消费者(Task 8c)——「领取 → 事务外 transport → CAS 回写」的唯一装配点。
/// <para>事务边界写死为三段:
/// 1) tx1 领取 <see cref="WfOutboxConsumerStore.ClaimAsync"/>;
/// 2) 无事务调用 <see cref="IWfOutboxTransport"/>;
/// 3) tx2 按结果 Complete / ScheduleRetry / Fail,CAS <c>AttemptCount</c>。
/// </para>
/// <para>迟到回写影响 0 行时不抛异常:transport 是 at-least-once,新 owner 会再投。
/// <see cref="OperationCanceledException"/> 原样穿透,行停在 Dispatching,可见性超时后可重领。</para>
/// </summary>
public class WfOutboxDispatcher(
    ISqlSugarClient db,
    IWfOutboxTransport transport,
    WorkflowOptions options,
    TimeProvider time,
    ILogger<WfOutboxDispatcher>? logger = null)
{
    /// <summary>
    /// 跑一拍。领不到返回 <c>null</c>。返回值来自回写后的一次 SELECT,不从内存反推。
    /// </summary>
    public virtual async Task<WfOutboxStatus?> RunAsync(long outboxId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkflowOptionsValidation.Validate(options);

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var visibility = TimeSpan.FromSeconds(Math.Max(1, options.OutboxVisibilityTimeoutSeconds));

        var claimTran = await db.Ado.UseTranAsync(() =>
            WfOutboxConsumerStore.ClaimAsync(db, outboxId, nowUtc, visibility, cancellationToken));
        if (!claimTran.IsSuccess)
            throw claimTran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        if (claimTran.Data is null)
            return null;

        var claimed = claimTran.Data;
        var result = await InvokeTransportAsync(claimed, cancellationToken);
        nowUtc = time.GetUtcNow().UtcDateTime;

        var writeTran = await db.Ado.UseTranAsync(() =>
            WriteBackAsync(claimed, result, nowUtc, cancellationToken));
        if (!writeTran.IsSuccess)
            throw writeTran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);

        return await db.Queryable<WfOutbox>()
            .Where(o => o.Id == claimed.Id)
            .Select(o => o.Status)
            .FirstAsync();
    }

    protected virtual async Task<WfOutboxDispatchResult> InvokeTransportAsync(
        WfOutbox claimed,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.DispatchAsync(ToMessage(claimed), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(
                ex,
                "工作流 outbox transport 出现未分类异常。OutboxId={OutboxId} MessageKey={MessageKey} ExceptionType={ExceptionType}",
                claimed.Id,
                claimed.MessageKey,
                ex.GetType().Name);

            var summary = WfNodeExecutionAttemptStore.Truncate(
                $"outbox transport 出现未分类异常(异常类型:{ex.GetType().Name})");
            return WfOutboxDispatchResult.RetryableFailure(
                WorkflowErrorCode.OutboxTransportUnhandled,
                summary);
        }
    }

    protected virtual async Task<bool> WriteBackAsync(
        WfOutbox claimed,
        WfOutboxDispatchResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        switch (result.Type)
        {
            case WfOutboxDispatchOutcome.Succeeded:
                return await WfOutboxConsumerStore.CompleteAsync(
                    db, claimed.Id, claimed.AttemptCount, nowUtc, cancellationToken);

            case WfOutboxDispatchOutcome.TerminalFailure:
                return await WfOutboxConsumerStore.FailAsync(
                    db, claimed.Id, claimed.AttemptCount, nowUtc, result.Summary, cancellationToken);

            case WfOutboxDispatchOutcome.RetryableFailure:
                var budgetExhausted = claimed.AttemptCount >= Math.Max(options.OutboxMaxAttempts, 1);
                if (budgetExhausted)
                {
                    return await WfOutboxConsumerStore.FailAsync(
                        db, claimed.Id, claimed.AttemptCount, nowUtc, result.Summary, cancellationToken);
                }

                var delay = WfOutboxConsumerStore.ResolveRetryDelay(claimed.AttemptCount, result.RetryAfter);
                return await WfOutboxConsumerStore.ScheduleRetryAsync(
                    db, claimed.Id, claimed.AttemptCount, nowUtc + delay, result.Summary, cancellationToken);

            default:
                throw WorkflowErrorCode.Exception(
                    WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["resultType"] = result.Type.ToString() });
        }
    }

    protected virtual WfOutboxMessage ToMessage(WfOutbox claimed) => new()
    {
        Id = claimed.Id,
        ExecutionId = claimed.ExecutionId,
        MessageType = claimed.MessageType,
        MessageKey = claimed.MessageKey,
        PayloadJson = claimed.PayloadJson,
        AttemptCount = claimed.AttemptCount,
    };
}
