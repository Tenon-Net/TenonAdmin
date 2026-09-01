using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_history.Sequence</c> 的分配器(M3a-1)。逐行分配、无间隙、无重试循环:
/// <c>SET HistorySeq = HistorySeq + 1</c> 相对递增 + 读回,四库通用(仓内先例 <c>Services/Jobs/JobExecutor.cs</c>
/// 的同类写法),该 UPDATE 在四库上都会取行排他锁持有到提交;MySQL RR 下 UPDATE 走 current read,
/// 读回 SELECT 读到本事务自己的写。不用任何方言特有语法(不用 PG <c>RETURNING</c>、不用 SqlServer
/// <c>SET @v = col = col+1</c>)。
/// <para><b>必须在事务内才成立</b>——两条裸自动提交语句之间,并发的另一次分配会让读回值撞号。
/// <see cref="WfExecutionContext.AppendHistoryAsync"/> 天然在引擎「一条 Cmd 一个事务」里;绕开 ctx 的
/// 4 个系统写入路径(<see cref="WfTimeoutJob"/> ×3、<c>WfTaskService.UrgeAsync</c> ×1)今天是裸调用,
/// 统一走 <see cref="WriteSystemRowAsync"/> 自带的短事务。</para>
/// </summary>
internal static class WfHistorySequence
{
    public static async Task<int> NextAsync(ISqlSugarClient db, long instanceId)
    {
        await db.Updateable<WfInstance>()
            .SetColumns(i => new WfInstance { HistorySeq = i.HistorySeq + 1 })
            .Where(i => i.Id == instanceId)
            .ExecuteCommandAsync();

        // WfInstance 是 DataEntity(IOrgScoped 全局过滤器),后台系统路径漏了 ClearFilter 会读回 0 行——
        // 序号永远是 0。
        return await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .Select(i => i.HistorySeq)
            .FirstAsync();
    }

    /// <summary>
    /// 绕开 <see cref="WfExecutionContext"/> 的系统写入(超时 ×3、催办 ×1)专用:短事务包住「分配序号 + 插一行」。
    /// 事务不是装饰——两条裸自动提交语句之间,并发的另一次分配会让读回值撞号。
    /// </summary>
    public static async Task WriteSystemRowAsync(ISqlSugarClient db, WfHistory row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            row.Sequence = await NextAsync(db, row.InstanceId);
            await db.Insertable(row).ExecuteCommandAsync();
        });
        if (!tran.IsSuccess)
            throw tran.ErrorException!;
    }
}
