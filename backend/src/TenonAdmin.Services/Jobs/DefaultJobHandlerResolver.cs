using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 默认处理器解析:遍历 scope 内全部 <see cref="IAdminJob"/>,按 <see cref="IAdminJob.Name"/> Ordinal 匹配;
/// 找不到返回 null(执行器记 47005 语义失败行)。消费者前置注册自己的 <see cref="IJobHandlerResolver"/> 即整体替换。
/// </summary>
public class DefaultJobHandlerResolver : IJobHandlerResolver
{
    /// <inheritdoc />
    public virtual Task<IAdminJob?> ResolveAsync(string handlerName, IServiceProvider scopedProvider, CancellationToken cancellationToken = default)
    {
        var match = scopedProvider.GetServices<IAdminJob>()
            .FirstOrDefault(j => string.Equals(j.Name, handlerName, StringComparison.Ordinal));
        return Task.FromResult(match);
    }
}
