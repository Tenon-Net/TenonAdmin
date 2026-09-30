using System.Net.Http.Json;
using System.Text.Json;

namespace TenonAdmin.Integration;

/// <summary>一次适配器调用的可选项。</summary>
public sealed record OutboundCallOptions
{
    /// <summary>业务操作名(写进调用记录)。</summary>
    public string? Operation { get; init; }

    /// <summary>幂等标识(按目标的 <c>IdempotencyHeader</c> 发送)。</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>附加请求头。</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>覆盖目标超时。</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>关联投递记录(可靠投递场景由框架填写)。</summary>
    public long? DeliveryId { get; init; }

    /// <summary>关联投递尝试序号。</summary>
    public int? AttemptNo { get; init; }
}

/// <summary>
/// 适配器依赖的框架服务聚合(DI 注册)。适配器构造只收这一个参数,框架以后增加依赖不会破坏消费者的构造函数。
/// </summary>
public sealed class OutboundAdapterServices(
    IOutboundHttpInvoker invoker,
    IOutboundTargetRegistry targets,
    IOutboundAuthenticator authenticator,
    IOutboundCredentialProvider credentials)
{
    /// <summary>出站调用入口。</summary>
    public IOutboundHttpInvoker Invoker { get; } = invoker;

    /// <summary>目标注册表。</summary>
    public IOutboundTargetRegistry Targets { get; } = targets;

    /// <summary>默认认证(按目标配置)。</summary>
    public IOutboundAuthenticator Authenticator { get; } = authenticator;

    /// <summary>秘密来源(签名类认证用)。</summary>
    public IOutboundCredentialProvider Credentials { get; } = credentials;
}

/// <summary>
/// 第三方适配器基类:消费者只写对方协议(路径、载荷、结果含义)与业务映射,
/// 目标地址安全、超时/取消、追踪、凭据施加、结果分类与脱敏调用记录全部复用 <see cref="IOutboundHttpInvoker"/>。
/// <para>可覆写:<see cref="AuthenticateAsync"/>(签名、换取令牌等)、<see cref="Classify"/>(识别对方的业务受理/拒绝)、
/// <see cref="JsonOptions"/>(对方的 JSON 命名)。</para>
/// </summary>
/// <example>
/// <code>
/// public class PartnerClient(OutboundAdapterServices services) : OutboundAdapterBase(services)
/// {
///     protected override string TargetName => "partner";
///     public Task&lt;OutboundResponse&gt; GetPartnerAsync(string code, CancellationToken ct) =&gt;
///         GetAsync($"partners/{Uri.EscapeDataString(code)}", new() { Operation = "partner.get" }, ct);
/// }
/// </code>
/// </example>
public abstract class OutboundAdapterBase(OutboundAdapterServices services)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>框架服务。</summary>
    protected OutboundAdapterServices Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>目标名(对应配置 <c>Outbound:Targets:{name}</c>)。</summary>
    protected abstract string TargetName { get; }

    /// <summary>请求/响应 JSON 的序列化选项。默认 Web 命名(驼峰)。</summary>
    protected virtual JsonSerializerOptions JsonOptions => WebJson;

    /// <summary>发送一次请求(不重试);结果以分类表达,不抛网络异常。</summary>
    protected virtual Task<OutboundResponse> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, OutboundCallOptions? options = null, CancellationToken cancellationToken = default) =>
        Services.Invoker.SendAsync(new OutboundRequest
        {
            Target = TargetName,
            Method = method,
            Path = path,
            Content = content,
            Headers = options?.Headers,
            Operation = options?.Operation,
            IdempotencyKey = options?.IdempotencyKey,
            Timeout = options?.Timeout,
            DeliveryId = options?.DeliveryId,
            AttemptNo = options?.AttemptNo,
            Authenticate = AuthenticateAsync,
            Classify = Classify,
        }, cancellationToken);

    /// <summary>GET。</summary>
    protected Task<OutboundResponse> GetAsync(string path, OutboundCallOptions? options = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, options, cancellationToken);

    /// <summary>以 JSON 请求体 POST。</summary>
    protected Task<OutboundResponse> PostJsonAsync<T>(string path, T body, OutboundCallOptions? options = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, JsonBody(body), options, cancellationToken);

    /// <summary>以 JSON 请求体 PUT。</summary>
    protected Task<OutboundResponse> PutJsonAsync<T>(string path, T body, OutboundCallOptions? options = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, JsonBody(body), options, cancellationToken);

    /// <summary>DELETE。</summary>
    protected Task<OutboundResponse> DeleteAsync(string path, OutboundCallOptions? options = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, path, null, options, cancellationToken);

    /// <summary>把对象序列化为 JSON 请求体(使用 <see cref="JsonOptions"/>)。</summary>
    protected HttpContent JsonBody<T>(T body) => JsonContent.Create(body, options: JsonOptions);

    /// <summary>按 <see cref="JsonOptions"/> 解析响应体;无响应体、被截断或格式不符返回默认值。</summary>
    protected T? ReadJson<T>(OutboundResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.ReadJson<T>(JsonOptions);
    }

    /// <summary>
    /// 施加凭据。默认按目标配置(<see cref="IOutboundAuthenticator"/>);覆写实现签名、换取令牌等,
    /// 秘密用 <see cref="GetCredentialAsync"/> 取,缺失时抛 <see cref="OutboundCredentialUnavailableException"/>(请求不会发出)。
    /// </summary>
    protected virtual ValueTask AuthenticateAsync(HttpRequestMessage request, OutboundTarget target, CancellationToken cancellationToken) =>
        Services.Authenticator.ApplyAsync(request, target, cancellationToken);

    /// <summary>识别对方协议里的结果含义(如 200 响应体里的业务失败、自定义的受理状态);返回 null 用默认分类。</summary>
    protected virtual OutboundClassification? Classify(OutboundHttpResult result) => null;

    /// <summary>取目标的秘密;未配置时抛 <see cref="OutboundCredentialUnavailableException"/>。</summary>
    protected async ValueTask<OutboundCredential> GetCredentialAsync(OutboundTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var credential = await Services.Credentials.GetAsync(target, cancellationToken);
        return credential is { Secret.Length: > 0 }
            ? credential
            : throw new OutboundCredentialUnavailableException(target.Name, OutboundCredentialUnavailableException.MissingReason);
    }
}
