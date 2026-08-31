using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 升级回填:把 <see cref="WfInstance.CompletedTime"/> 加列<b>之前</b>就已终态的旧实例,从它们的
/// <see cref="WfHistoryEventType.InstanceCompleted"/> 事件时间补齐(数据库评审 §九 #4)。
/// <para><b>为什么是自动跑而不是「文档里的一条手工升级命令」</b>:内核卖点是三行 <c>Program.cs</c>,
/// 手工步骤等于不会被执行,那列就永远是空的 —— 等于没做。</para>
/// <para><b>存在性守卫兜住注册顺序</b>:<c>AddTenonAdminWorkflow</c> 通常在 <c>AddTenonAdmin</c> 之前调用,
/// 本服务因此会排在内核的建表 <c>DatabaseInitializer</c> <b>前面</b>启动。全新库:表不存在 → 跳过
/// (本就没有旧行要回填)。升级库:首次启动时列还没加 → 跳过,同一次启动里建表器补上列 →
/// <b>下次重启自愈</b>。用「晚一次重启」换掉「跨包去改内核 HostedService 顺序」的耦合,划算。</para>
/// <para><b>幂等</b>:条件恒含 <c>CompletedTime == null</c>,跑第二遍 0 行;没有 <c>InstanceCompleted</c>
/// 事件可依据的旧行<b>保持空</b>(评审 §九 #4 的原话:无法确定时保持空)。</para>
/// </summary>
internal sealed class WfCompletedTimeBackfill(
    ISqlSugarClient db,
    ILogger<WfCompletedTimeBackfill> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // isCache:false —— 元数据缓存可能掩盖「表/列其实不存在」(同 DatabaseInitializer 的守卫写法)。
        var table = db.EntityMaintenance.GetTableName<WfInstance>();
        if (!db.DbMaintenance.IsAnyTable(table, false)) return;

        var hasColumn = db.DbMaintenance.GetColumnInfosByTableName(table, false)
            .Any(c => string.Equals(c.DbColumnName, nameof(WfInstance.CompletedTime), StringComparison.OrdinalIgnoreCase));
        if (!hasColumn) return;

        var filled = await BackfillAsync(cancellationToken);
        if (filled > 0)
            logger.LogInformation("工作流升级回填:{Count} 条旧终态实例的完结时间已按 InstanceCompleted 事件补齐", filled);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 两步 provider-neutral 回填:先查「终态 + 完结时间为空」的实例及其最早一条 <c>InstanceCompleted</c>
    /// 事件时间,再逐条条件更新。<b>不写 <c>UPDATE ... FROM</c></b> —— 四库语法各不相同。
    /// <para><b>必须用 <c>SetColumns</c> 条件更新</b>:它走不到只认 <c>UpdateByObject</c> 的审计 AOP,
    /// 于是回填不会把 <c>UpdateTime</c>/<c>UpdateUserId</c> 刷成启动时刻 —— 回填不是一次业务更新,
    /// 把审计字段改成「升级那一刻某个人改的」就是伪造审计。</para>
    /// </summary>
    private async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        // 回填是系统上下文,机构维度不设限(DataScopeContext 无请求时本就是 Unrestricted;
        // 显式 ClearFilter 是引擎既有惯例,免得将来有人在请求上下文里调它而静默少回填)。
        var candidates = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .InnerJoin<WfHistory>((i, h) => i.Id == h.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .Where((i, h) => i.Status != WfInstanceStatus.Running && i.CompletedTime == null)
            .GroupBy((i, h) => i.Id)
            .Select((i, h) => new { InstanceId = i.Id, CompletedTime = SqlFunc.AggregateMin(h.CreateTime) })
            .ToListAsync(cancellationToken);

        var filled = 0;
        foreach (var row in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = row.CompletedTime;
            filled += await db.Updateable<WfInstance>()
                .SetColumns(i => new WfInstance { CompletedTime = completed })
                .Where(i => i.Id == row.InstanceId && i.CompletedTime == null)
                .ExecuteCommandAsync();
        }

        return filled;
    }
}
