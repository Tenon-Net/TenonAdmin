using System.Text.Json;

namespace TenonAdmin.Integration;

/// <summary>
/// 出站调用结果分类(实现契约 §6.3)。基础调用层只分类、<b>从不自动重试</b>;是否重发由调用方(如可靠投递)按对方的去重/查询能力决定。
/// </summary>
public enum OutboundOutcome
{
    /// <summary>远端确认完成(2xx,202 除外)</summary>
    Succeeded = 0,

    /// <summary>远端已受理、最终结果待确认(202 或适配器识别的受理)——<b>不等于成功</b></summary>
    Accepted = 1,

    /// <summary>认证/授权失败(401/403),远端未执行</summary>
    AuthenticationFailed = 2,

    /// <summary>远端明确拒绝(其他 4xx、3xx、适配器识别的业务拒绝),未执行</summary>
    Rejected = 3,

    /// <summary>请求未被远端处理(DNS/连接/TLS 失败、建连超时、目标被拦截、凭据缺失、429)</summary>
    NotSent = 4,

    /// <summary>远端可能已执行也可能未执行(超时、连接中断、响应不完整、408、全部 5xx)</summary>
    Unknown = 5,

    /// <summary>调用方取消。请求可能已经发出,按结果未知对待</summary>
    Cancelled = 6,
}

/// <summary>一个已解析、已校验的出站目标(不含秘密)。</summary>
/// <param name="Name">目标名</param>
/// <param name="BaseUri">基础地址(以 <c>/</c> 结尾)</param>
/// <param name="Timeout">调用超时</param>
/// <param name="TrustedCidrs">受信网段</param>
/// <param name="AuthType">凭据施加方式</param>
/// <param name="AuthHeaderName">自定义凭据头名</param>
/// <param name="AuthUsername">Basic 用户名</param>
/// <param name="IdempotencyHeader">幂等标识请求头名</param>
public sealed record OutboundTarget(
    string Name,
    Uri BaseUri,
    TimeSpan Timeout,
    IReadOnlyList<string> TrustedCidrs,
    OutboundAuthType AuthType,
    string? AuthHeaderName,
    string? AuthUsername,
    string IdempotencyHeader);

/// <summary>出站秘密。<see cref="ToString"/> 永不输出秘密本身(防止被日志或异常消息带出)。</summary>
/// <param name="Secret">秘密</param>
/// <param name="Username">可选用户名(Basic;空则用目标配置的用户名)</param>
public sealed record OutboundCredential(string Secret, string? Username = null)
{
    /// <inheritdoc />
    public override string ToString() => "OutboundCredential { Secret = *** }";
}

/// <summary>远端 HTTP 响应的只读快照(供结果分类与适配器解析;只在内存中,不写进任何记录)。</summary>
/// <param name="StatusCode">HTTP 状态码</param>
/// <param name="Body">响应体(按上限截断;无响应体为 null)</param>
/// <param name="Headers">响应头(含内容头,键不区分大小写)</param>
public sealed record OutboundHttpResult(int StatusCode, string? Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>一次分类结论。</summary>
/// <param name="Outcome">结果分类</param>
/// <param name="Reason">机器可读原因(如 <c>http_422</c>、<c>business_rejected</c>),写进调用记录</param>
/// <param name="RetryAfter">远端建议的重试间隔</param>
/// <param name="Transient">仅对 <see cref="OutboundOutcome.NotSent"/> 有意义:稍后重发是否可能成功(网络、限流类为真;目标被拦截、凭据缺失等配置类为假)</param>
public readonly record struct OutboundClassification(
    OutboundOutcome Outcome,
    string? Reason = null,
    TimeSpan? RetryAfter = null,
    bool Transient = false);

/// <summary>一次出站调用请求。<see cref="Path"/> 相对目标基础地址解析,且不能越出它(防止借调用跳到任意主机或路径)。</summary>
public sealed class OutboundRequest
{
    /// <summary>目标名(对应配置 <c>Outbound:Targets:{name}</c>)。</summary>
    public required string Target { get; init; }

    /// <summary>HTTP 方法。GET/HEAD/OPTIONS 以外的方法按写操作对待:每次独立连接,不复用池化连接。</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>相对路径(可含查询串;查询串不会写进调用记录)。</summary>
    public required string Path { get; init; }

    /// <summary>请求体;空表示无请求体。</summary>
    public HttpContent? Content { get; init; }

    /// <summary>附加请求头(只允许可见 ASCII;不会写进调用记录)。<c>Content-*</c> 头施加到请求体上。</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>业务操作名(写进调用记录,便于排障,如 <c>ticket.create</c>)。</summary>
    public string? Operation { get; init; }

    /// <summary>幂等标识;给出即按目标的 <c>IdempotencyHeader</c> 发送(对方是否真按它去重,由适配器声明)。</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>覆盖目标超时。</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>关联的投递记录(可靠投递场景由框架填写)。</summary>
    public long? DeliveryId { get; init; }

    /// <summary>关联的投递尝试序号。</summary>
    public int? AttemptNo { get; init; }

    /// <summary>按调用覆盖认证(如签名、换取令牌);不给则用 <see cref="IOutboundAuthenticator"/>。</summary>
    public Func<HttpRequestMessage, OutboundTarget, CancellationToken, ValueTask>? Authenticate { get; init; }

    /// <summary>按调用覆盖结果分类;返回 null 回落到 <see cref="IOutboundResultClassifier"/>。</summary>
    public Func<OutboundHttpResult, OutboundClassification?>? Classify { get; init; }
}

/// <summary>一次出站调用的结果。<see cref="Body"/> 只在内存中交给调用方,不写进任何记录。</summary>
public sealed class OutboundResponse
{
    /// <summary>调用 Id(同时作为 <c>X-Request-Id</c> 发给对方,并写进调用记录,双方对账用)。</summary>
    public required string CallId { get; init; }

    /// <summary>目标名。</summary>
    public required string Target { get; init; }

    /// <summary>结果分类。</summary>
    public OutboundOutcome Outcome { get; init; }

    /// <summary>见 <see cref="OutboundClassification.Transient"/>。</summary>
    public bool Transient { get; init; }

    /// <summary>HTTP 状态码(没收到响应为空)。</summary>
    public int? StatusCode { get; init; }

    /// <summary>响应体(按上限截断)。</summary>
    public string? Body { get; init; }

    /// <summary>响应体超过上限被截断。</summary>
    public bool BodyTruncated { get; init; }

    /// <summary>响应头(含内容头)。</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>远端建议的重试间隔。</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>机器可读原因(如 <c>timeout</c>、<c>connection_failed</c>、<c>target_blocked</c>、<c>http_503</c>)。</summary>
    public string? Reason { get; init; }

    /// <summary>可读摘要(已截断,不含秘密与响应正文)。</summary>
    public string? ErrorSummary { get; init; }

    /// <summary>耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>是否成功或已受理(受理仍待最终确认)。</summary>
    public bool IsSuccess => Outcome is OutboundOutcome.Succeeded or OutboundOutcome.Accepted;

    /// <summary>把响应体按 JSON(Web 默认命名)反序列化;无响应体、被截断或格式不符返回默认值。</summary>
    public T? ReadJson<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(Body) || BodyTruncated) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(Body, options ?? JsonDefaults);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static readonly JsonSerializerOptions JsonDefaults = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// 出站调用的唯一入口(实现契约 §6):目标地址安全、超时/取消、追踪、凭据施加、结果分类与脱敏调用记录。
/// <b>从不自动重试</b>——写操作的重发由可靠投递按对方的去重/查询能力决定。
/// <para>默认实现 <see cref="OutboundHttpInvoker"/>,<c>TryAddScoped</c> 注册、方法 virtual;前置注册同接口即整体替换。</para>
/// </summary>
public interface IOutboundHttpInvoker
{
    /// <summary>
    /// 发送一次请求并分类。网络失败、远端错误与调用方取消都不抛异常,以 <see cref="OutboundResponse.Outcome"/> 表达;
    /// 只有调用方自身的编程错误(目标名为空、路径越出基础地址、请求头非法)抛 <see cref="ArgumentException"/>。
    /// </summary>
    Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default);
}

/// <summary>出站目标注册表(由配置构建,启动即校验)。</summary>
public interface IOutboundTargetRegistry
{
    /// <summary>全部目标(按名排序)。</summary>
    IReadOnlyList<OutboundTarget> Targets { get; }

    /// <summary>按名取目标(不区分大小写);未配置返回 null。</summary>
    OutboundTarget? Find(string name);
}

/// <summary>
/// 出站秘密来源(实现契约 §6.1)。默认 <see cref="ConfigurationOutboundCredentialProvider"/> 每次调用时读部署配置;
/// 消费者前置注册同接口即可换成密钥管理服务。秘密不入库、不进投递载荷、不回显。
/// </summary>
public interface IOutboundCredentialProvider
{
    /// <summary>取目标的秘密;未配置返回 null。</summary>
    ValueTask<OutboundCredential?> GetAsync(OutboundTarget target, CancellationToken cancellationToken = default);
}

/// <summary>把凭据施加到请求上(默认按目标 <c>Auth:Type</c>);适配器可按调用覆盖(签名、换取令牌等)。</summary>
public interface IOutboundAuthenticator
{
    /// <summary>施加凭据;必需的秘密缺失或含非法字符时抛 <see cref="OutboundCredentialUnavailableException"/>(请求不会发出)。</summary>
    ValueTask ApplyAsync(HttpRequestMessage request, OutboundTarget target, CancellationToken cancellationToken = default);
}

/// <summary>默认结果分类(实现契约 §6.3 表);适配器可按调用覆盖,或前置注册同接口整体替换。</summary>
public interface IOutboundResultClassifier
{
    /// <summary>对一次 HTTP 响应分类。</summary>
    OutboundClassification Classify(OutboundHttpResult result);
}

/// <summary>目标需要秘密但来源未提供或秘密不可用时抛出。请求不会发出,分类为不可自动重试的 <see cref="OutboundOutcome.NotSent"/>。</summary>
/// <param name="target">目标名</param>
/// <param name="reason">原因短码:<c>credential_missing</c> 或 <c>credential_invalid</c></param>
public sealed class OutboundCredentialUnavailableException(string target, string reason)
    : InvalidOperationException(reason == MissingReason ? $"出站目标 {target} 未配置凭据。" : $"出站目标 {target} 的凭据含请求头不允许的字符。")
{
    /// <summary>未配置秘密。</summary>
    public const string MissingReason = "credential_missing";

    /// <summary>秘密含请求头不允许的字符。</summary>
    public const string InvalidReason = "credential_invalid";

    /// <summary>目标名。</summary>
    public string Target { get; } = target;

    /// <summary>原因短码。</summary>
    public string Reason { get; } = reason;
}

/// <summary>出站请求头的合法性规则(名为 RFC 9110 token;值只允许可见 ASCII 与空格、制表符——拦 CRLF 注入,也避免非 ASCII 在发送时才失败)。</summary>
public static class OutboundHeaders
{
    private const string Separators = "()<>@,;:\\\"/[]?={} \t";

    /// <summary>请求头名是否合法。</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => c > 0x20 && c < 0x7F && !Separators.Contains(c));

    /// <summary>请求头值是否合法。</summary>
    public static bool IsValidValue(string? value) =>
        value is not null && value.All(c => c == '\t' || c is >= (char)0x20 and < (char)0x7F);
}
