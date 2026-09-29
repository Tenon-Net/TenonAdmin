namespace TenonAdmin.Integration;

/// <summary>一次入队请求(实现契约 §7)。</summary>
public sealed record DeliveryRequest
{
    /// <summary>目标适配器名(须是已注册的 <see cref="IDeliveryAdapter.Name"/>)。</summary>
    public required string Adapter { get; init; }

    /// <summary>业务操作名(1–64 位字母数字及 <c>._:-</c>,如 <c>ticket.create</c>)。</summary>
    public required string Operation { get; init; }

    /// <summary>业务载荷对象(按 Web 默认命名序列化为 JSON)。与 <see cref="PayloadJson"/> 二选一。<b>不要放认证秘密。</b></summary>
    public object? Payload { get; init; }

    /// <summary>已序列化的业务载荷 JSON。与 <see cref="Payload"/> 二选一。</summary>
    public string? PayloadJson { get; init; }

    /// <summary>
    /// 稳定的逻辑投递标识(同时是发给对方的幂等标识,1–128 位可见 ASCII,如 <c>ticket-created:{id}</c>)。
    /// 空则生成 <c>dlv_{雪花号}</c>。同键重复入队:适配器、操作与载荷一致 → 返回既有记录;不一致 → 49042。
    /// </summary>
    public string? DeliveryKey { get; init; }

    /// <summary>业务键(便于从业务数据反查,≤128)。</summary>
    public string? BusinessKey { get; init; }

    /// <summary>覆盖发送次数上限(1–100)。</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>覆盖自动处理时限(1 分钟–30 天),超过即耗尽转人工。</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>最早处理时刻(延迟投递)。</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>
    /// 允许在没有活动事务时入队:记录独立提交,<b>不与任何业务写入原子</b>。默认 false——无事务即拒绝(49041),
    /// 防止误以为有原子性。调用时若恰在事务内,记录仍随该事务提交或回滚。
    /// </summary>
    public bool Standalone { get; init; }
}

/// <summary>入队结果。</summary>
/// <param name="Id">投递记录 Id</param>
/// <param name="DeliveryKey">投递标识(幂等标识)</param>
/// <param name="Created">是否新建;同键同内容的重复入队为 false</param>
public sealed record DeliveryEnqueueResult(long Id, string DeliveryKey, bool Created);

/// <summary>
/// 事务内投递入口(实现契约 §7)。在注入的同一个 <c>ISqlSugarClient</c> 上执行,<b>不另开连接或事务</b>,
/// 因此与调用方 <c>db.Ado.UseTranAsync(...)</c> 内的业务写入同属一个事务:提交则两者都在,回滚则两者都不在。
/// <para>跨库(业务在 <c>AdditionalDatabases</c> 副库)不属于同一事务,框架不承诺原子性——投递与业务请放同一个库。</para>
/// <para>默认实现 <see cref="DeliveryOutbox"/>,<c>TryAddScoped</c> 注册、步骤 virtual。</para>
/// </summary>
public interface IDeliveryOutbox
{
    /// <summary>入队一条待投递记录;校验失败抛业务异常(4904x),同键并发插入的唯一冲突原样抛给调用方事务。</summary>
    Task<DeliveryEnqueueResult> EnqueueAsync(DeliveryRequest request, CancellationToken cancellationToken = default);
}
