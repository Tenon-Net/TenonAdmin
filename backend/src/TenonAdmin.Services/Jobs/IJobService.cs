using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 定时任务管理服务(scheduling-ledger §8)。写操作一律在入库前校验触发配置与载荷
/// (cron 47003 / 触发 47004 / 属性包 47011 / HTTP 围栏 47009 / SQL 闸 47008),
/// 并算好首个 <c>NextRunTime</c>、发 <see cref="JobChangedEvent"/> 唤醒本进程调度循环。
/// </summary>
public interface IJobService
{
    /// <summary>分页查询任务(行含全列,编辑表单直接用行数据,故不设 GET /{id})。</summary>
    Task<PagedList<SysJob>> PageAsync(JobPageInput input);

    /// <summary>新增任务,Code 唯一(冲突抛 <see cref="ErrorCode.JobCodeExists"/>),返回新 Id。</summary>
    Task<long> AddAsync(JobInput input);

    /// <summary>更新任务(Code 不可变);触发配置变更即重算 NextRunTime。</summary>
    Task UpdateAsync(long id, JobInput input);

    /// <summary>删除任务(软删);内置任务抛 <see cref="ErrorCode.JobProtected"/>。</summary>
    Task DeleteAsync(long id);

    /// <summary>批量删除;命中内置任务则整批拒绝(<see cref="ErrorCode.JobProtected"/>)。</summary>
    Task DeleteBatchAsync(IReadOnlyCollection<long> ids);

    /// <summary>
    /// 启停一体(对齐前端 StatusSwitch):true = 恢复调度(重算 NextRunTime、清连败;
    /// 重算无未来时刻则维持 Completed 并抛 <see cref="ErrorCode.JobStatusConflict"/>);false = 暂停。
    /// </summary>
    Task SetEnabledAsync(long id, bool enabled);

    /// <summary>
    /// 执行一次:<b>在收到请求的副本本机执行</b>,不经选主、不做 CAS、不动 NextRunTime。
    /// 串行任务上次未结束抛 <see cref="ErrorCode.JobAlreadyRunning"/>。
    /// </summary>
    Task RunOnceAsync(long id);

    /// <summary>cron 预览(归一化 + 未来若干次;非法抛 <see cref="ErrorCode.JobCronInvalid"/>)。</summary>
    CronPreviewOutput PreviewCron(CronPreviewInput input);

    /// <summary>已注册的编译处理器清单(前端下拉数据源;内置三个也在内)。</summary>
    IReadOnlyList<string> ListHandlers();

    /// <summary>监控仪表盘聚合。</summary>
    Task<JobDashboardOutput> GetDashboardAsync();
}

/// <summary>执行记录服务:分页、终止、清空。</summary>
public interface IJobLogService
{
    /// <summary>分页查询执行记录(按开始时刻倒序)。</summary>
    Task<PagedList<SysJobLog>> PageAsync(JobLogPageInput input);

    /// <summary>
    /// 终止一次执行:目标行非运行中抛 <see cref="ErrorCode.JobRunNotAlive"/>;
    /// 写 KillRequested 旗标(跨节点)+ 取消本机在跑项(同节点即刻生效)。
    /// </summary>
    Task KillAsync(long logId);

    /// <summary>清空执行记录(硬删);未闭合的运行中记录一律保留。返回删除行数。</summary>
    Task<int> ClearAsync(JobLogClearInput input);
}
