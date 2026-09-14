using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置 outbox 监控与死信重放。分页经 instance 的机构过滤器限制可见范围;
/// 重放与回执在同一短事务中提交,只接受 <see cref="WfOutboxStatus.Failed"/>。
/// </summary>
public class WfOutboxService(
    ISqlSugarClient db,
    IWfOperationReceiptService receipts,
    ICurrentUser currentUser,
    TimeProvider time) : IWfOutboxService
{
    public virtual async Task<PagedList<WfOutboxOutput>> PageAsync(
        WfOutboxPageInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        RequireActor();

        var status = input.Status ?? WfOutboxStatus.Failed;
        var messageType = string.IsNullOrWhiteSpace(input.MessageType) ? null : input.MessageType.Trim();
        var query = db.Queryable<WfOutbox>()
            .InnerJoin<WfNodeExecution>((o, e) => o.ExecutionId == e.Id)
            .InnerJoin<WfInstance>((o, e, i) => e.InstanceId == i.Id)
            .Where((o, e, i) => o.Status == status)
            .WhereIF(messageType is not null, (o, e, i) => o.MessageType == messageType)
            .OrderBy((o, e, i) => o.Id, OrderByType.Desc)
            .Select((o, e, i) => new WfOutboxOutput
            {
                Id = o.Id,
                ExecutionId = o.ExecutionId,
                InstanceId = i.Id,
                MessageType = o.MessageType,
                MessageKey = o.MessageKey,
                PayloadJson = o.PayloadJson,
                Status = o.Status,
                AttemptCount = o.AttemptCount,
                AvailableAtUtc = o.AvailableAtUtc,
                LastError = o.LastError,
                CompletedAtUtc = o.CompletedAtUtc,
            });

        return await query.ToPagedListAsync(input.Current, input.Size);
    }

    public virtual async Task<WfOutboxOutput> ReplayAsync(
        long id,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorUserId = RequireActor();
        var requestKey = WfWriteCmd.NormalizeRequestId(requestId)
            ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.RequestIdInvalid);

        var row = await RequireVisibleAsync(id, cancellationToken);
        var identity = WfOperationIdentity.Create(
            row.Execution.ScopeKey,
            WfCommandType.OutboxReplay,
            WfTargetType.Outbox,
            row.Outbox.Id,
            actorUserId,
            requestKey);

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var hit = await receipts.TryBeginAsync(identity, cancellationToken);
            if (hit is not null)
                return DeserializeReceipt(hit);

            var current = await db.Queryable<WfOutbox>()
                .Where(o => o.Id == id)
                .FirstAsync();
            if (current is null)
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.OutboxNotFound);
            if (current.Status != WfOutboxStatus.Failed)
            {
                throw WorkflowErrorCode.Exception(
                    WorkflowErrorCode.OutboxReplayNotAllowed,
                    new Dictionary<string, object?> { ["reason"] = "notFailed", ["status"] = current.Status });
            }

            if (!await WfOutboxConsumerStore.ReplayFailedAsync(db, id, nowUtc, cancellationToken))
            {
                throw WorkflowErrorCode.Exception(
                    WorkflowErrorCode.OutboxReplayNotAllowed,
                    new Dictionary<string, object?> { ["reason"] = "casLost", ["id"] = id });
            }

            var reloaded = await db.Queryable<WfOutbox>().Where(o => o.Id == id).FirstAsync()
                ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.OutboxNotFound);
            var output = ToOutput(reloaded, row.Execution.InstanceId);
            await receipts.CommitAsync(
                identity,
                0,
                JsonSerializer.Serialize(output, WfModelJson.Options),
                cancellationToken);
            return output;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        return tran.Data!;
    }

    protected virtual async Task<(WfOutbox Outbox, WfNodeExecution Execution)> RequireVisibleAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var outbox = await db.Queryable<WfOutbox>()
            .Where(o => o.Id == id)
            .FirstAsync();
        if (outbox is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OutboxNotFound);

        var execution = await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == outbox.ExecutionId)
            .FirstAsync();
        if (execution is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OutboxNotFound);

        var instance = await db.Queryable<WfInstance>()
            .Where(i => i.Id == execution.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OutboxNotFound);

        return (outbox, execution);
    }

    protected virtual WfOutboxOutput ToOutput(WfOutbox row, long instanceId) => new()
    {
        Id = row.Id,
        ExecutionId = row.ExecutionId,
        InstanceId = instanceId,
        MessageType = row.MessageType,
        MessageKey = row.MessageKey,
        PayloadJson = row.PayloadJson,
        Status = row.Status,
        AttemptCount = row.AttemptCount,
        AvailableAtUtc = row.AvailableAtUtc,
        LastError = row.LastError,
        CompletedAtUtc = row.CompletedAtUtc,
    };

    private static WfOutboxOutput DeserializeReceipt(WfOperationReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.ResultJson))
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.OperationFailed,
                new Dictionary<string, object?>
                {
                    ["reason"] = "receiptResultMissing",
                    ["identityHash"] = receipt.IdentityHash,
                });
        }

        try
        {
            return JsonSerializer.Deserialize<WfOutboxOutput>(receipt.ResultJson, WfModelJson.Options)
                ?? throw WorkflowErrorCode.Exception(
                    WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "receiptResultMissing",
                        ["identityHash"] = receipt.IdentityHash,
                    });
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.OperationFailed,
                new Dictionary<string, object?>
                {
                    ["reason"] = "receiptResultInvalid",
                    ["identityHash"] = receipt.IdentityHash,
                });
        }
    }

    private long RequireActor() =>
        currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);
}
