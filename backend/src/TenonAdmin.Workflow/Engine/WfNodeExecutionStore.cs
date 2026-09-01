using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_node_execution</c> 的占位 + 领取(M3a-1 Task 3)。<c>public</c> 而不是 <c>internal</c>——全仓无
/// <c>InternalsVisibleTo</c>,<see cref="WfIdentityHash"/> 同为 <c>public static</c>。<b>零 DI 注册</b>,
/// 调用方(Task 6 的调度器)直接经 <c>ISqlSugarClient</c> 调用。
/// <para><b>本轮不做唯一冲突的认赢家恢复</b>——那是 Task 6 的活,PG 上需要 savepoint,抄
/// <see cref="WfOperationReceiptService.BeginNestedAsync"/>/<see cref="WfOperationReceiptService.RollbackNestedAsync"/>
/// 的姿势。<see cref="EnsureAsync"/> 本轮先查后插,查不到就插——插入撞了唯一键就让异常原样抛出去,
/// <b>不写 try/catch</b>:半吊子的 catch 在 PG 上会更糟(事务已 aborted,<c>25P02</c>)。</para>
/// </summary>
public static class WfNodeExecutionStore
{
    /// <summary>
    /// 按 <see cref="WfNodeExecution.ExecutionKey"/> 幂等 ensure-insert:已存在则原样返回既有行,
    /// 否则插入 <paramref name="row"/>(Id 由审计 AOP 填雪花)并返回它。
    /// </summary>
    public static async Task<WfNodeExecution> EnsureAsync(
        ISqlSugarClient db,
        WfNodeExecution row,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await db.Queryable<WfNodeExecution>()
            .Where(e => e.ExecutionKey == row.ExecutionKey)
            .FirstAsync();
        if (existing is not null) return existing;

        await db.Insertable(row).ExecuteCommandAsync();
        return row;
    }

    /// <summary>
    /// 领取:条件 <c>UPDATE ... WHERE</c> + 影响行数判定,四库通用——无 <c>RETURNING</c>、无
    /// <c>SET @v = col = col+1</c>、无 <c>FOR UPDATE SKIP LOCKED</c>、无数据库时间函数。
    /// 可领取的三种前提:<see cref="WfNodeExecutionStatus.Pending"/>;
    /// <see cref="WfNodeExecutionStatus.RetryScheduled"/> 且 <c>NextRetryAtUtc &lt;= nowUtc</c>;
    /// <see cref="WfNodeExecutionStatus.Running"/> 且租约已过期(<c>LeaseExpiresAtUtc &lt; nowUtc</c>,
    /// Fence + 1 挡住老 owner 的迟到回写)。
    /// <para>影响行数 <c>1</c> = 领到,返回读回后的行;<c>0</c> = 不可领,返回 <c>null</c>(不抛异常——与
    /// <c>ClaimInstanceAsync</c> 抛 48004 的差别是有意的:那里是用户请求撞车必须让用户看见,这里是 worker
    /// 空跑一拍)。</para>
    /// <para><b>必须在事务内才成立</b>——领取 UPDATE 与读回 SELECT 之间,裸自动提交会被并发的另一次领取
    /// 插一脚,读回值可能已经不是本次领取的结果(与 <see cref="WfHistorySequence.NextAsync"/> 同款约束)。
    /// 调用方(Task 6)负责起事务。</para>
    /// <para><paramref name="nowUtc"/>/<paramref name="leaseDuration"/> 由调用方传入(应用时间),不在
    /// <c>SetColumns</c> 里内联 <c>DateTime</c> 表达式——SqlSugar 会按当前区域把内联表达式格式化成字面量拼进
    /// SQL,zh-CN 下会炸出 <c>near "下午"</c>。</para>
    /// </summary>
    public static async Task<WfNodeExecution?> ClaimAsync(
        ISqlSugarClient db,
        long executionId,
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var leaseUntilUtc = nowUtc + leaseDuration;

        var claimed = await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution
            {
                Status = WfNodeExecutionStatus.Running,
                LeaseOwner = owner,
                LeaseExpiresAtUtc = leaseUntilUtc,
                Fence = e.Fence + 1,
                AttemptCount = e.AttemptCount + 1,
            })
            .Where(e => e.Id == executionId)
            .Where(e => e.Status == WfNodeExecutionStatus.Pending
                     || (e.Status == WfNodeExecutionStatus.RetryScheduled && e.NextRetryAtUtc <= nowUtc)
                     || (e.Status == WfNodeExecutionStatus.Running && e.LeaseExpiresAtUtc < nowUtc))
            .ExecuteCommandAsync();
        if (claimed != 1) return null;

        // 本表非 IOrgScoped(BaseEntity),读回无需 ClearFilter。
        return await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == executionId)
            .FirstAsync();
    }
}
