namespace TenonAdmin.Integration;

/// <summary>一次保留清理的结果(各类记录删除的行数)。</summary>
public sealed class IntegrationRetentionResult
{
    /// <summary>删除的开放调用记录行数。</summary>
    public int InboundLogs { get; init; }

    /// <summary>删除的出站调用记录行数。</summary>
    public int OutboundLogs { get; init; }

    /// <summary>删除的已完结投递记录条数(其尝试记录一并删除)。</summary>
    public int Deliveries { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"开放调用记录 {InboundLogs} 行,出站调用记录 {OutboundLogs} 行,已完结投递 {Deliveries} 条";
}

/// <summary>
/// 调用记录与投递记录的保留清理(实现契约 §9)。按保留期分批物理删除;<b>永不删除未解决的投递记录</b>。
/// <para>默认实现 <see cref="IntegrationRetentionService"/>,由每日任务 <see cref="IntegrationRetentionJob"/> 驱动;前置注册同接口即可替换。</para>
/// </summary>
public interface IIntegrationRetentionService
{
    /// <summary>执行一次清理。</summary>
    Task<IntegrationRetentionResult> CleanupAsync(CancellationToken cancellationToken = default);
}
