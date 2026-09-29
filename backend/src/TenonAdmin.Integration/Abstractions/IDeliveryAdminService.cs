using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>人工操作名(与管理接口路由末段一致)。可用性由服务端按状态、适配器能力与尝试历史计算,执行前复检。</summary>
public static class DeliveryActions
{
    /// <summary>重置预算后重新发送(失败;或本轮尝试都属于可安全重试路径的耗尽)。</summary>
    public const string Retry = "retry";

    /// <summary>立即查询对方结果并按结果迁移(适配器可查询时)。</summary>
    public const string Query = "query";

    /// <summary>人工确认对方已成功(待核对、待确认、耗尽)。</summary>
    public const string ConfirmSucceeded = "confirm-succeeded";

    /// <summary>人工确认对方未执行(待核对)→ 重新进入待处理,之后按常规规则发送。</summary>
    public const string ConfirmNotExecuted = "confirm-not-executed";

    /// <summary>关闭(非处理中、非终态)。</summary>
    public const string Cancel = "cancel";
}

/// <summary>投递记录分页入参(全部可选;时间按入队时间,本地时间口径与内核日志一致)。</summary>
public record DeliveryPageInput : PageInputBase
{
    public DeliveryStatus? Status { get; init; }
    public string? Adapter { get; init; }

    /// <summary>业务操作名关键字(模糊)。</summary>
    public string? Operation { get; init; }

    /// <summary>投递标识(精确)。</summary>
    public string? DeliveryKey { get; init; }

    /// <summary>业务键(精确)。</summary>
    public string? BusinessKey { get; init; }

    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
}

/// <summary>投递记录列表项。</summary>
public record DeliveryListItem
{
    public long Id { get; init; }
    public string DeliveryKey { get; init; } = "";
    public string Adapter { get; init; } = "";
    public string Operation { get; init; } = "";
    public string? BusinessKey { get; init; }
    public DeliveryStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }

    /// <summary>本轮预算内已发送次数(人工重试会重置预算)。</summary>
    public int AttemptsInBudget { get; init; }

    public DateTimeOffset? NextAttemptAt { get; init; }
    public OutboundOutcome? LastOutcome { get; init; }
    public string? LastError { get; init; }
    public string? RemoteReference { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>一条尝试记录。</summary>
public record DeliveryAttemptView
{
    public long Id { get; init; }
    public int AttemptNo { get; init; }
    public DeliveryAttemptKind Kind { get; init; }
    public DeliveryAttemptTrigger Trigger { get; init; }
    public long? OperatorId { get; init; }
    public string? OperatorName { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string Outcome { get; init; } = "";
    public int? HttpStatus { get; init; }

    /// <summary>出站调用 Id(可在出站调用记录中按它查同一次调用)。</summary>
    public string? CallId { get; init; }

    public string? ErrorSummary { get; init; }
    public string? Note { get; init; }
    public string? NodeName { get; init; }
}

/// <summary>投递记录详情:记录全部字段、适配器能力、服务端判定的可用操作与尝试历史(新在前,最多 200 条)。</summary>
public record DeliveryDetail : DeliveryListItem
{
    /// <summary>业务载荷 JSON(只含业务数据;入队规则禁止放入秘密)。</summary>
    public string PayloadJson { get; init; } = "";

    /// <summary>并发栅栏(执行人工操作时回传,检测页面是否过期)。</summary>
    public long Fence { get; init; }

    public DateTimeOffset? DeadlineAt { get; init; }
    public DateTimeOffset? ConfirmDeadlineAt { get; init; }
    public bool VerifyBeforeSend { get; init; }
    public DateTimeOffset? LeaseUntil { get; init; }
    public string? LeaseOwner { get; init; }
    public long? ResolvedBy { get; init; }
    public string? ResolutionNote { get; init; }

    /// <summary>适配器仍已注册。</summary>
    public bool AdapterRegistered { get; init; }

    public bool SupportsIdempotency { get; init; }
    public bool SupportsQuery { get; init; }

    /// <summary>当前可执行的人工操作(<see cref="DeliveryActions"/>)。</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    public IReadOnlyList<DeliveryAttemptView> Attempts { get; init; } = [];
}

/// <summary>按状态计数。</summary>
/// <param name="Status">状态</param>
/// <param name="Count">记录数</param>
public sealed record DeliveryStatusCount(DeliveryStatus Status, int Count);

/// <summary>人工操作入参。</summary>
public sealed record DeliveryActionInput
{
    /// <summary>处理说明(确认类操作必填,≤256)。</summary>
    public string? Note { get; init; }

    /// <summary>页面看到的栅栏值;给出且与当前不符即拒绝(页面已过期)。</summary>
    public long? Fence { get; init; }
}

/// <summary>
/// 投递的后台管理(实现契约 §8.6、§12):查询、详情、状态汇总与受控人工操作。人工操作在服务端计算可用性并在执行前复检,
/// 全部保持原幂等标识、走与自动投递相同的调用与安全限制,并记录操作人与说明。
/// <para>默认实现 <see cref="DeliveryAdminService"/>,<c>TryAddScoped</c> 注册、步骤 virtual。</para>
/// </summary>
public interface IDeliveryAdminService
{
    /// <summary>分页查询(新在前)。</summary>
    Task<PagedList<DeliveryListItem>> PageAsync(DeliveryPageInput input, CancellationToken cancellationToken = default);

    /// <summary>详情(含可用操作与尝试历史);不存在抛 49040。</summary>
    Task<DeliveryDetail> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>按状态计数。</summary>
    Task<IReadOnlyList<DeliveryStatusCount>> SummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>执行一项人工操作并返回最新详情;不可用抛 49045,并发修改抛 49046,确认类缺说明抛 49047。</summary>
    Task<DeliveryDetail> ExecuteAsync(long id, string action, DeliveryActionInput input, CancellationToken cancellationToken = default);
}
