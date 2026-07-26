using System.Collections.ObjectModel;

namespace TenonAdmin.Core;

/// <summary>
/// 一次执行尝试的只读快照(执行器构建、处理器消费,docs/scheduling-ledger.md §6)。
/// 同一次触发(FireInstanceId)内的各次重试各得一个新实例,仅 <see cref="RetryIndex"/> 不同。
/// </summary>
public sealed class JobExecutionContext
{
    /// <summary>任务 Id(<c>sys_job.Id</c>)</summary>
    public required long JobId { get; init; }

    /// <summary>任务编码(种子/日志/排障的稳定锚点)</summary>
    public required string JobCode { get; init; }

    /// <summary>任务显示名</summary>
    public required string JobName { get; init; }

    /// <summary>一次触发的关联 Id(雪花);重试共享同一值,执行记录靠它聚合</summary>
    public required long FireInstanceId { get; init; }

    /// <summary>重试序号,0 = 首次</summary>
    public int RetryIndex { get; init; }

    /// <summary>触发来源</summary>
    public JobFireMode FireMode { get; init; } = JobFireMode.Schedule;

    /// <summary>计划触发时刻(整秒,服务器本地时间)</summary>
    public required DateTime ScheduledTime { get; init; }

    /// <summary>实际开跑时刻</summary>
    public required DateTime FireTime { get; init; }

    /// <summary>属性包(<c>sys_job.PropsJson</c>)——处理器参数的唯一入口</summary>
    public IReadOnlyDictionary<string, string?> Properties { get; init; } = ReadOnlyDictionary<string, string?>.Empty;

    /// <summary>追加消息到本次执行记录的 MessageText(截断由执行器负责);未接线时为 null,处理器需判空</summary>
    public Action<string>? Log { get; init; }
}
