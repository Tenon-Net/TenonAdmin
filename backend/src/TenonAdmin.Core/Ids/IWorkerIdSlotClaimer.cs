namespace TenonAdmin.Core;

/// <summary>
/// 未显式配置 <c>TenonAdmin:Id:WorkerId</c> 时，从共享库领一个空闲机器号。
/// 实现放在 Services（能碰 <c>sys_worker_lease</c>）；Core 只有接口，避免发号器与
/// <c>ISqlSugarClient</c> 循环依赖。
/// </summary>
public interface IWorkerIdSlotClaimer
{
    /// <summary>
    /// 默认实现为显式号确认固定槽，未配则领取 0–63 中第一个空闲槽并抢文件锁；
    /// 返回的分配持有持续续租的生命周期。消费者已有实现仍可返回 Resolve/FromClaimedSlot 的分配，
    /// 默认发号器会经 <see cref="IWorkerIdLeaseInitializer"/> 接管尚未附加的数据库租约。
    /// </summary>
    WorkerIdAssignment Claim(AdminIdOptions? options, WorkerIdLockDirFallback? onFallback = null);
}
