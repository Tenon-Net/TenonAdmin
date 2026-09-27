using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 雪花 WorkerId 数据库租约——防止多实例配了相同 WorkerId 导致同毫秒撞 Id(QA27)。
/// 每个启动的实例尝试插入/续租;另一个活跃实例已占用同号 → 启动抛异常。
/// <para>不继承 <see cref="BaseEntity"/>:无软删、无审计;生命周期完全由 <see cref="WorkerIdLeaseGuard"/> 管理。</para>
/// </summary>
[SugarTable("sys_worker_lease", TableDescription = "雪花 WorkerId 租约")]
[SugarIndex("uk_sys_worker_lease_wid", nameof(WorkerId), OrderByType.Asc, IsUnique = true)]
public class SysWorkerLease : PrimaryId
{
    [SugarColumn(ColumnDescription = "雪花机器号(0–63)")]
    public int WorkerId { get; set; }

    [SugarColumn(Length = 128, ColumnDescription = "节点名")]
    public string NodeName { get; set; } = "";

    [SugarColumn(ColumnDescription = "租约到期时刻")]
    public DateTime LeaseExpiresAt { get; set; }

    [SugarColumn(ColumnDescription = "进程 Id")]
    public int Pid { get; set; }

    /// <summary>
    /// 持有者主机名。单独成列而不是从 <see cref="NodeName"/> 里反解:
    /// <see cref="Pid"/> 只在同一台主机上才有可比性,接管判定要拿它做前置闸门。
    /// </summary>
    [SugarColumn(Length = 128, ColumnDescription = "持有者主机名", IsNullable = true)]
    public string? MachineName { get; set; }
}
