using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 出站调用记录(<c>itg_outbound_log</c>,实现契约 §6.3):目标、操作、地址(不含查询串)、结果、耗时与调用 Id。
/// <b>不记录请求/响应正文与任何请求头</b>(出站秘密只在请求头里);错误摘要截断到列宽,认证步骤的意外异常只记类型、不记消息(可能夹带秘密)。
/// 只增不改,按保留期清理。
/// </summary>
[SugarTable("itg_outbound_log", TableDescription = "出站调用记录")]
[SugarIndex("idx_itg_outbound_log_time", nameof(CreateTime), OrderByType.Desc)]
[SugarIndex("idx_itg_outbound_log_target", nameof(Target), OrderByType.Asc, nameof(CreateTime), OrderByType.Desc)]
[SugarIndex("idx_itg_outbound_log_delivery", nameof(DeliveryId), OrderByType.Asc)]
[SugarIndex("idx_itg_outbound_log_call", nameof(CallId), OrderByType.Asc)]   // 尝试记录深链 ?callId=
public class IntegrationOutboundLog : AuditEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "目标名")]
    public string Target { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "业务操作名")]
    public string? Operation { get; set; }

    [SugarColumn(Length = 16, ColumnDescription = "HTTP 方法")]
    public string HttpMethod { get; set; } = "";

    /// <summary>请求地址(scheme + host + path,不含查询串)。</summary>
    [SugarColumn(Length = 512, ColumnDescription = "请求地址(不含查询串)")]
    public string Url { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDescription = "HTTP 状态码")]
    public int? StatusCode { get; set; }

    [SugarColumn(ColumnDescription = "结果分类")]
    public OutboundOutcome Outcome { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "原因短码")]
    public string? Reason { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "错误摘要")]
    public string? ErrorSummary { get; set; }

    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long ElapsedMs { get; set; }

    /// <summary>调用 Id(随请求以 <c>X-Request-Id</c> 发给对方,双方对账用)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "调用 Id")]
    public string CallId { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "追踪标识")]
    public string? TraceId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "关联投递记录 Id")]
    public long? DeliveryId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "关联投递尝试序号")]
    public int? AttemptNo { get; set; }
}
