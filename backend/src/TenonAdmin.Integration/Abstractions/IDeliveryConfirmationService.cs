namespace TenonAdmin.Integration;

/// <summary>一次回调确认的处理结果。</summary>
public enum DeliveryConfirmOutcome
{
    /// <summary>已按回调更新记录</summary>
    Applied = 0,

    /// <summary>记录已是同一终局(重复回调),未做改动</summary>
    AlreadyApplied = 1,

    /// <summary>
    /// 没有可由本回调确认的记录:标识不存在、记录不属于回调声明的适配器或业务键、或从未发送过(不区分是哪一种,不透露记录是否存在)
    /// </summary>
    NotFound = 2,

    /// <summary>记录已被人工关闭或已确认为相反结果,未做改动(需人工查看)</summary>
    Rejected = 3,
}

/// <summary>一条告警:投递进入需要人工处理的状态。</summary>
/// <param name="DeliveryId">投递记录 Id</param>
/// <param name="DeliveryKey">投递标识</param>
/// <param name="Adapter">适配器</param>
/// <param name="Operation">业务操作</param>
/// <param name="Status">进入的状态(待核对、耗尽或失败)</param>
/// <param name="Reason">原因短码</param>
/// <param name="OccurredAt">发生时刻</param>
public sealed record DeliveryAlert(
    long DeliveryId,
    string DeliveryKey,
    string Adapter,
    string Operation,
    DeliveryStatus Status,
    string? Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// 一次回调确认。回调端点以接入应用身份调用,投递标识来自对方,所以必须声明本端点负责的范围:
/// 记录的适配器须在 <see cref="Adapters"/> 里,给了 <see cref="BusinessKey"/> 时业务键也须一致——否则按「没有这条记录」处理。
/// </summary>
public sealed class DeliveryConfirmation
{
    /// <summary>投递标识(即发给对方的幂等标识)。</summary>
    public required string DeliveryKey { get; init; }

    /// <summary>对方告知成功为 true,失败为 false。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>本回调端点负责的适配器(至少一个,不区分大小写)。</summary>
    public required IReadOnlyCollection<string> Adapters { get; init; }

    /// <summary>可选:记录须属于这个业务键(调用方已按接入应用的数据范围核实过该业务行)。</summary>
    public string? BusinessKey { get; init; }

    /// <summary>对方引用(可空)。</summary>
    public string? RemoteReference { get; init; }

    /// <summary>说明(可空,≤256)。</summary>
    public string? Note { get; init; }
}

/// <summary>
/// 异步受理的回调确认(实现契约 §8.6):消费者在自己的回调开放接口里,按对方告知的结果调用本服务。
/// 与投递器、人工操作同样按栅栏 CAS 更新;确认会让仍在进行中的调用在回写时成为「迟到结果」(只追加尝试记录)。
/// <para>默认实现 <see cref="DeliveryConfirmationService"/>,<c>TryAddScoped</c> 注册。</para>
/// </summary>
public interface IDeliveryConfirmationService
{
    /// <summary>按投递标识确认最终结果;不属于回调声明范围或从未发送过的记录返回 <see cref="DeliveryConfirmOutcome.NotFound"/>。</summary>
    /// <exception cref="ArgumentException">未声明任何适配器。</exception>
    Task<DeliveryConfirmOutcome> ConfirmAsync(DeliveryConfirmation confirmation, CancellationToken cancellationToken = default);
}

/// <summary>
/// 投递告警出口(集合,<c>TryAddEnumerable</c>):记录进入待核对、耗尽或失败时逐个调用。默认 <see cref="LoggingDeliveryAlertSink"/>
/// 输出结构化 Warning 日志;消费者追加邮件、即时通讯等实现即可。出口抛出的异常只记日志,不影响投递状态。
/// </summary>
public interface IDeliveryAlertSink
{
    /// <summary>发送一条告警。</summary>
    Task NotifyAsync(DeliveryAlert alert, CancellationToken cancellationToken = default);
}
