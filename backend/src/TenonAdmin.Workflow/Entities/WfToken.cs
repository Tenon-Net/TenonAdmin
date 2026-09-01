using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 运行 token(<c>wf_token</c>)——实例在图上的执行指针。一期串行≈每实例 1 活跃 token;
/// 并行网关(M3)启用后多 token,<b>表结构先到位</b>。
/// </summary>
[SugarTable("wf_token", TableDescription = "流程运行 token")]
[SugarIndex("idx_wf_token_instance", nameof(InstanceId), OrderByType.Asc)]
[SugarIndex("idx_wf_token_instance_status", nameof(InstanceId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public class WfToken : BaseEntity
{
    [SugarColumn(ColumnDescription = "实例 Id")]
    public long InstanceId { get; set; }

    /// <summary>当前所在节点 Id(schema 内节点 id,非雪花)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "节点 Id")]
    public string NodeId { get; set; } = "";

    [SugarColumn(ColumnDescription = "状态(1 活跃 / 2 完成 / 3 取消)")]
    public WfTokenStatus Status { get; set; } = WfTokenStatus.Active;

    /// <summary>
    /// token 级乐观锁;每次状态推进经「期望状态 + 版本」双条件 CAS 递增
    /// (<see cref="WfExecutionContext.ClaimTokenAsync"/>)。
    /// <para><b>「状态推进」包括改 <see cref="NodeId"/>,不只是改 <see cref="Status"/></b>:token 换节点
    /// 就是这个实例往前(或往后)走了一步。正因为进节点也要领取,一次会推进 token 的同意才会与并发的
    /// 撤销互斥——两者都要 CAS 同一行,只有一个拿得到。</para>
    /// <para><c>DefaultValue = "0"</c> 的机制同 <see cref="WfInstance.Version"/>:它让 SqlSugar 走
    /// 「先加可空列 → 回填 → 改 NOT NULL」这条三步升级序列,<b>不是</b>在 ADD COLUMN 里拼 DEFAULT 子句;
    /// SQLite 因 <c>SqliteCodeFirstEnableDefaultValue</c> 未开启,DDL 里不会出现 DEFAULT,但回填 UPDATE
    /// 照旧执行。详见那一处的完整说明。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "乐观锁版本", DefaultValue = "0")]
    public int Version { get; set; }

    /// <summary>
    /// 本次「访问」标识(M3a-1):token 每次经 <see cref="EnterNodeOp.ExecuteAsync"/> 进入新节点时生成一次
    /// 雪花 Id(与 <see cref="NodeId"/> 同一条 UPDATE 落库),**停留期间不变**——会签未满票、转办、催办等
    /// 「留在原节点」的写路径都不经过 <see cref="EnterNodeOp"/>,只推进 <see cref="Version"/>,不动本列。
    /// <para><b>与 <see cref="Version"/> 职责不混用</b>:<see cref="Version"/> 是乐观锁,每次状态推进(含
    /// 停留期间的多次 CAS)都会前进;本列只回答「当前这次访问是哪一次」,同一次访问期间恒定,天然适合
    /// 拿来标注同一次访问建出的 <c>wf_task</c>/<c>wf_cc</c>/<c>wf_history</c> 行。</para>
    /// <para>可空、无默认值:与 <see cref="WfInstance.CompletedTime"/> 同型,升级路径走单步
    /// <c>ADD COLUMN ... NULL</c>,旧行读 <c>null</c>;下次进节点自然补上,永远停在某节点的老 token
    /// 保持 <c>null</c>——这是语义不是缺陷,不写回填 HostedService。</para>
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "节点访问 Id")]
    public long? NodeVisitId { get; set; }
}
