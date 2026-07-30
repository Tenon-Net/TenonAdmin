using System.Text.Json;
using StackExchange.Redis;
using TenonAdmin.Core;

namespace TenonAdmin.Caching.Redis;

/// <summary>
/// <see cref="ICacheProvider"/> 的 Redis 实现(可选包 <c>TenonAdmin.Caching.Redis</c>):
/// 用 StackExchange.Redis 把热数据放进共享 Redis,替代进程内 <c>MemoryCacheProvider</c>,支持多实例共享缓存。
/// <para>逻辑键统一追加 <see cref="AdminCacheOptions.KeyPrefix"/> 前缀(如 <c>tenon:perm:123</c>,对应设计 §15 命名);
/// 与其它服务共享同一 Redis 实例时按前缀隔离命名空间,调用方不关心前缀。</para>
/// <para>值用 System.Text.Json 序列化(进程外必然涉及序列化;缓存的 DTO 均为序列化友好的 record/POCO)。
/// 连接<b>惰性</b>建立且 <c>AbortOnConnectFail=false</c>:构造时不连库(DI 注册期不依赖活跃 Redis),
/// 首次使用才连、断线后台自动重连;连不上时操作抛异常,由 <c>/health/ready</c> 探针捕获报未就绪。</para>
/// </summary>
public class RedisCacheProvider : ICacheProvider, ISecureCacheCapabilities, IDisposable
{
    private readonly string _prefix;
    private readonly AdminCacheOptions _options;
    private readonly Lazy<IConnectionMultiplexer> _mux;

    // 缓存 DTO 属性名与 JSON 默认(PascalCase)一致,无需驼峰策略;不缩进以省带宽。
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    /// <param name="options">
    /// 缓存配置:取 <see cref="AdminCacheOptions.RedisConnectionString"/> 建连、<see cref="AdminCacheOptions.KeyPrefix"/> 作键前缀。
    /// </param>
    public RedisCacheProvider(AdminCacheOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            throw new InvalidOperationException("启用 Redis 缓存需配置 TenonAdmin:Cache:RedisConnectionString。");

        _options = options;
        _prefix = options.KeyPrefix;
        var conn = options.RedisConnectionString;
        var requireTls = options.RequireTls;
        // AbortOnConnectFail=false:连不上也返回多路复用器(后台持续重连),
        // 避免 Lazy 缓存首连异常,导致 Redis 恢复后本进程仍永久不可用。
        _mux = new Lazy<IConnectionMultiplexer>(() =>
        {
            var config = ConfigurationOptions.Parse(conn);
            config.AbortOnConnectFail = false;
            // Level3 / 显式 RequireTls:强制 SSL,避免连接串漏写 ssl=true 时明文连
            if (requireTls)
                config.Ssl = true;
            return ConnectionMultiplexer.Connect(config);
        });
    }

    /// <inheritdoc />
    public bool IsDistributed => true;

    /// <inheritdoc />
    public bool HasAuthenticationConfigured =>
        RedisConnectionSecurity.Inspect(_options.RedisConnectionString, _options.RequireTls).HasAuth;

    /// <inheritdoc />
    public bool HasTlsConfigured =>
        RedisConnectionSecurity.Inspect(_options.RedisConnectionString, _options.RequireTls).HasTls
        || _options.RequireTls;

    /// <inheritdoc />
    public virtual async Task<(bool Ok, string Message)> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pong = await Database.PingAsync();
            return (true, $"PING ok ({pong.TotalMilliseconds:F0} ms)");
        }
        catch (Exception ex)
        {
            // 脱敏:不回显连接串/密码
            return (false, $"Redis PING 失败: {ex.GetType().Name}");
        }
    }

    /// <summary>当前 Redis 库句柄(扩展点:子类可覆写以复用外部注入的 <see cref="IConnectionMultiplexer"/> 单例)。</summary>
    protected virtual IDatabase Database => _mux.Value.GetDatabase();

    private string Prefixed(string key) => _prefix + key;

    /// <inheritdoc />
    public virtual async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Deserialize<T>(await Database.StringGetAsync(Prefixed(key)));

    /// <inheritdoc />
    public virtual Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        // expiry 为 null → 不设 TTL(永久,仅靠显式失效清除);给定 → 相对过期(TimeSpan 隐式转 Expiration,StackExchange.Redis 3.x)
        return expiry is { } ttl && ttl > TimeSpan.Zero
            ? Database.StringSetAsync(full, json, ttl)
            : Database.StringSetAsync(full, json);
    }

    /// <inheritdoc />
    public virtual Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Database.KeyDeleteAsync(Prefixed(key));

    /// <inheritdoc />
    /// <remarks>
    /// Redis 原生 <c>INCR</c> 原子自增(跨实例安全);expiry 给定则每次自增后刷新 TTL(滑动窗口,契合登录失败计数)。
    /// 存的是纯整数,<see cref="GetAsync{T}"/> 以 <c>long</c> 读取时经 JSON 解析("5" 是合法 JSON 数字)对得上。
    /// </remarks>
    public virtual async Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        var next = await Database.StringIncrementAsync(full);
        if (expiry is { } e && e > TimeSpan.Zero)
            await Database.KeyExpireAsync(full, e);
        return next;
    }

    /// <inheritdoc />
    /// <remarks>Redis 原生 <c>GETDEL</c>(需 Redis 6.2+)原子取删:并发携同一票据时只有一个调用取到非空值。</remarks>
    public virtual async Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Deserialize<T>(await Database.StringGetDeleteAsync(Prefixed(key)));

    /// <summary>反序列化缓存值;键不存在(<see cref="RedisValue.IsNullOrEmpty"/>)返回 <c>default</c>——
    /// 与内存实现一致,"已缓存的空集合" 与 "未缓存" 可区分。</summary>
    private static T? Deserialize<T>(RedisValue value) =>
        value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);

    /// <summary>释放底层连接(仅当已惰性建立);单例随容器释放,单元测试反复起停宿主时避免连接泄漏。</summary>
    public void Dispose()
    {
        if (_mux.IsValueCreated) _mux.Value.Dispose();
        GC.SuppressFinalize(this);
    }
}
