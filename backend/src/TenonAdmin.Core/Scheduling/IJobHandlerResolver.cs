namespace TenonAdmin.Core;

/// <summary>
/// 处理器解析器(六件套成员):按 <c>sys_job.HandlerName</c> 找到要执行的 <see cref="IAdminJob"/> 实例。
/// <para>
/// 默认实现遍历 scope 内全部 <see cref="IAdminJob"/> 按 <see cref="IAdminJob.Name"/> Ordinal 匹配;
/// 找不到返回 null——执行器据此记一行失败执行记录(47005 语义),不抛异常不掀翻调度循环。
/// 消费者可整体替换解析策略(前置注册即胜出)。
/// </para>
/// </summary>
public interface IJobHandlerResolver
{
    /// <summary>解析处理器;<paramref name="scopedProvider"/> 是执行器为本次触发开的 scope。找不到返回 null。</summary>
    Task<IAdminJob?> ResolveAsync(string handlerName, IServiceProvider scopedProvider, CancellationToken cancellationToken = default);
}
