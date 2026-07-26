using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 调度节点注册表——每个参与调度的进程(含备节点、Worker)每次心跳 upsert 自己一行(docs/scheduling-ledger.md §3.4)。
/// <para><b>不落 IsLeader 列</b>:谁是主由查询时与 <see cref="SysJobLock"/> 行比对得出,避免双写不一致。
/// 心跳超 24h 的陈尸行由 JobLogCleanupJob 顺手清(§7.3)。Id 由代码显式赋雪花(PrimaryId 不种子化)。</para>
/// </summary>
[SugarTable("sys_job_node", TableDescription = "调度节点注册表")]
[SugarIndex("uk_sys_job_node_name", nameof(NodeName), OrderByType.Asc, IsUnique = true)]
public class SysJobNode : PrimaryId
{
    /// <summary>节点名(唯一;默认 {MachineName}#{WorkerId})</summary>
    [SugarColumn(Length = 128, ColumnDescription = "节点名(唯一)")]
    public string NodeName { get; set; } = "";

    [SugarColumn(Length = 128, ColumnDescription = "主机名")]
    public string HostName { get; set; } = "";

    [SugarColumn(ColumnDescription = "进程 Id")]
    public int Pid { get; set; }

    /// <summary>雪花机器号(TenonAdmin:Id:WorkerId;监控页展示,排查同号碰撞)</summary>
    [SugarColumn(ColumnDescription = "雪花机器号")]
    public int WorkerId { get; set; }

    [SugarColumn(ColumnDescription = "进程启动时刻")]
    public DateTime StartTime { get; set; }

    [SugarColumn(ColumnDescription = "最后心跳时刻")]
    public DateTime LastHeartbeat { get; set; }
}
