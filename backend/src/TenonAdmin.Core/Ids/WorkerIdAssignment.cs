namespace TenonAdmin.Core;

/// <summary>
/// 本进程雪花机器号的分配结果，同时根住文件锁和可选的数据库续租生命周期。
/// 必须由 DI 单例持有，并在释放后停止使用关联的发号器。
/// </summary>
public sealed class WorkerIdAssignment : IDisposable
{
    /// <summary>本进程使用的机器号（0–63）。</summary>
    public long WorkerId { get; }

    /// <summary>文件锁租约；显式配置了 WorkerId 时为 null。</summary>
    public WorkerIdLease? Lease { get; }

    /// <summary>
    /// 领槽时写入租约行的节点令牌。守卫必须用同一个，才能认出「库里那行是自己刚领的」，
    /// 而不是靠 pid（同进程里两个守卫测例会误伤）。
    /// </summary>
    public string? InstanceToken { get; }

    private readonly object _gate = new();
    private IWorkerIdLeaseLifetime? _lifetime;
    private bool _requiresLease;
    private bool _disposed;

    /// <summary>已接管本分配的数据库租约；数据层单独使用文件锁时为 null。</summary>
    public IWorkerIdLeaseLifetime? Lifetime
    {
        get { lock (_gate) return _lifetime; }
    }

    /// <summary>完整 Services 装配要求先确认数据库租约，才允许默认发号器工作。</summary>
    public void RequireLease()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _requiresLease = true;
        }
    }

    /// <summary>将唯一的数据库续租生命周期交给本分配管理；重复接管或释放后接管会失败。</summary>
    public void AttachLifetime(IWorkerIdLeaseLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_lifetime is not null)
                throw new InvalidOperationException("WorkerId 已有数据库租约生命周期，不能重复接管。");
            lifetime.EnsureActive();
            _lifetime = lifetime;
            _requiresLease = true;
        }
    }

    /// <summary>默认发号器在计算前后检查；等待内部锁或时钟期间过期的 ID 不得返回给调用者。</summary>
    public void EnsureActive()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_requiresLease && _lifetime is null)
                throw new InvalidOperationException("WorkerId 数据库租约尚未确认，拒绝生成 ID。");
            _lifetime?.EnsureActive();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new InvalidOperationException("WorkerId 分配已释放，拒绝生成 ID。");
    }

    private WorkerIdAssignment(long workerId, WorkerIdLease? lease, string? instanceToken = null)
    {
        WorkerId = workerId;
        Lease = lease;
        InstanceToken = instanceToken;
    }

    /// <summary>库表领槽成功后再配上该号的文件锁。</summary>
    public static WorkerIdAssignment FromClaimedSlot(long workerId, WorkerIdLease lease, string instanceToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrEmpty(instanceToken);
        if (lease.WorkerId != workerId)
            throw new ArgumentException("文件锁抢到的号与库表领到的号不一致。", nameof(lease));
        return new WorkerIdAssignment(workerId, lease, instanceToken);
    }

    /// <summary>
    /// 显式 <see cref="AdminIdOptions.WorkerId"/>（含 0）直接用，不抢文件锁。
    /// 选项未注册时回落 0、不抢锁（SqlSugar 单独装配的测试/单进程路径）。
    /// 选项在但未配号时 <see cref="WorkerIdLease.Acquire"/>。
    /// </summary>
    public static WorkerIdAssignment Resolve(AdminIdOptions? options, WorkerIdLockDirFallback? onFallback = null)
    {
        if (options?.WorkerId is { } configured)
            return new WorkerIdAssignment(configured, lease: null);
        if (options is null)
            return new WorkerIdAssignment(0, lease: null);

        var lease = WorkerIdLease.Acquire(options.WorkerIdLockDir, onFallback);
        return new WorkerIdAssignment(lease.WorkerId, lease);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IWorkerIdLeaseLifetime? lifetime;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            lifetime = _lifetime;
        }
        try { lifetime?.Dispose(); }
        finally { Lease?.Dispose(); }
    }
}
