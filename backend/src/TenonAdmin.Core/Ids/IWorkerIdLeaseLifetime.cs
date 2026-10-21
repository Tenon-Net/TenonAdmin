namespace TenonAdmin.Core;

/// <summary>
/// WorkerId 的数据库租约生命周期。持有者负责续租，失去所有权、过期或释放后必须永久拒绝发号。
/// Core 只依赖此契约；数据库实现由 Services 提供。
/// </summary>
public interface IWorkerIdLeaseLifetime : IDisposable
{
    /// <summary>租约尚可用于发号时返回，否则抛出 <see cref="InvalidOperationException"/>。</summary>
    void EnsureActive();
}
