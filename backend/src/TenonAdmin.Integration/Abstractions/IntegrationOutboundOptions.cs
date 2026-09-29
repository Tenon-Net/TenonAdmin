using System.Net;
using System.Text.RegularExpressions;
using TenonAdmin.Services;
using static TenonAdmin.Integration.IntegrationOptionsValidation;

namespace TenonAdmin.Integration;

/// <summary>出站凭据的施加方式(对应 <c>Targets:{name}:Auth:Type</c>)。</summary>
public enum OutboundAuthType
{
    /// <summary>不附加凭据</summary>
    None = 0,

    /// <summary><c>Authorization: Bearer {secret}</c></summary>
    Bearer = 1,

    /// <summary>自定义请求头 <c>{HeaderName}: {secret}</c></summary>
    Header = 2,

    /// <summary><c>Authorization: Basic base64({Username}:{secret})</c></summary>
    Basic = 3,
}

/// <summary>普通出站调用配置(对应 <c>TenonAdmin:Integration:Outbound</c>)。</summary>
public class IntegrationOutboundOptions
{
    /// <summary>第三方目标(键 = 目标名,适配器按名引用;不区分大小写)。</summary>
    public Dictionary<string, OutboundTargetOptions> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>目标未单独设置时的调用超时(秒)。默认 30。</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>读入内存交给适配器解析的响应体上限(字节);超出部分丢弃并标记截断。默认 1 MB。</summary>
    public int MaxResponseBytes { get; set; } = 1_048_576;
}

/// <summary>一个第三方目标。秘密只经 <see cref="IOutboundCredentialProvider"/> 在调用时取得,不入库、不回显。</summary>
public class OutboundTargetOptions
{
    /// <summary>基础地址(http/https,不含 userinfo、查询串与片段),如 <c>https://erp.example.com/api/</c>。请求路径相对它解析,不能越出。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>调用超时(秒);空则用 <see cref="IntegrationOutboundOptions.DefaultTimeoutSeconds"/>。</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 受信网段(CIDR)。回环、私网、CGNAT 地址<b>默认拒绝</b>,只有解析结果落在这里列出的网段内才放行——
    /// 内部系统显式受信,不因「支持内网」而放开任意目标。链路本地(含云元数据)、未指定、组播与保留地址恒拒绝,列在这里也无效。
    /// </summary>
    public string[] TrustedCidrs { get; set; } = [];

    /// <summary>凭据施加方式。</summary>
    public OutboundAuthOptions Auth { get; set; } = new();

    /// <summary>
    /// 默认凭据来源读取的秘密(<c>Targets:{name}:Secret</c>)。经环境变量、用户机密或密钥管理配置提供方注入,
    /// 不要写进提交到代码库的配置文件。消费者前置注册 <see cref="IOutboundCredentialProvider"/> 后本项可不填。
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>传递幂等标识的请求头名(请求带幂等标识时使用)。默认 <c>Idempotency-Key</c>。</summary>
    public string IdempotencyHeader { get; set; } = "Idempotency-Key";
}

/// <summary>凭据施加方式配置。</summary>
public class OutboundAuthOptions
{
    /// <summary>施加方式,默认不附加凭据。</summary>
    public OutboundAuthType Type { get; set; } = OutboundAuthType.None;

    /// <summary><see cref="OutboundAuthType.Header"/> 时的请求头名。</summary>
    public string? HeaderName { get; set; }

    /// <summary><see cref="OutboundAuthType.Basic"/> 时的用户名。</summary>
    public string? Username { get; set; }
}

/// <summary>出站配置校验(由 <see cref="IntegrationOptionsValidation"/> 调用,非法值启动即抛)。</summary>
public static partial class OutboundOptionsValidation
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex TargetNamePattern();

    /// <summary>校验出站配置;消息指明目标与配置键,永不包含秘密。</summary>
    public static void Validate(IntegrationOutboundOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Require(options.DefaultTimeoutSeconds is > 0 and <= 600, "Outbound:DefaultTimeoutSeconds 须在 1..600 之间。");
        Require(options.MaxResponseBytes is > 0 and <= 64 * 1024 * 1024, "Outbound:MaxResponseBytes 须在 1..67108864 之间。");
        Require(options.Targets is not null, "Outbound:Targets 不能为空。");
        foreach (var (name, target) in options.Targets!)
        {
            var prefix = $"Outbound:Targets:{name}:";
            Require(TargetNamePattern().IsMatch(name ?? ""), $"Outbound:Targets 的目标名 \"{name}\" 须为 1–64 位字母、数字及 ._-。");
            Require(target is not null, prefix + " 不能为空。");
            Require(Uri.TryCreate(target!.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https",
                prefix + "BaseUrl 必须是 http/https 绝对地址。");
            Require(string.IsNullOrEmpty(uri!.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
                prefix + "BaseUrl 不能包含用户信息、查询串或片段(凭据请走 Auth 与 Secret)。");
            Require(target.TimeoutSeconds is null or (> 0 and <= 600), prefix + "TimeoutSeconds 须在 1..600 之间。");
            var trusted = target.TrustedCidrs ?? [];
            foreach (var cidr in trusted)
                Require(cidr is not null && JobHttpFence.TryParseCidr(cidr, out _, out _), prefix + $"TrustedCidrs 含非法网段 \"{cidr}\"。");
            // 字面 IP 的基础地址启动即判定;域名在每次建连时按解析结果复检
            Require(!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) || OutboundAddressGuard.IsAllowed(literal, trusted),
                prefix + "BaseUrl 指向被拒绝的地址(内部地址须列入 TrustedCidrs;链路本地、未指定、组播与保留地址恒拒绝)。");
            var auth = target.Auth ?? new OutboundAuthOptions();
            Require(Enum.IsDefined(auth.Type), prefix + "Auth:Type 取值非法。");
            Require(auth.Type != OutboundAuthType.Header || OutboundHeaders.IsValidName(auth.HeaderName),
                prefix + "Auth:Type=Header 时须提供合法的 Auth:HeaderName。");
            Require(auth.Type != OutboundAuthType.Basic || !string.IsNullOrWhiteSpace(auth.Username),
                prefix + "Auth:Type=Basic 时须提供 Auth:Username。");
            Require(OutboundHeaders.IsValidName(target.IdempotencyHeader), prefix + "IdempotencyHeader 不是合法的请求头名。");
        }
    }
}
