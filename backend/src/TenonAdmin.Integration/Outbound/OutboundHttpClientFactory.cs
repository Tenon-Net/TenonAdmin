using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TenonAdmin.Integration;

/// <summary>
/// 按目标隔离的 <see cref="HttpClient"/>(单例;不引 <c>Microsoft.Extensions.Http</c>,与内核 <c>JobHttpClient</c> 同一做法)。
/// <para>每个目标两个客户端:<b>读</b>(GET/HEAD/OPTIONS)走连接池;<b>写</b>(其余方法)每次新建连接并带 <c>Connection: close</c>。
/// 原因:<see cref="SocketsHttpHandler"/> 对「复用的池化连接上发送失败」有内部重试,何时判定可重试属于未公开的实现细节;
/// 写请求从不复用连接,「一次写调用最多到达对方一次」就不依赖这项细节(实测 .NET 10 在对方执行后断开时并不重发,但框架不以此为前提)。
/// 每次写都在新连接上按当下的 DNS 解析结果复检地址,代价是多一次建连(HTTPS 还有一次握手)。</para>
/// <para>全部客户端:禁用代理(否则建连回调只看得见代理 IP,地址策略失效)、禁用自动重定向、建连时对 DNS 解析出的每个地址复检
/// <see cref="OutboundAddressGuard"/>;建连超时单独计时(取调用超时的一半,1–15 秒),使「连不上」能与「发出后没等到结果」区分开。
/// 整体超时由调用方的取消令牌控制。</para>
/// <para>默认 <c>TryAddSingleton</c> 注册;可继承覆写 <see cref="ResolveAsync"/>(如接入内部 DNS),覆写结果同样逐个经过地址策略。</para>
/// </summary>
public class OutboundHttpClientFactory : IDisposable
{
    private readonly ConcurrentDictionary<(string Target, bool Write), Lazy<HttpClient>> _clients = new();

    /// <summary>取目标的客户端(按目标名与读写分别缓存)。</summary>
    public HttpClient GetClient(OutboundTarget target, bool write)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _clients.GetOrAdd((target.Name.ToLowerInvariant(), write),
            _ => new Lazy<HttpClient>(() => new HttpClient(CreateHandler(target, write), disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            })).Value;
    }

    /// <summary>方法是否按「写」对待(GET/HEAD/OPTIONS 以外一律视为可能改变远端状态)。</summary>
    public static bool IsWrite(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options;
    }

    /// <summary>建连超时:调用超时的一半,限制在 1–15 秒。</summary>
    public static TimeSpan ConnectTimeoutFor(OutboundTarget target) =>
        TimeSpan.FromMilliseconds(Math.Clamp(target.Timeout.TotalMilliseconds / 2, 1_000, 15_000));

    /// <summary>解析主机名。默认走系统 DNS;覆写的结果同样逐个经过 <see cref="OutboundAddressGuard"/>。</summary>
    protected virtual async ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);

    /// <summary>构造目标的处理器(禁代理、禁重定向、建连复检;写客户端不入池)。</summary>
    protected virtual SocketsHttpHandler CreateHandler(OutboundTarget target, bool write) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        Proxy = null,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        // 写客户端:空闲超时为 0 → 连接用完即关,池里没有可复用的连接
        PooledConnectionIdleTimeout = write ? TimeSpan.Zero : TimeSpan.FromMinutes(1),
        ConnectTimeout = ConnectTimeoutFor(target),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
                ? [literal]
                : await ResolveAsync(host, cancellationToken);
            var allowed = addresses.Where(a => OutboundAddressGuard.IsAllowed(a, target.TrustedCidrs)).ToArray();
            if (allowed.Length == 0) throw new OutboundTargetBlockedException(host);

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var client in _clients.Values.Where(c => c.IsValueCreated)) client.Value.Dispose();
        _clients.Clear();
        GC.SuppressFinalize(this);
    }
}
