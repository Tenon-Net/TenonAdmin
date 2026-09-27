namespace TenonAdmin.Workflow;

/// <summary>
/// 写操作幂等回执的持久化 SPI(设计规划 §14.2、数据库评审 §五)。消费者前置注册同接口即可整体替换。
/// <para><b>两个方法都必须跑在调用方已开启的事务里</b> —— 引擎在 <c>UseTranAsync</c> 内先
/// <see cref="TryBeginAsync"/> 占位、跑完领域操作再 <see cref="CommitAsync"/> 回填。业务抛错时整事务回滚,
/// 占位行随之消失,重试可以重来;<b>实现里绝不能自己开事务</b>,否则占位行会提前提交、回滚不掉。</para>
/// </summary>
public interface IWfOperationReceiptService
{
    /// <summary>
    /// 占位或命中:库里已有同 <see cref="WfOperationIdentity.IdentityHash"/> 的回执则原样返回(调用方据此
    /// <b>短路</b>,返回第一次的结果而不是再推进一次状态);没有则插入占位行并返回 <c>null</c>。
    /// </summary>
    Task<WfOperationReceipt?> TryBeginAsync(
        WfOperationIdentity identity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 领域操作成功后回填占位行的结果。<b>只更新、不新增</b>。
    /// 业务失败不调用本方法——失败随事务回滚,不落回执(见 <c>## 语义契约</c>)。
    /// </summary>
    Task CommitAsync(
        WfOperationIdentity identity,
        int resultCode,
        string? resultJson,
        CancellationToken cancellationToken = default);
}
