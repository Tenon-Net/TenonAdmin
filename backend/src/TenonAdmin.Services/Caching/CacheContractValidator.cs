using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>启动时拒绝仍依赖非原子默认操作的旧缓存实现，避免业务提交后才发现替换契约不完整。</summary>
internal sealed class CacheContractValidator(IServiceScopeFactory scopes) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICacheProvider>();
        var type = provider.GetType();
        var map = type.GetInterfaceMap(typeof(ICacheProvider));
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var method = map.InterfaceMethods[i];
            if (method.Name is not (nameof(ICacheProvider.IncrementAsync) or nameof(ICacheProvider.GetAndRemoveAsync)))
                continue;
            if (map.TargetMethods[i].DeclaringType == typeof(ICacheProvider))
                throw new InvalidOperationException(
                    $"缓存提供者 {type.FullName} 必须实现原子 {method.Name}；请覆写 IncrementAsync（含过期设置）和 GetAndRemoveAsync，或使用内置 Memory/Redis 提供者。");
        }
        // 这里只校验实现已显式承担契约，无法证明自定义方法内部具有原子性。
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
