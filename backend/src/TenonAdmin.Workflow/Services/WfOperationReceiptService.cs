using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置回执服务。读写都走 <see cref="IRepository{TEntity}.Db"/>(同一个 <c>SqlSugarScope</c> 单例),
/// 因此自动落在调用方 <c>UseTranAsync</c> 的事务里 —— 与领域状态同生共死。
/// 方法拆成 <c>virtual</c> 小步,消费者可继承覆写单步。
/// </summary>
public class WfOperationReceiptService(IRepository<WfOperationReceipt> receipts) : IWfOperationReceiptService
{
    /// <inheritdoc />
    public virtual async Task<WfOperationReceipt?> TryBeginAsync(
        WfOperationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await FindAsync(identity.IdentityHash, cancellationToken);
        if (existing is not null)
            return existing;

        try
        {
            await InsertPlaceholderAsync(identity, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 唯一索引冲突 = 同一 identity 的另一个请求刚提交。**不解析各库错误码**(四库方言不同,
            // 解析它是方言陷阱):再查一次,查到就是那个赢家的回执;查不到说明异常另有原因,原样抛。
            var winner = await FindAsync(identity.IdentityHash, cancellationToken);
            if (winner is not null)
                return winner;
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task CommitAsync(
        WfOperationIdentity identity,
        int resultCode,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var affected = await receipts.Db.Updateable<WfOperationReceipt>()
            .SetColumns(r => new WfOperationReceipt { ResultCode = resultCode, ResultJson = resultJson })
            .Where(r => r.IdentityHash == identity.IdentityHash)
            .ExecuteCommandAsync(cancellationToken);

        // 0 行 = 占位行不见了(调用方没先 TryBeginAsync,或另开了事务把占位提前提交/回滚掉)。
        // 这里**必须抛**:静默放过会留下一条 ResultJson 为空的回执,而它命中的下一次重试会拿到
        // 「有回执、但结果是空」的自相矛盾状态 —— 幂等在最不该出错的地方悄悄坏掉。抛出后整事务回滚。
        if (affected != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed, new Dictionary<string, object?>
            {
                ["reason"] = "receiptPlaceholderMissing",
                ["identityHash"] = identity.IdentityHash,
                ["affected"] = affected,
            });
        }
    }

    /// <summary>按 hash 查回执;查不到返回 <c>null</c>。</summary>
    protected virtual Task<WfOperationReceipt?> FindAsync(
        string identityHash,
        CancellationToken cancellationToken) =>
        receipts.Db.Queryable<WfOperationReceipt>()
            .Where(r => r.IdentityHash == identityHash)
            .FirstAsync(cancellationToken);

    /// <summary>插入占位行(结果列留空,等 <see cref="CommitAsync"/> 回填)。</summary>
    protected virtual Task<int> InsertPlaceholderAsync(
        WfOperationIdentity identity,
        CancellationToken cancellationToken) =>
        receipts.Db.Insertable(new WfOperationReceipt
        {
            ScopeKey = identity.ScopeKey,
            CommandType = identity.CommandType,
            TargetType = identity.TargetType,
            TargetId = identity.TargetId,
            ActorUserId = identity.ActorUserId,
            RequestKey = identity.RequestKey,
            IdentityHash = identity.IdentityHash,
        }).ExecuteCommandAsync(cancellationToken);
}
