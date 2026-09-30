using System.Net;
using TenonAdmin.Services;

namespace TenonAdmin.Integration;

/// <summary>目标地址被安全策略拒绝(静态校验,或建连时对解析结果的复检)。</summary>
public sealed class OutboundTargetBlockedException(string host)
    : HttpRequestException($"出站目标 {host} 的地址被安全策略拒绝(未受信的内部地址或恒拒网段)。");

/// <summary>
/// 出站目标地址安全(实现契约 §6.2),复用内核 <see cref="JobHttpFence"/> 的 CIDR 匹配,但默认比 HTTP 任务更严:
/// <list type="bullet">
/// <item>恒拒绝:未指定(<c>0.0.0.0/8</c>、<c>::</c>)、链路本地(<c>169.254.0.0/16</c> 含云元数据、<c>fe80::/10</c>)、组播、保留与广播段;</item>
/// <item>回环、私网、CGNAT、基准测试段、IPv6 唯一本地与站点本地:只有落在目标 <c>TrustedCidrs</c> 内才放行;</item>
/// <item>其余(公网)放行。NAT64 前缀 <c>64:ff9b::/96</c> 取出内嵌的 IPv4 再按上面规则判定,不能借它绕过。</item>
/// </list>
/// 建连时对 DNS 解析出的<b>每个</b>地址复检(覆盖 DNS 变化与 rebinding);自动重定向关闭,3xx 不跟随。
/// </summary>
public static class OutboundAddressGuard
{
    private static readonly string[] AlwaysBlocked =
        ["0.0.0.0/8", "169.254.0.0/16", "224.0.0.0/4", "240.0.0.0/4", "::/128", "fe80::/10", "ff00::/8"];

    private static readonly string[] InternalRanges =
    [
        "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "198.18.0.0/15",
        "::1/128", "fc00::/7", "fec0::/10",
    ];

    private static readonly string[] Nat64 = ["64:ff9b::/96"];

    /// <summary>该地址是否允许连接。</summary>
    public static bool IsAllowed(IPAddress address, IReadOnlyList<string> trustedCidrs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(trustedCidrs);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (JobHttpFence.IsBlocked(address, Nat64))
        {
            var bytes = address.GetAddressBytes();
            var embedded = new IPAddress(bytes[12..]);
            return IsAllowed(embedded, trustedCidrs);
        }
        if (JobHttpFence.IsBlocked(address, AlwaysBlocked)) return false;
        if (JobHttpFence.IsBlocked(address, InternalRanges))
            return JobHttpFence.IsBlocked(address, trustedCidrs as string[] ?? [.. trustedCidrs]);
        return true;
    }

    /// <summary>静态校验:仅 http/https、无 userinfo;主机是字面 IP 时立即按地址策略判定(域名留到建连时复检)。</summary>
    public static void Validate(Uri uri, IReadOnlyList<string> trustedCidrs)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new OutboundTargetBlockedException(uri.IsAbsoluteUri ? uri.Host : "(relative)");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && !IsAllowed(literal, trustedCidrs))
            throw new OutboundTargetBlockedException(uri.Host);
    }
}
