namespace TenonAdmin.Core;

/// <summary>
/// WorkerId 的数据库租约生命周期。持有者负责续租；本地确认期限过期时必须暂停发号，
/// 实现可在同一所有者完成一次新鲜确认后恢复。确认所有权已丢失或生命周期已释放后，必须永久拒绝发号。
/// Core 只依赖此契约；数据库实现由 Services 提供。
/// </summary>
public interface IWorkerIdLeaseLifetime : IDisposable
{
    /// <summary>
    /// 当前租约已获有效确认、可用于发号时返回；确认期限已过、所有权已丢失或已释放时，
    /// 抛出 <see cref="InvalidOperationException"/>。确认期限已过是暂停状态，具体实现可在安全地重新确认同一所有者后恢复。
    /// </summary>
    void EnsureActive();
}
