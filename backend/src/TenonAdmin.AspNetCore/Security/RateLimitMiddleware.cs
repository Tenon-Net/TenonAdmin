using Microsoft.AspNetCore.Http;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 按客户端 IP 的固定窗口限流(设计 §12/§14):全局一档 + 认证端点(<c>/api/v1/auth/*</c>)更严一档,
/// 超阈即 429 + 统一信封(<see cref="ErrorCode.TooManyRequests"/>)+ <c>Retry-After</c>。
///
/// <para><b>为什么自己数,而不用 ASP.NET 的 RateLimiter</b>:<c>PartitionedRateLimiter</c> 的窗口计数留在
/// <b>进程内的限流器对象</b>里 → N 个副本 = N × 阈值(认证桶默认 20/min,两副本就成了 40/min),
/// §14 的爆破基线被<b>静默</b>削半。而它的分区选择器是<b>同步</b>的,拿不到异步的分布式计数
/// (当初被迫造 <see cref="RuntimeRateLimit"/> 快照、把阈值编进分区键,正是这个同步约束的产物)。
/// 计数改走 <see cref="ICacheProvider.IncrementAsync"/> 后:装 Redis = 计数跨副本共享;不装 = 退回进程内,
/// 与原行为一致。<b>不新增任何抽象</b>——该方法的注释本就写着"用于登录失败计数等并发安全计数场景",
/// 且内存/Redis 两个实现都已覆写为真原子。我们本来也没用过限流器的排队能力(<c>QueueLimit</c> 恒为 0,超阈即拒)。</para>
///
/// <para>阈值/开关从 <see cref="RuntimeRateLimit"/> 的快照<b>同步</b>读(避免每请求回查 4 个配置键);
/// 快照启动载入 + 订阅配置变更 + 定期重载(跨副本收敛)。</para>
///
/// <para>限流器跑在路由之前,按 <c>Request.Path</c> 直接区分认证端点(不依赖端点元数据)。
/// 客户端 IP 取 <c>Connection.RemoteIpAddress</c> —— 反代之后必须先经 <c>UseForwardedHeaders</c> 还原成真实客户端,
/// 否则全体用户共享同一个桶(见 <see cref="AdminForwardedHeadersOptions"/>)。</para>
///
/// <para>// ponytail: 固定窗口,窗口边界允许 2× 突发(与被替换掉的 FixedWindowRateLimiter 同款,非回归)。
/// 要平滑就得改滑动窗口/令牌桶——那需要每键存一个结构而不只是计数,等真被边界突发咬到再说。</para>
/// </summary>
internal sealed class RateLimitMiddleware(
    RequestDelegate next,
    RuntimeRateLimit runtime,
    ICacheProvider cache,
    TimeProvider time)
{
    /// <summary>认证端点(登录/刷新/验证码):爆破的主战场,单独一档更严的阈值。</summary>
    private const string AUTH_PREFIX = "/api/v1/auth";

    public async Task InvokeAsync(HttpContext context)
    {
        var snapshot = runtime.Current;
        if (!snapshot.Enabled)
        {
            await next(context);
            return;
        }

        var isAuth = context.Request.Path.StartsWithSegments(AUTH_PREFIX);
        var permit = isAuth ? snapshot.AuthPermitPerWindow : snapshot.PermitPerWindow;
        if (permit <= 0)   // 该档位阈值 <=0 视为不限
        {
            await next(context);
            return;
        }

        var window = snapshot.WindowSeconds > 0 ? snapshot.WindowSeconds : 60;
        var nowSeconds = time.GetUtcNow().ToUnixTimeSeconds();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // 窗口序号编进键 → 换窗口即换键、计数自然归零;旧键靠 TTL 回收,无需清理逻辑。
        var key = CacheKeys.RateLimit(isAuth ? "auth" : "all", ip, nowSeconds / window);
        var count = await cache.IncrementAsync(key, TimeSpan.FromSeconds(window), context.RequestAborted);

        if (count <= permit)
        {
            await next(context);
            return;
        }

        var retryAfter = (int)(window - nowSeconds % window);   // 距本窗口结束还有多久
        context.Response.Headers.RetryAfter = retryAfter.ToString();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            Result<object>.Fail(ErrorCode.TooManyRequests, new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter }),
            context.RequestAborted);
    }
}
