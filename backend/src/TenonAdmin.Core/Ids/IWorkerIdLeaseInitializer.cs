namespace TenonAdmin.Core;

/// <summary>
/// 为默认发号器使用的分配确认数据库租约。可接管消费者既有领取器或前置注册的分配，
/// 不要求调用者改变 <see cref="IWorkerIdSlotClaimer"/> 的签名或等待 Host 启动。
/// </summary>
public interface IWorkerIdLeaseInitializer
{
    /// <summary>
    /// 并发幂等确认租约；实现必须在返回前附加有效的 <see cref="IWorkerIdLeaseLifetime"/>，否则抛异常。
    /// 已有消费者 lifetime 时只检查其有效性，不另建默认租约；已失效或槽位属于其它节点时拒绝初始化。
    /// 失败时清理本次创建的数据库资源，不释放调用方的 assignment 或文件锁；调用方可重试或自行释放。
    /// </summary>
    void Initialize(WorkerIdAssignment assignment);
}
