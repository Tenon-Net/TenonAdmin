using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 应用状态版本、按列更新的审计列与 UTC 时间的共用小工具。版本递增用 <c>StateVersion = StateVersion + 1</c> 的原子 SQL,
/// 并发的两次变更不会写出同一个版本号(否则按版本缓存的授权可能永远停在中间态)。
/// </summary>
public static class IntegrationAppState
{
    /// <summary>原子递增应用状态版本;应在承载变更的同一事务内调用。</summary>
    public static Task<int> BumpAsync(ISqlSugarClient db, long appId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Updateable<IntegrationApp>()
            .SetColumns(a => a.StateVersion == a.StateVersion + 1)
            .Where(a => a.Id == appId)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>
    /// 按列更新时补上审计列:<c>SetColumns</c> 不走整对象更新路径,内核审计 AOP 不会触发(与内核软删同一做法),
    /// 不显式写,UpdateTime/UpdateUserId 就停在更新之前。无登录上下文时不写操作人。
    /// </summary>
    internal static IUpdateable<T> WithAudit<T>(this IUpdateable<T> update, TimeProvider time, ICurrentUser? currentUser)
        where T : AuditEntity, new()
    {
        var now = time.GetLocalNow().DateTime;   // 与审计 AOP 同一时间口径
        update = update.SetColumns(e => e.UpdateTime == now);
        return currentUser?.UserId is { } userId ? update.SetColumns(e => e.UpdateUserId == userId) : update;
    }

    /// <summary>UTC 时刻向下取整到秒:各方言 datetime 精度不同(MySQL 常无小数秒),取整后读回值与写入值一致。</summary>
    public static DateTime FloorSeconds(DateTime utc) =>
        new(utc.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>库里读回的 UTC 列(Kind 丢失)转成带偏移的时刻,序列化为 ISO 8601 带 <c>+00:00</c>。</summary>
    public static DateTimeOffset? ToOffset(DateTime? utc) =>
        utc is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
}
