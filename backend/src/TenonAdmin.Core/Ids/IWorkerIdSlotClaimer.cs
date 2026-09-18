namespace TenonAdmin.Core;

/// <summary>
/// 未显式配置 <c>TenonAdmin:Id:WorkerId</c> 时，从共享库领一个空闲机器号。
/// 实现放在 Services（能碰 <c>sys_worker_lease</c>）；Core 只有接口，避免发号器与
/// <c>ISqlSugarClient</c> 循环依赖。
/// </summary>
public interface IWorkerIdSlotClaimer
{
    /// <summary>
    /// 显式配置了号则不写库（留给租约守卫去登记）；未配则 <c>INSERT</c> 0–63 中第一个成功的槽，
    /// 再抢该号的文件锁。
    /// </summary>
    WorkerIdAssignment Claim(AdminIdOptions? options, WorkerIdLockDirFallback? onFallback = null);
}
