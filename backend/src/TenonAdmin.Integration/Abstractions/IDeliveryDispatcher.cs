namespace TenonAdmin.Integration;

/// <summary>一轮投递扫描的统计(写进任务运行日志)。</summary>
public sealed class DeliveryDispatchSummary
{
    /// <summary>扫描到的候选记录数。</summary>
    public int Scanned { get; set; }

    /// <summary>
    /// 本轮由本节点拿下的记录数:领取成功的(发送、查询、中断恢复),加上无需调用、直接迁移生效的(截止、确认超时、次数上限等到期处理)。
    /// 领取或迁移未生效即已被其他节点抢先,跳过。
    /// </summary>
    public int Claimed { get; set; }

    /// <summary>发送次数。</summary>
    public int Sent { get; set; }

    /// <summary>查询次数(发送前核实、受理后确认、中断恢复)。</summary>
    public int Queried { get; set; }

    /// <summary>中断恢复次数(租约过期后夺回)。</summary>
    public int Recovered { get; set; }

    /// <summary>进入成功的记录数。</summary>
    public int Succeeded { get; set; }

    /// <summary>进入需要人工处理状态(待核对、耗尽、失败)的记录数。</summary>
    public int NeedAttention { get; set; }

    /// <summary>回写被栅栏挡住的迟到结果数(只追加尝试记录)。</summary>
    public int Late { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"扫描 {Scanned}、领取 {Claimed}、发送 {Sent}、查询 {Queried}、恢复 {Recovered}、成功 {Succeeded}、待人工 {NeedAttention}、迟到回写 {Late}";
}

/// <summary>
/// 可靠投递器(实现契约 §8.2–§8.4):扫描到期记录 → 短语句条件领取(栅栏 +1、租约)→ <b>事务外</b>调用适配器 → 短事务按栅栏回写。
/// 多副本同时扫描时由栅栏 CAS 保证单赢家;租约过期的「处理中」记录<b>不</b>视为可安全重发,按适配器能力恢复。
/// <para>默认实现 <see cref="DeliveryDispatcher"/>,由后台任务 <see cref="IntegrationDeliveryJob"/> 每 5 秒驱动;步骤 virtual。</para>
/// </summary>
public interface IDeliveryDispatcher
{
    /// <summary>扫描并处理一批到期记录。</summary>
    Task<DeliveryDispatchSummary> RunOnceAsync(CancellationToken cancellationToken = default);
}
