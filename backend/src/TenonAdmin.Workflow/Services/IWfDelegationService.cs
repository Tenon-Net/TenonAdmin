using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>长期委托管理与任务创建时的一跳解析；消费者可整体替换。</summary>
public interface IWfDelegationService
{
    Task<PagedList<WfDelegationRuleOutput>> PageAsync(
        WfDelegationRulePageInput input,
        CancellationToken cancellationToken = default);

    Task<WfDelegationRuleOutput> AddAsync(
        WfDelegationRuleInput input,
        CancellationToken cancellationToken = default);

    Task<WfDelegationRuleOutput> UpdateAsync(
        long id,
        WfDelegationRuleInput input,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        long id,
        string? requestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WfDelegationAssignment>> ResolveAsync(
        IReadOnlyList<long> originalUserIds,
        CancellationToken cancellationToken = default);
}

/// <summary>长期委托规则提交后的通知 SPI；默认实现复用实时推送。</summary>
public interface IWfDelegationNotifier
{
    Task RuleChangedAsync(
        WfDelegationRuleOutput rule,
        WfDelegationRuleChangeType changeType,
        CancellationToken cancellationToken = default);
}

/// <summary>长期委托规则管理入参。时间统一按 UTC 解释。</summary>
public record WfDelegationRuleInput
{
    public long OriginalUserId { get; init; }
    public long DelegateUserId { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public string? RequestId { get; init; }
}

public record WfDelegationRulePageInput : PageInputBase
{
    public long? OriginalUserId { get; init; }
    public bool? Enabled { get; init; }
}

public record WfDelegationRuleOutput
{
    public long Id { get; init; }
    public long ScopeOrgId { get; init; }
    public long OriginalUserId { get; init; }
    public long DelegateUserId { get; init; }
    public string? OriginalUserName { get; init; }
    public string? DelegateUserName { get; init; }
    public bool Enabled { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public int Version { get; init; }
}

/// <summary>任务 actor 的一次长期委托解析快照。</summary>
public sealed record WfDelegationAssignment(
    long UserId,
    long? OriginalUserId,
    long? DelegationRuleId,
    long? ScopeOrgId);
