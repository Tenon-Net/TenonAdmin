using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>投递尝试的种类。</summary>
public enum DeliveryAttemptKind
{
    /// <summary>发送</summary>
    Send = 0,

    /// <summary>查询对方结果</summary>
    Query = 1,

    /// <summary>中断恢复(租约过期后夺回)</summary>
    Recovery = 2,

    /// <summary>人工操作</summary>
    Manual = 3,

    /// <summary>回调确认</summary>
    Confirm = 4,

    /// <summary>投递器判定到期(截止时刻、受理确认时限、次数上限)而改变状态,没有发生调用</summary>
    Expired = 5,
}

/// <summary>投递尝试的触发方。</summary>
public enum DeliveryAttemptTrigger
{
    /// <summary>后台投递器</summary>
    Worker = 0,

    /// <summary>管理员</summary>
    Manual = 1,

    /// <summary>对方回调</summary>
    Callback = 2,
}

/// <summary>
/// 投递尝试记录(<c>itg_delivery_attempt</c>):每次发送、查询、恢复、人工操作与回调确认各追加一行,只增不改。
/// 迟到的结果(已被恢复或他人重新领取)同样只追加在这里,不覆盖投递记录的当前状态。
/// </summary>
[SugarTable("itg_delivery_attempt", TableDescription = "投递尝试记录")]
[SugarIndex("idx_itg_delivery_attempt_delivery", nameof(DeliveryId), OrderByType.Asc, nameof(Id), OrderByType.Asc)]
public class IntegrationDeliveryAttempt : AuditEntity
{
    [SugarColumn(ColumnDescription = "投递记录 Id")]
    public long DeliveryId { get; set; }

    /// <summary>发生时的发送序号(查询/恢复记当时的发送次数)。</summary>
    [SugarColumn(ColumnDescription = "发送序号")]
    public int AttemptNo { get; set; }

    [SugarColumn(ColumnDescription = "种类")]
    public DeliveryAttemptKind Kind { get; set; }

    [SugarColumn(ColumnDescription = "触发方")]
    public DeliveryAttemptTrigger Trigger { get; set; }

    /// <summary>人工操作人。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "操作人")]
    public long? OperatorId { get; set; }

    [SugarColumn(ColumnDescription = "开始时刻(UTC)")]
    public DateTime StartedAtUtc { get; set; }

    [SugarColumn(IsNullable = true, ColumnDescription = "结束时刻(UTC)")]
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>本次尝试的结论(发送/查询为调用结果分类;人工/确认为操作后的状态说明)。</summary>
    [SugarColumn(Length = 32, ColumnDescription = "结论")]
    public string Outcome { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDescription = "HTTP 状态码")]
    public int? HttpStatus { get; set; }

    /// <summary>出站调用 Id(可到出站调用记录里查同一次调用)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "出站调用 Id")]
    public string? CallId { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "错误摘要")]
    public string? ErrorSummary { get; set; }

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "说明")]
    public string? Note { get; set; }

    /// <summary>执行节点。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "执行节点")]
    public string? NodeName { get; set; }
}
