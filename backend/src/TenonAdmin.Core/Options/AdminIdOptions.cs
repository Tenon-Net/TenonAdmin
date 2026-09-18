namespace TenonAdmin.Core;

/// <summary>
/// 雪花 ID 配置(对应 <c>TenonAdmin:Id</c> 节,设计 §12)。
/// </summary>
public class AdminIdOptions
{
    /// <summary>
    /// 机器号(0–63)。<b>单机部署不配即可</b>:同机多进程由 <see cref="WorkerIdLease"/> 用文件锁自动错开;
    /// <b>跨机器/跨容器必须为每个实例配置不同值</b>,否则不同实例同毫秒发号会撞 Id。
    /// 对应 <c>TenonAdmin:Id:WorkerId</c>。
    /// <para><c>null</c> = 未显式配置：同机抢文件锁，共享库则在 <c>sys_worker_lease</c> 上从 0 试到 63
    /// 谁插入成功谁用谁。显式写 <c>0</c> 不再自动换号，只登记该号；与另一个活实例撞号则启动失败。</para>
    /// </summary>
    public int? WorkerId { get; set; }

    /// <summary>
    /// 同机文件锁目录,对应 <c>TenonAdmin:Id:WorkerIdLockDir</c>。
    /// 空则用 <see cref="WorkerIdLease.PreferredLockDir"/>,写不进去再退
    /// <see cref="WorkerIdLease.FallbackLockDir"/>。
    /// </summary>
    public string? WorkerIdLockDir { get; set; }
}
