namespace TenonAdmin.Core;

/// <summary>
/// 三个 codec 接口的默认实现:一律抛 <see cref="ErrorCode.ExcelProviderMissing"/>。
/// <para>
/// 与 <c>NoopRealtimePublisher</c> 不同——实时是纯增强,关掉也能用;导入/导出没有 codec 就是不能用,
/// 所以这里刻意 fail-loud。装 <c>TenonAdmin.Excel</c> 并在 <c>AddTenonAdmin()</c> 之前调
/// <c>AddTenonAdminExcel()</c> 即经 TryAdd 前置替换接管(§5.2)。
/// </para>
/// <para>三个接口共用一个类:默认态下读者/写者/模板生成者是同一"未安装"语义,不必拆三个空壳。</para>
/// </summary>
public sealed class MissingExcelProvider : IExcelReader, IExcelWriter, IExcelTemplateBuilder
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default)
        => throw new AdminException(ErrorCode.ExcelProviderMissing);

    /// <inheritdoc />
    public IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
        Stream file, IReadOnlyDictionary<string, string> headerToKey, CancellationToken cancellationToken = default)
        => throw new AdminException(ErrorCode.ExcelProviderMissing);

    /// <inheritdoc />
    public Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default)
        => throw new AdminException(ErrorCode.ExcelProviderMissing);

    /// <inheritdoc />
    public Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default)
        => throw new AdminException(ErrorCode.ExcelProviderMissing);
}
