using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 定时任务端点(scheduling-ledger §8):任务 CRUD + 启停 + 执行一次 + cron 预览 + 处理器清单 +
/// 执行记录(分页/终止/清空)+ 监控仪表盘。
/// <para>全部 <c>[RolePermission]</c>(权限码 = 路由),唯一例外是 <c>preview-cron</c>——
/// 表单里人人要用、不值得单独授权,故走 <c>[ActiveSession]</c>(任何登录用户)。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/job")]
[Module("Job")]   // 可经 Api:DisabledModules=["Job"] 整模块下线
public class JobController(IJobService jobService, IJobLogService jobLogService) : ControllerBase
{
    /// <summary>分页查询任务(行含全列,编辑表单直接用行数据)</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<SysJob>>> Page([FromQuery] JobPageInput input) =>
        Result<PagedList<SysJob>>.Ok(await jobService.PageAsync(input));

    /// <summary>新增任务</summary>
    [HttpPost("")]
    [RolePermission]
    [OperationLog("新增定时任务")]
    public async Task<Result<long>> Add(JobInput input) =>
        Result<long>.Ok(await jobService.AddAsync(input));

    /// <summary>更新任务(Code 创建后不可变;触发配置变更即重算下次执行时刻)</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新定时任务")]
    public async Task<Result<bool>> Update(long id, JobInput input)
    {
        await jobService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除任务(软删,可回收站恢复;内置任务禁删)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除定时任务")]
    public async Task<Result<bool>> Delete(long id)
    {
        await jobService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>批量删除任务(命中内置任务则整批拒绝)</summary>
    [HttpPost("batch-delete")]
    [RolePermission]
    [OperationLog("批量删除定时任务")]
    public async Task<Result<bool>> BatchDelete(BatchDeleteInput input)
    {
        await jobService.DeleteBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }

    /// <summary>启停任务(true=恢复调度并重算下次执行时刻,false=暂停)</summary>
    [HttpPut("{id}/enabled")]
    [RolePermission]
    [OperationLog("启停定时任务")]
    public async Task<Result<bool>> SetEnabled(long id, [FromQuery] bool enabled)
    {
        await jobService.SetEnabledAsync(id, enabled);
        return Result<bool>.Ok(true);
    }

    /// <summary>执行一次(在收到请求的副本本机执行,不经选主、不影响调度节奏)</summary>
    [HttpPost("{id}/run")]
    [RolePermission]
    [OperationLog("手动执行定时任务")]
    public async Task<Result<bool>> RunOnce(long id)
    {
        await jobService.RunOnceAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>cron 预览(归一化 + 未来若干次)。POST 而非 GET:cron 含 <c>? #</c>,走 query 有转义坑</summary>
    [HttpPost("preview-cron")]
    [ActiveSession]
    public Result<CronPreviewOutput> PreviewCron(CronPreviewInput input) =>
        Result<CronPreviewOutput>.Ok(jobService.PreviewCron(input));

    /// <summary>已注册的编译处理器清单(前端下拉数据源,免手打 HandlerName)</summary>
    [HttpGet("handlers")]
    [RolePermission]
    public Result<IReadOnlyList<string>> Handlers() =>
        Result<IReadOnlyList<string>>.Ok(jobService.ListHandlers());

    /// <summary>分页查询执行记录</summary>
    [HttpGet("log/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysJobLog>>> LogPage([FromQuery] JobLogPageInput input) =>
        Result<PagedList<SysJobLog>>.Ok(await jobLogService.PageAsync(input));

    /// <summary>终止一次执行(跨节点:写终止旗标,目标节点最迟 KillPollSeconds 后停)</summary>
    [HttpPost("log/{id}/kill")]
    [RolePermission]
    [OperationLog("终止任务执行")]
    public async Task<Result<bool>> KillRun(long id)
    {
        await jobLogService.KillAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>清空执行记录(硬删;运行中的记录一律保留)</summary>
    [HttpPost("log/clear")]
    [RolePermission]
    [OperationLog("清空任务执行记录")]
    public async Task<Result<int>> ClearLogs(JobLogClearInput input) =>
        Result<int>.Ok(await jobLogService.ClearAsync(input));

    /// <summary>监控仪表盘(今日成败/在飞/状态分布/近 14 日趋势/即将执行/集群节点)</summary>
    [HttpGet("dashboard")]
    [RolePermission]
    public async Task<Result<JobDashboardOutput>> Dashboard() =>
        Result<JobDashboardOutput>.Ok(await jobService.GetDashboardAsync());
}
