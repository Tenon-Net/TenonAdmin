namespace TenonAdmin.Core;

/// <summary>
/// Level3 部署期 InitGrant / EmergencyGrant 的持久 TTL 与一次性消费。
/// 不得仅依赖可丢弃缓存;消费与 first-seen 须在重启/清空 Redis 后仍成立。
/// </summary>
public interface ILevel3DeployGrantStore
{
    /// <summary>
    /// 无副作用可用性检查:消费状态、first-seen TTL、绝对 NotAfter。
    /// 不创建 first-seen 行。无 first-seen 且未消费时,仅当绝对 NotAfter 仍有效才视为可用。
    /// </summary>
    Task<Level3DeployGrantUsability> CheckUsableAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 确保 first-seen 已记录且仍在 TTL/绝对窗口内(有副作用:首次写入 FirstSeenAt)。
    /// 已消费或已过期 → 抛 <see cref="ErrorCode.BindInviteInvalid"/>。
    /// </summary>
    Task EnsureWithinTtlAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子消费:单条条件更新要求 ConsumedAt 为空、first-seen TTL 未过、绝对 NotAfter 未过;
    /// 受影响行数必须为 1,否则视为并发失败/已失效。
    /// </summary>
    Task ConsumeAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default);
}

/// <summary>部署授权可用性结果(预检/诊断用,不抛业务异常)。</summary>
public sealed class Level3DeployGrantUsability
{
    public bool Usable { get; init; }
    public string Reason { get; init; } = "";

    public static Level3DeployGrantUsability Ok(string reason = "usable") =>
        new() { Usable = true, Reason = reason };

    public static Level3DeployGrantUsability Fail(string reason) =>
        new() { Usable = false, Reason = reason };
}
