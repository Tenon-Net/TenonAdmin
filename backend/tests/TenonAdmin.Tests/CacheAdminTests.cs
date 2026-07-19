using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 缓存管理(C2)——定向失效动作按 DB 已知 ID 驱动、逐键移除,不枚举缓存。锁 <c>FlushConfigAsync</c>:
/// 对每个 <c>sys_config</c> 键预置哨兵缓存,清后全部消失且返回值 = 配置数(证明"遍历 DB 键 → 逐键移除"闭环)。
/// </summary>
public class CacheAdminTests
{
    [Fact]
    public async Task Flush_config_removes_cache_for_every_config_key()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var cache = sp.GetRequiredService<ICacheProvider>();   // Singleton:跨 scope 同一实例
        var keys = await sp.GetRequiredService<IRepository<SysConfig>>().AsQueryable().Select(c => c.ConfigKey).ToListAsync();
        Assert.NotEmpty(keys);   // 种子已播若干配置项

        // 对每个配置键预置哨兵值,并确认确已缓存
        foreach (var key in keys)
            await cache.SetAsync(CacheKeys.Config(key), "sentinel");
        foreach (var key in keys)
            Assert.Equal("sentinel", await cache.GetAsync<string>(CacheKeys.Config(key)));

        // 清配置缓存 → 返回被清数 = 配置数,且每个键都已移除(下次读穿透重建)
        var cleared = await sp.GetRequiredService<ICacheAdminService>().FlushConfigAsync();
        Assert.Equal(keys.Count, cleared);
        foreach (var key in keys)
            Assert.Null(await cache.GetAsync<string>(CacheKeys.Config(key)));
    }
}
