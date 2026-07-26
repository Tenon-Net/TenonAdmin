using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// HTTP 任务的共享客户端(单例):持有一个套了 SSRF 围栏的 <see cref="HttpClient"/>。
/// <para>不引 <c>Microsoft.Extensions.Http</c>——仓内成法是长命实例(Auth.DingTalk/WeCom 同款),
/// DNS 陈旧由 <c>PooledConnectionLifetime</c>(5 分钟)解决;超时不用 <see cref="HttpClient.Timeout"/>,
/// 由任务 TimeoutSeconds / 属性包 timeoutSeconds 的取消令牌控制。</para>
/// </summary>
public class JobHttpClient(AdminJobsOptions options) : IDisposable
{
    /// <summary>共享客户端(线程安全;围栏见 <see cref="JobHttpFence.CreateHandler"/>)</summary>
    public HttpClient Client { get; } = new(JobHttpFence.CreateHandler(options.Http), disposeHandler: true)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
