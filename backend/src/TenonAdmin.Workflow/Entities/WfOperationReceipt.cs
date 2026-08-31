using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 写操作幂等回执(<c>wf_operation_receipt</c>,数据库评审 §五)——解决「第一次事务已成功、但 HTTP 响应
/// 丢失,客户端重试只能拿到 <c>TaskConflict</c>」。回执与领域状态<b>同一事务</b>提交:业务回滚则回执不残留;
/// 相同 <see cref="IdentityHash"/> 的重试直接返回第一次的 <see cref="ResultJson"/>。
/// <para><b>与 <c>Version</c> CAS 互补而非替代</b>:CAS 解决「并发两个<b>不同</b>请求」,回执解决
/// 「同一个请求被重发」。</para>
/// <para><b>刻意继承 <see cref="BaseEntity"/> 而非 <c>DataEntity</c></b>:<c>DataEntity</c> 带
/// <c>IOrgScoped</c>,会吃全局数据范围过滤器(只作用于 SELECT)——数据范围窄的用户重试时可能<b>查不到自己
/// 刚写的回执</b>,幂等静默失效、状态被推进两次。机构维度改由本表显式非空的 <see cref="ScopeKey"/> 承载,
/// 也正是评审 §五「不要直接依赖包含 nullable <c>CreateOrgId</c> 的组合唯一索引」(各数据库对 NULL 的唯一性
/// 判定不一致)。<see cref="BaseEntity.IsDelete"/> 对本表永不置真:回执 append-only,与 <c>wf_history</c> 同。</para>
/// </summary>
[SugarTable("wf_operation_receipt", TableDescription = "流程写操作幂等回执")]
[SugarIndex("uk_wf_receipt_identity", nameof(IdentityHash), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_wf_receipt_target", nameof(TargetType), OrderByType.Asc, nameof(TargetId), OrderByType.Asc)]
public class WfOperationReceipt : BaseEntity
{
    /// <summary>
    /// 机构/租户范围键。非空——无归属机构的用户归一化为哨兵
    /// <see cref="WfIdentityHash.ScopeSentinel"/>,不允许 null 与空串产生两个 identity。
    /// </summary>
    [SugarColumn(Length = 64, ColumnDescription = "机构/租户范围键(无机构用哨兵)")]
    public string ScopeKey { get; set; } = "";

    [SugarColumn(ColumnDescription = "写命令类型")]
    public WfCommandType CommandType { get; set; }

    [SugarColumn(ColumnDescription = "目标类型(实例/待办/定义版本)")]
    public WfTargetType TargetType { get; set; }

    /// <summary>目标 Id:实例 Id / 待办 Id / 定义版本 Id(<see cref="WfCommandType.Start"/>)。</summary>
    [SugarColumn(ColumnDescription = "目标 Id")]
    public long TargetId { get; set; }

    [SugarColumn(ColumnDescription = "操作者用户 Id")]
    public long ActorUserId { get; set; }

    /// <summary>客户端提交的 request key(<c>RequestId</c>);一次用户动作内复用,重试同 key。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "客户端 request key")]
    public string RequestKey { get; set; } = "";

    /// <summary>
    /// 上述六个维度规范化后的 SHA-256 小写十六进制(<see cref="WfIdentityHash.Compute"/>);
    /// 唯一索引建在本列。组成字段一并保留只为排查,不参与唯一性。
    /// </summary>
    [SugarColumn(Length = 64, ColumnDescription = "幂等标识哈希(SHA-256 小写 hex)")]
    public string IdentityHash { get; set; } = "";

    /// <summary>首次执行的结果码;<c>0</c> = 成功。</summary>
    [SugarColumn(ColumnDescription = "首次执行结果码(0=成功)")]
    public int ResultCode { get; set; }

    /// <summary>首次执行的 <see cref="WfEngineResult"/> 序列化快照;重试原样返回。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "首次执行结果 JSON")]
    public string? ResultJson { get; set; }
}
