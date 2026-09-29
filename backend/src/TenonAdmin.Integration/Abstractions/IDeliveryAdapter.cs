using System.Text.Json;

namespace TenonAdmin.Integration;

/// <summary>一次投递调用交给适配器的上下文。</summary>
public sealed class DeliveryContext
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>投递记录 Id。</summary>
    public required long DeliveryId { get; init; }

    /// <summary>逻辑投递标识 = 幂等标识(所有尝试不变)。</summary>
    public required string DeliveryKey { get; init; }

    /// <summary>业务操作名。</summary>
    public required string Operation { get; init; }

    /// <summary>业务键。</summary>
    public string? BusinessKey { get; init; }

    /// <summary>业务载荷 JSON。</summary>
    public required string PayloadJson { get; init; }

    /// <summary>本次发送序号(查询时为当时的发送次数)。</summary>
    public int AttemptNo { get; init; }

    /// <summary>此前记录的对方引用(如受理时返回的对方单号)。</summary>
    public string? RemoteReference { get; init; }

    /// <summary>按 JSON 反序列化载荷(默认 Web 命名)。</summary>
    public T? ReadPayload<T>(JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(PayloadJson, options ?? WebJson);

    /// <summary>
    /// 发送用的调用选项:幂等标识 = <see cref="DeliveryKey"/>,并关联投递记录与序号(写进出站调用记录)。
    /// 适配器把它传给 <see cref="OutboundAdapterBase"/> 的发送方法即可;对方用别的方式传幂等标识时自行放进载荷或请求头。
    /// </summary>
    public OutboundCallOptions SendOptions => new()
    {
        Operation = Operation,
        IdempotencyKey = DeliveryKey,
        DeliveryId = DeliveryId,
        AttemptNo = AttemptNo,
    };

    /// <summary>查询用的调用选项(不带幂等标识头,关联投递记录)。</summary>
    public OutboundCallOptions QueryOptions => new()
    {
        Operation = Operation + ".query",
        DeliveryId = DeliveryId,
        AttemptNo = AttemptNo,
    };

    /// <summary>由投递记录构造(投递器与人工查询共用)。</summary>
    internal static DeliveryContext From(IntegrationDelivery row) => new()
    {
        DeliveryId = row.Id,
        DeliveryKey = row.DeliveryKey,
        Operation = row.Operation,
        BusinessKey = row.BusinessKey,
        PayloadJson = row.PayloadJson,
        AttemptNo = row.AttemptCount,
        RemoteReference = row.RemoteReference,
    };
}

/// <summary>一次发送的结果(由适配器从出站调用结果整理)。</summary>
/// <param name="Outcome">调用结果分类</param>
/// <param name="RemoteReference">对方返回的引用(如对方单号)</param>
/// <param name="Reason">原因短码</param>
/// <param name="ErrorSummary">错误摘要(不含秘密)</param>
/// <param name="RetryAfter">对方建议的重试间隔</param>
/// <param name="HttpStatus">HTTP 状态码</param>
/// <param name="CallId">出站调用 Id</param>
/// <param name="Transient">
/// 仅对 <see cref="OutboundOutcome.NotSent"/> 有意义:稍后重发是否可能成功。暂时性的(网络、限流)按退避自动重试;
/// 非暂时性的(凭据缺失、目标被拦截等配置问题)不自动重试,直接耗尽交人工
/// </param>
public sealed record DeliverySendResult(
    OutboundOutcome Outcome,
    string? RemoteReference = null,
    string? Reason = null,
    string? ErrorSummary = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    string? CallId = null,
    bool Transient = false)
{
    /// <summary>从出站调用结果整理(可附对方引用)。</summary>
    public static DeliverySendResult From(OutboundResponse response, string? remoteReference = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new DeliverySendResult(response.Outcome, remoteReference, response.Reason, response.ErrorSummary,
            response.RetryAfter, response.StatusCode, response.CallId, response.Transient);
    }
}

/// <summary>按幂等标识查询到的对方状态。</summary>
public enum RemoteDeliveryState
{
    /// <summary>对方已完成</summary>
    Succeeded = 0,

    /// <summary>对方已受理、尚未完成</summary>
    Accepted = 1,

    /// <summary>对方明确失败/拒绝</summary>
    Failed = 2,

    /// <summary>对方没有这条记录(可安全发送)</summary>
    NotFound = 3,

    /// <summary>查询本身失败,状态不明</summary>
    Unknown = 4,
}

/// <summary>一次查询的结果。</summary>
/// <param name="State">对方状态</param>
/// <param name="RemoteReference">对方引用</param>
/// <param name="Reason">原因短码</param>
/// <param name="HttpStatus">HTTP 状态码</param>
/// <param name="CallId">出站调用 Id</param>
public sealed record DeliveryQueryResult(
    RemoteDeliveryState State,
    string? RemoteReference = null,
    string? Reason = null,
    int? HttpStatus = null,
    string? CallId = null);

/// <summary>
/// 可靠投递适配器(实现契约 §8.5):消费者为每个「对方 + 协议」写一个,按 <see cref="Name"/> 被投递记录引用。
/// <para><b>去重责任</b>:框架保证同一逻辑投递的幂等标识在所有自动与人工尝试中不变,并随每次调用交给适配器,且不对结果未知的调用盲目重发;
/// 适配器负责把幂等标识按对方协议传出,<b>只有对方确实按该标识去重时</b>才声明 <see cref="SupportsIdempotency"/>;
/// 能按标识查询对方结果时才声明 <see cref="SupportsQuery"/>;区分「已受理」与「已完成」。框架不承诺任意第三方恰好执行一次。</para>
/// <para>一般继承 <see cref="DeliveryAdapterBase"/>(复用出站调用基础设施),以 <c>TryAddEnumerable</c> 注册为 scoped。</para>
/// </summary>
public interface IDeliveryAdapter
{
    /// <summary>适配器名(1–64 位字母数字及 <c>._-</c>;全局唯一,不区分大小写)。</summary>
    string Name { get; }

    /// <summary>对方按幂等标识去重:结果未知时可用同一标识安全重发。</summary>
    bool SupportsIdempotency { get; }

    /// <summary>能按幂等标识查询对方结果:结果未知时先查询再决定。</summary>
    bool SupportsQuery { get; }

    /// <summary>发送一次(不重试);网络与远端错误以结果分类表达,不抛异常。</summary>
    Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken);

    /// <summary>按幂等标识查询对方结果(仅在 <see cref="SupportsQuery"/> 为真时被调用)。</summary>
    Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken);
}
