namespace TenonAdmin.Core;

/// <summary>
/// 本进程雪花机器号的分配结果。未显式配置 <c>TenonAdmin:Id:WorkerId</c> 时持有
/// <see cref="WorkerIdLease"/>，必须由 DI 单例根住——<c>FileStream</c> 被回收会提前放锁，
/// 进程却还在用那个号发 Id。
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
    public void Dispose() => Lease?.Dispose();
}
