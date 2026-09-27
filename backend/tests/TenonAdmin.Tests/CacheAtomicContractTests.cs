using TenonAdmin.Core;
using TenonAdmin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TenonAdmin.Tests;

public class CacheAtomicContractTests
{
    [Fact]
    public async Task Provider_without_atomic_operations_fails_closed_without_touching_data()
    {
        ICacheProvider cache = new BasicCache();
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.IncrementAsync("attempts"));
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.GetAndRemoveAsync<string>("ticket"));
    }

    [Fact]
    public async Task Host_startup_rejects_incomplete_custom_cache_before_business_requests()
    {
        using var host = ValidatorHost(new BasicCache());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains(nameof(BasicCache), error.Message);
        Assert.Contains(nameof(ICacheProvider.IncrementAsync), error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_accepts_inherited_and_explicit_atomic_implementations(bool explicitImplementation)
    {
        using var host = ValidatorHost(explicitImplementation ? new ExplicitCache() : new InheritedCache());
        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost ValidatorHost(ICacheProvider cache) =>
        Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(cache);
            services.AddTenonAdminServices();
            // 只启动缓存契约检查；该测试不需要数据库等完整宿主依赖。
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Name != "CacheContractValidator").ToArray())
                services.Remove(descriptor);
        }).Build();

    private class AtomicCache : BasicCache, ICacheProvider
    {
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => throw new Exception("Unexpected probe");
        public Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default) => throw new Exception("Unexpected probe");
    }

    private sealed class InheritedCache : AtomicCache;

    private sealed class ExplicitCache : BasicCache, ICacheProvider
    {
        Task<long> ICacheProvider.IncrementAsync(string key, TimeSpan? expiry, CancellationToken cancellationToken) => throw new Exception("Unexpected probe");
        Task<T?> ICacheProvider.GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken) where T : default => throw new Exception("Unexpected probe");
    }

    private class BasicCache : ICacheProvider
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => throw new Exception("Unexpected read");
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => throw new Exception("Unexpected write");
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new Exception("Unexpected removal");
    }
}
