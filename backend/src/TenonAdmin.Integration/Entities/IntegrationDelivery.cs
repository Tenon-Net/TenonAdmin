using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>可靠投递状态(实现契约 §8.1)。</summary>
public enum DeliveryStatus
{
    /// <summary>待处理(含退避等待、待校验后发送)</summary>
    Pending = 0,

    /// <summary>已领取、调用进行中(租约内)</summary>
    Dispatching = 1,

    /// <summary>远端已受理,等待最终确认</summary>
    AwaitingConfirmation = 2,

    /// <summary>远端确认成功(终态)</summary>
    Succeeded = 3,

    /// <summary>结果未知且不可安全重试,待人工核对</summary>
    NeedsReconciliation = 4,

    /// <summary>次数或时间上限耗尽,待人工处理</summary>
    Exhausted = 5,

    /// <summary>确定性失败(业务拒绝、认证失败),待人工处理</summary>
    Failed = 6,

    /// <summary>人工关闭(终态)</summary>
    Cancelled = 7,
}

/// <summary>
/// 可靠投递记录(<c>itg_delivery</c>,实现契约 §7–§8)。由 <see cref="IDeliveryOutbox.EnqueueAsync"/> 在调用方的业务事务内写入,
/// 与业务数据同库同事务提交或回滚;之后由后台投递器领取、调用适配器并回写。
/// <para><see cref="DeliveryKey"/> 是稳定的逻辑投递标识,也是发给对方的幂等标识:全生命周期(含全部自动与人工尝试)不变。
/// 载荷只放业务数据,<b>不放任何认证秘密</b>——凭据在每次投递时从凭据来源取得。</para>
/// <para>不继承 <c>DataEntity</c>:后台任务没有请求级数据范围;管理访问由路由权限控制。业务时间列一律 UTC(<c>*Utc</c>)。</para>
/// </summary>
[SugarTable("itg_delivery", TableDescription = "可靠投递记录")]
[SugarIndex("uk_itg_delivery_key", nameof(DeliveryKey), OrderByType.Asc, IsUnique = true)]
[SugarIndex("idx_itg_delivery_due", nameof(Status), OrderByType.Asc, nameof(NextAttemptAtUtc), OrderByType.Asc)]
[SugarIndex("idx_itg_delivery_adapter", nameof(Adapter), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("idx_itg_delivery_completed", nameof(CompletedAtUtc), OrderByType.Asc)]
[SugarIndex("idx_itg_delivery_business", nameof(BusinessKey), OrderByType.Asc)]   // 业务页深链 ?businessKey=
public class IntegrationDelivery : AuditEntity
{
    /// <summary>逻辑投递标识 / 幂等标识(唯一,全生命周期不变)。</summary>
    [SugarColumn(Length = 128, ColumnDescription = "投递标识(幂等键,唯一)")]
    public string DeliveryKey { get; set; } = "";

    /// <summary>目标适配器名。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "适配器")]
    public string Adapter { get; set; } = "";

    /// <summary>业务操作名(如 <c>ticket.create</c>)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "业务操作")]
    public string Operation { get; set; } = "";

    /// <summary>业务键(便于从业务数据反查,如工单 Id)。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "业务键")]
    public string? BusinessKey { get; set; }

    /// <summary>业务载荷 JSON(规范化的紧凑形式)。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, ColumnDescription = "业务载荷 JSON")]
    public string PayloadJson { get; set; } = "";

    /// <summary>载荷摘要(SHA-256 hex),用于判定重复入队是否同一内容。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "载荷摘要")]
    public string PayloadHash { get; set; } = "";

    [SugarColumn(ColumnDescription = "状态")]
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>并发栅栏:每次领取、恢复、人工操作都递增,回写以它做 CAS。</summary>
    [SugarColumn(ColumnDescription = "并发栅栏")]
    public long Fence { get; set; }

    /// <summary>真实发送次数(不含查询与恢复)。</summary>
    [SugarColumn(ColumnDescription = "发送次数")]
    public int AttemptCount { get; set; }

    /// <summary>本轮预算起点(人工重试重置预算时记录当时的发送次数)。</summary>
    [SugarColumn(ColumnDescription = "预算起点")]
    public int BudgetStartAttempt { get; set; }

    /// <summary>本记录的发送次数上限(自预算起点起计)。</summary>
    [SugarColumn(ColumnDescription = "发送次数上限")]
    public int MaxAttempts { get; set; }

    /// <summary>下次可处理时刻(UTC)。</summary>
    [SugarColumn(ColumnDescription = "下次处理时刻(UTC)")]
    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>租约到期时刻(UTC,处理中时有值)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "租约到期(UTC)")]
    public DateTime? LeaseUntilUtc { get; set; }

    /// <summary>持有租约的节点。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "租约持有节点")]
    public string? LeaseOwner { get; set; }

    /// <summary>自动处理截止时刻(UTC),超过即耗尽。</summary>
    [SugarColumn(ColumnDescription = "截止时刻(UTC)")]
    public DateTime DeadlineAtUtc { get; set; }

    /// <summary>异步受理的确认截止时刻(UTC)。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "受理确认截止(UTC)")]
    public DateTime? ConfirmDeadlineAtUtc { get; set; }

    /// <summary>下次发送前须先查询对方(上次结果未知且对方可查询)。</summary>
    [SugarColumn(ColumnDescription = "发送前先查询")]
    public bool VerifyBeforeSend { get; set; }

    /// <summary>最近一次调用的结果分类。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最近结果")]
    public OutboundOutcome? LastOutcome { get; set; }

    /// <summary>最近一次错误摘要(已截断、不含秘密)。</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "最近错误")]
    public string? LastError { get; set; }

    /// <summary>对方返回的引用(如对方单号)。</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "对方引用")]
    public string? RemoteReference { get; set; }

    /// <summary>进入终态(成功/取消)的时刻(UTC),保留清理按它计算。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "完成时刻(UTC)")]
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>最近一次人工处理的操作人。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "人工处理人")]
    public long? ResolvedBy { get; set; }

    /// <summary>最近一次人工处理说明。</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "人工处理说明")]
    public string? ResolutionNote { get; set; }

    /// <summary>逐字段浅拷贝(以后新增的列自动包含)。</summary>
    internal IntegrationDelivery ShallowCopy() => (IntegrationDelivery)MemberwiseClone();
}
