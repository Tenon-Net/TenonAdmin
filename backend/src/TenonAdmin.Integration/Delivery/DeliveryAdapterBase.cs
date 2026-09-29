namespace TenonAdmin.Integration;

/// <summary>
/// 可靠投递适配器基类:在 <see cref="OutboundAdapterBase"/>(地址安全、超时、追踪、凭据、分类、记录)之上补齐 <see cref="IDeliveryAdapter"/>。
/// 消费者实现 <see cref="Name"/>、目标名与 <see cref="SendAsync"/>;对方支持去重/查询时覆写对应声明与 <see cref="QueryAsync"/>。
/// </summary>
/// <example>
/// <code>
/// public class TicketDeliveryAdapter(OutboundAdapterServices services) : DeliveryAdapterBase(services)
/// {
///     public override string Name =&gt; "partner-ticket";
///     protected override string TargetName =&gt; "partner";
///     public override bool SupportsIdempotency =&gt; true;   // 对方按 Idempotency-Key 去重
///     public override async Task&lt;DeliverySendResult&gt; SendAsync(DeliveryContext context, CancellationToken ct)
///     {
///         var response = await PostJsonAsync("tickets", context.ReadPayload&lt;TicketPayload&gt;(), context.SendOptions, ct);
///         return DeliverySendResult.From(response, ReadJson&lt;TicketCreated&gt;(response)?.TicketNo);
///     }
/// }
/// </code>
/// </example>
public abstract class DeliveryAdapterBase(OutboundAdapterServices services) : OutboundAdapterBase(services), IDeliveryAdapter
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual bool SupportsIdempotency => false;

    /// <inheritdoc />
    public virtual bool SupportsQuery => false;

    /// <inheritdoc />
    public abstract Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"适配器 {Name} 未声明可查询。");
}
