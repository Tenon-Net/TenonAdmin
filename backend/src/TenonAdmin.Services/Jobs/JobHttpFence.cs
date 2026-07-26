using System.Net;
using System.Net.Sockets;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// HTTP 任务的 SSRF 围栏(scheduling-ledger §7.1):仅 http/https、主机白名单、CIDR 黑名单。
/// <b>两次校验</b>:入库时(JobService)+ 执行时;域名解析后的 IP 在 <see cref="CreateHandler"/> 的
/// ConnectCallback 里复检——防 DNS rebinding(校验时解析成公网、执行时解析成内网的把戏)。
/// 默认只封云元数据段(169.254.0.0/16),<b>不封内网</b>:调度器打内网服务是主用途。
/// </summary>
public static class JobHttpFence
{
    /// <summary>URL 静态校验;不过关抛 <see cref="ErrorCode.JobHttpUrlBlocked"/>(47009)。</summary>
    public static void ValidateUrl(string url, AdminJobsHttpOptions http)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AdminException(ErrorCode.JobHttpUrlBlocked, new Dictionary<string, object?> { ["url"] = url },
                $"URL 非法或协议不受支持(仅 http/https):{url}");
        if (http.AllowedHosts is { Length: > 0 } allow
            && !allow.Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase)))
            throw new AdminException(ErrorCode.JobHttpUrlBlocked, new Dictionary<string, object?> { ["url"] = url },
                $"主机不在白名单(Jobs:Http:AllowedHosts):{uri.Host}");
        if (IPAddress.TryParse(uri.Host, out var literal) && IsBlocked(literal, http.BlockedCidrs))
            throw new AdminException(ErrorCode.JobHttpUrlBlocked, new Dictionary<string, object?> { ["url"] = url },
                $"目标地址命中围栏黑名单(Jobs:Http:BlockedCidrs):{uri.Host}");
        // 域名的解析后复检在 ConnectCallback——此处只拦字面 IP,不做"校验时解析"(那正是 rebinding 绕过面)
    }

    /// <summary>
    /// 校验一对请求头。<b>拦 CRLF 注入</b>:<c>TryAddWithoutValidation</c> 对含 CR/LF 的值原样上线路,
    /// 内部人能借此在同一连接上走私第二个请求(方法/路径/Host 全自选),ValidateUrl 与执行记录都看不见它。
    /// 入库与执行两处都调本方法;不合规抛 <see cref="ErrorCode.JobPropsInvalid"/>。
    /// </summary>
    public static void ValidateHeader(string name, string? value)
    {
        // 名字必须是 HTTP token(RFC 9110):可见 ASCII 且不含分隔符
        const string separators = "()<>@,;:\\\"/[]?={} \t";
        var nameOk = !string.IsNullOrEmpty(name)
            && name.All(c => c > 0x20 && c < 0x7F && !separators.Contains(c));
        AdminException.ThrowIf(!nameOk, ErrorCode.JobPropsInvalid,
            new Dictionary<string, object?> { ["key"] = "headers" });
        // 值不许出现任何控制字符(制表符除外)——CR/LF/NUL 都在此拦下
        var valueOk = value is null || value.All(c => c == '\t' || (c >= 0x20 && c != 0x7F));
        AdminException.ThrowIf(!valueOk, ErrorCode.JobPropsInvalid,
            new Dictionary<string, object?> { ["key"] = "headers" });
    }

    /// <summary>IP 是否命中 CIDR 黑名单(IPv4 映射 IPv6 先折回 IPv4)。</summary>
    public static bool IsBlocked(IPAddress ip, string[] cidrs)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return cidrs.Any(c => Matches(ip, c));
    }

    /// <summary>
    /// 解析一条 CIDR;不合法返回 false。无斜杠按单地址处理(<c>/32</c> / <c>/128</c>)——
    /// 「169.254.169.254」这种直觉写法能用,不至于手抖一个条目就让整条黑名单静默变空集。
    /// </summary>
    public static bool TryParseCidr(string cidr, out IPAddress? network, out int bits)
    {
        network = null;
        bits = 0;
        var text = cidr.Trim();
        var slash = text.IndexOf('/');
        var addressPart = slash < 0 ? text : text[..slash];
        if (!IPAddress.TryParse(addressPart.Trim(), out network)) return false;
        var maxBits = network.GetAddressBytes().Length * 8;
        if (slash < 0)
        {
            bits = maxBits;
            return true;
        }
        if (!int.TryParse(text[(slash + 1)..].Trim(), out bits) || bits < 0 || bits > maxBits) return false;
        return true;
    }

    private static bool Matches(IPAddress ip, string cidr)
    {
        if (!TryParseCidr(cidr, out var net, out var bits) || net is null) return false;
        if (net.AddressFamily != ip.AddressFamily) return false;
        var ipBytes = ip.GetAddressBytes();
        var netBytes = net.GetAddressBytes();
        var fullBytes = bits / 8;
        var remBits = bits % 8;
        for (var i = 0; i < fullBytes; i++)
            if (ipBytes[i] != netBytes[i]) return false;
        if (remBits > 0)
        {
            var mask = 0xFF << (8 - remBits) & 0xFF;
            if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) return false;
        }
        return true;
    }

    /// <summary>
    /// 带围栏的 <see cref="SocketsHttpHandler"/>:禁跟随重定向;<b>禁用代理</b>;连接期对<b>解析后的每个 IP</b>
    /// 复检黑名单,全部命中即拒连;连接池 5 分钟轮换(长命 HttpClient 的 DNS 陈旧问题由此解决,不需要 IHttpClientFactory)。
    /// <para><b>为什么必须禁代理</b>:默认 <c>UseProxy=true</c> 会走 <c>HttpClient.DefaultProxy</c>——它由
    /// <c>HTTP_PROXY</c>/<c>HTTPS_PROXY</c> 环境变量(Windows 上还有系统代理)构造。有代理时 ConnectCallback
    /// 只看得见代理的 IP,真实目标由代理去解析去连,IP 围栏整个归零(实测能经代理取回云元数据)。
    /// 消费者若确需经代理出网,请在代理侧另行限制目标——本客户端不提供代理选项,正是因为一开代理围栏就失效。</para>
    /// <para>注意:配置在此一次性捕获(<see cref="JobHttpClient"/> 是单例),改 <c>BlockedCidrs</c> 要重启才生效;
    /// 上面那句「5 分钟轮换」说的只是 DNS 缓存,不是配置热更。</para>
    /// </summary>
    public static SocketsHttpHandler CreateHandler(AdminJobsHttpOptions http) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        Proxy = null,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(a => !IsBlocked(a, http.BlockedCidrs)).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException(
                    $"目标 {context.DnsEndPoint.Host} 解析后的地址全部命中围栏黑名单(DNS rebinding 防护,47009 语义)");
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
}
