using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 调度选主锁——<b>恒一行</b>(Id=1),启动时幂等 ensure-insert(docs/scheduling-ledger.md §3.4/§5.2)。
/// 租约只回答「谁来扫表」(效率);防双发的正确性由任务行 NextRunTime 的领取 CAS 独立保证,
/// 租约烂掉也不会双发。全部操作是参数化 <c>UPDATE ... WHERE</c> 按影响行数判定,四库通吃。
/// <para>基类 <see cref="PrimaryId"/>:单行基础设施,不要审计四件套;不走 IRepository(约束 AuditEntity),
/// 经 ISqlSugarClient 直写。</para>
/// </summary>
[SugarTable("sys_job_lock", TableDescription = "调度选主锁(单行)")]
public class SysJobLock : PrimaryId
{
    /// <summary>锁行的固定主键(恒 1)</summary>
    public const long SingletonId = 1;

    /// <summary>当前主节点名({MachineName}#{WorkerId})</summary>
    [SugarColumn(Length = 128, ColumnDescription = "当前主节点名")]
    public string OwnerNodeName { get; set; } = "";

    /// <summary>租约到期时刻(整秒);过期未续即可被备节点夺取</summary>
    [SugarColumn(ColumnDescription = "租约到期时刻")]
    public DateTime LeaseUntil { get; set; }

    /// <summary>第几任主——纯诊断,不承担任何正确性判定(fencing 靠领取 CAS,不靠 Term)</summary>
    [SugarColumn(ColumnDescription = "任期号(诊断用)")]
    public long Term { get; set; }
}
