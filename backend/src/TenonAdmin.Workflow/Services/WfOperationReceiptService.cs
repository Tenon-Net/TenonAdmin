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

        // 占位 INSERT 圈进一个可单独回滚的点(仅 PostgreSQL 需要,见 UseNestedSavepoint)。
        var nested = await BeginNestedAsync(cancellationToken);
        try
        {
            await InsertPlaceholderAsync(identity, cancellationToken);
            if (nested)
                await ReleaseNestedAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 唯一索引冲突 = 同一 identity 的另一个请求刚提交。**不解析各库错误码**(四库方言不同,
            // 解析它是方言陷阱):再查一次,查到就是那个赢家的回执;查不到说明异常另有原因,原样抛。
            // PG 上这次 SELECT 之所以还能执行,全靠下面这一步先回滚到点(否则整事务已 aborted)。
            if (nested)
                await RollbackNestedAsync(cancellationToken);
            var winner = await FindAsync(identity.IdentityHash, cancellationToken);
            if (winner is not null)
                return winner;
            throw;
        }
    }

    /// <summary>
    /// 是否需要给占位 INSERT 套一个 SAVEPOINT ——<b>只有 PostgreSQL 需要,且只在显式事务里</b>。
    /// <para><b>为什么非要有这个方言分支</b>(它是内核 <c>src/</c> 里的第一个):PG 一旦有语句报错,就把
    /// <b>整个事务</b>置为 aborted,此后任何语句都只回
    /// <c>25P02 current transaction is aborted, commands ignored until end of transaction block</c>。
    /// 于是 <see cref="TryBeginAsync"/> 赖以「认赢家」的那次二次 <see cref="FindAsync"/> 在 PG 上根本执行不了,
    /// 而且那个新异常还会<b>顶替</b>原始的唯一冲突异常抛出去,连诊断线索一并丢掉。SQLite / MySQL / SqlServer
    /// 的语句级错误不中止事务,这两条语句对它们纯属多余;SqlServer 的语法本就不同(<c>SAVE TRANSACTION</c> /
    /// <c>ROLLBACK TRANSACTION</c>),写全等于替三个不需要它的方言各付一份代价。<b>这不是性能取舍——PG 的事务
    /// 中止语义没有可移植替代</b>,躲不掉的这一处,写出来比藏起来便宜。上面那句「不解析各库错误码」的决定仍然
    /// 有效并保留:本分支判的是<b>方言身份</b>,不是错误码。</para>
    /// <para><c>IsAnyTran</c> 守卫的原因:PG 的 <c>SAVEPOINT</c> 只能用在事务块里,自动提交模式下发它会直接
    /// 报错;而在那种模式下语句失败本就不会中止任何东西,也就不需要它。</para>
    /// </summary>
    protected virtual bool UseNestedSavepoint =>
        receipts.Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL && receipts.Db.Ado.IsAnyTran();

    /// <summary>savepoint 名;单事务内只会建一次(<see cref="TryBeginAsync"/> 每事务调一次)。</summary>
    protected const string NestedSavepointName = "wf_receipt_try";

    /// <summary>需要时建立嵌套点;返回值告诉调用方后面要不要配对地回滚/释放。</summary>
    protected virtual async Task<bool> BeginNestedAsync(CancellationToken cancellationToken)
    {
        if (!UseNestedSavepoint)
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        await receipts.Db.Ado.ExecuteCommandAsync($"SAVEPOINT {NestedSavepointName}");
        return true;
    }

    /// <summary>回滚到嵌套点:撤掉失败的那条 INSERT,把事务从 aborted 里救回来,后续语句照常。</summary>
    protected virtual Task RollbackNestedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return receipts.Db.Ado.ExecuteCommandAsync($"ROLLBACK TO SAVEPOINT {NestedSavepointName}");
    }

    /// <summary>成功路径释放嵌套点,不让它挂到事务结束(外层事务的提交/回滚语义不受影响)。</summary>
    protected virtual Task ReleaseNestedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return receipts.Db.Ado.ExecuteCommandAsync($"RELEASE SAVEPOINT {NestedSavepointName}");
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
        // `!`:SqlSugar 的 FirstAsync 标注成 Task<T> 非空,实际查不到就是 null(本方法的语义正是可空)。
        receipts.Db.Queryable<WfOperationReceipt>()
            .Where(r => r.IdentityHash == identityHash)
            .FirstAsync(cancellationToken)!;

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
