using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 任务执行记录——一行 = 一次执行尝试(docs/scheduling-ledger.md §3.3)。
/// <para><see cref="EndTime"/> 为空 = 运行中(任务无 Running 态,全靠此行推导,§2.2);
/// 同一次触发的各次重试共享 <see cref="FireInstanceId"/>,靠它聚合。</para>
/// <para>基类 <see cref="AuditEntity"/>(物理删):只增日志,镜像 SysOpLog 语义,清理走硬删。
/// <b>永不落请求头</b>——header 常含密钥(§13-1)。</para>
/// </summary>
[SugarTable("sys_job_log", TableDescription = "任务执行记录")]
[SugarIndex("idx_sys_job_log", nameof(JobId), OrderByType.Asc, nameof(AuditEntity.CreateTime), OrderByType.Desc)]
public class SysJobLog : AuditEntity
{
    [SugarColumn(ColumnDescription = "任务 Id")]
    public long JobId { get; set; }

    /// <summary>任务名快照(任务删了记录仍可读)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "任务名快照")]
    public string JobName { get; set; } = "";

    /// <summary>一次触发的关联 Id(雪花);重试各占一行,靠它聚合</summary>
    [SugarColumn(ColumnDescription = "触发实例 Id(重试共享)")]
    public long FireInstanceId { get; set; }

    /// <summary>重试序号,0 = 首次</summary>
    [SugarColumn(ColumnDescription = "重试序号(0=首次)")]
    public int RetryIndex { get; set; }

    [SugarColumn(ColumnDescription = "触发来源:1=调度/2=手动/3=补跑/4=错过跳过")]
    public JobFireMode FireMode { get; set; } = JobFireMode.Schedule;

    /// <summary>计划触发时刻(整秒)</summary>
    [SugarColumn(ColumnDescription = "计划触发时刻(整秒)")]
    public DateTime ScheduledTime { get; set; }

    [SugarColumn(ColumnDescription = "开始时刻")]
    public DateTime StartTime { get; set; }

    /// <summary>结束时刻;<b>为空 = 运行中</b></summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "结束时刻(空=运行中)")]
    public DateTime? EndTime { get; set; }

    [SugarColumn(ColumnDescription = "结果:1=运行中/2=成功/3=失败/4=超时/5=取消/6=跳过")]
    public JobRunStatus RunStatus { get; set; } = JobRunStatus.Running;

    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long ElapsedMs { get; set; }

    /// <summary>执行节点(NodeName,{MachineName}#{WorkerId})</summary>
    [SugarColumn(Length = 128, ColumnDescription = "执行节点")]
    public string NodeName { get; set; } = "";

    /// <summary>
    /// 执行进程实例快照(与 <see cref="SysJobNode.InstanceId"/> 对应)。
    /// 孤儿回收按「节点名 + 实例 Id」判活:同名重启后旧实例的未闭合行会被回收,避免 SerialSkip 永久停摆。
    /// </summary>
    [SugarColumn(Length = 32, ColumnDescription = "执行进程实例快照")]
    public string NodeInstanceId { get; set; } = "";

    /// <summary>跨节点终止旗标:kill 端点置 true,执行侧每 KillPollSeconds 轮询自己这行(§5.4)</summary>
    [SugarColumn(ColumnDescription = "终止旗标(跨节点 kill)")]
    public bool KillRequested { get; set; }

    /// <summary>处理器输出(截 8KB;HTTP 响应体截 Http.MaxResponseLogBytes)</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true, ColumnDescription = "输出消息(截断)")]
    public string? MessageText { get; set; }

    /// <summary>失败异常信息(截 8KB)</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true, ColumnDescription = "异常信息(截断)")]
    public string? ErrorText { get; set; }
}
