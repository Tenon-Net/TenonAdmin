using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 内置狗粮任务:清理过期执行记录 + 失联节点行(scheduling-ledger §7.3;种子见 <c>DefaultJobSeed</c>)。
/// 保留天数走配置中心 <c>sys.job.logRetentionDays</c>(默认 30,≤0 不清理);分批 500 防长事务。
/// 未闭合的行(EndTime 为空,进程崩溃残留)不删——它是"曾经跑过没收尾"的唯一证据。
/// </summary>
public class JobLogCleanupJob(ISqlSugarClient db, IConfigService config, TimeProvider time) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        var raw = await config.GetValueByKeyAsync(JobConfigKeys.KEY_LOG_RETENTION_DAYS);
        var days = int.TryParse(raw, out var d) ? d : 30;
        var now = JobTime.Truncate(time.GetLocalNow().DateTime);

        var removed = 0;
        if (days > 0)
        {
            var cutoff = now.AddDays(-days);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ids = await db.Queryable<SysJobLog>()
                    .Where(l => l.CreateTime < cutoff && l.EndTime != null)
                    .Take(500)
                    .Select(l => l.Id)
                    .ToListAsync();
                if (ids.Count == 0) break;
                removed += await db.Deleteable<SysJobLog>().In(ids).ExecuteCommandAsync();
                if (ids.Count < 500) break;
            }
        }

        var staleNodes = await db.Deleteable<SysJobNode>()
            .Where(n => n.LastHeartbeat < now.AddHours(-24))
            .ExecuteCommandAsync();

        context.Log?.Invoke($"清理执行记录 {removed} 行(保留 {days} 天),失联节点 {staleNodes} 行。");
    }
}
