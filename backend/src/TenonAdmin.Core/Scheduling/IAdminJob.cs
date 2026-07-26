namespace TenonAdmin.Core;

/// <summary>
/// 编译类任务处理器——消费者接入定时任务唯一必须实现的契约(docs/scheduling-ledger.md §6)。
/// <para>
/// 注册(内核内置与消费者同一路径,Scoped——任务普遍要用仓储,执行器每次触发开新 scope 解析):
/// <code>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IAdminJob, MyCleanupJob&gt;());</code>
/// 不用 keyed DI:<c>TryAddEnumerable</c> 自带"按实现类型防重"语义、六件套契约现成;
/// 且类型名匹配让 DB 行 ↔ 代码的对应肉眼可查。
/// </para>
/// <para>
/// <b>实现必须真异步</b>:禁 <c>Thread.Sleep</c> / <c>.Result</c> / <c>.Wait()</c>——
/// 同步阻塞的任务 8 个在飞就占死 8 个线程池线程,MaxConcurrentRuns 只是兜底不是解药。
/// </para>
/// </summary>
public interface IAdminJob
{
    /// <summary>处理器标识,<c>sys_job.HandlerName</c> 按它匹配(Ordinal);默认 = 类型全名。</summary>
    string Name => GetType().FullName!;

    /// <summary>
    /// 执行一次触发。<paramref name="cancellationToken"/> 覆盖三种取消:任务超时(TimeoutSeconds)、
    /// 手动终止(kill)、宿主停机——实现应把它传给所有下游异步调用。
    /// </summary>
    Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
}
