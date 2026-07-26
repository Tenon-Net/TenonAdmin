namespace TenonAdmin.Core;

/// <summary>
/// xlsx 读取(codec 层)。内核只定义抽象、不带任何 Excel 实现(运行时依赖纪律:仅 SqlSugarCore + Microsoft.*);
/// 默认实现 <c>MissingExcelProvider</c> 一律抛 <see cref="ErrorCode.ExcelProviderMissing"/>——
/// 这里刻意 fail-loud 而非静默(与 <c>NoopRealtimePublisher</c> 不同:实时是纯增强,导入没有 codec 就是不能用)。
/// 装 TenonAdmin.Excel 并在 AddTenonAdmin() 之前调 AddTenonAdminExcel() 即接管(TryAdd 前置替换,§5.2)。
/// </summary>
public interface IExcelReader
{
    /// <summary>只读首行表头(用于列映射建议),不读数据行。</summary>
    Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式读数据行。<paramref name="headerToKey"/> 把表头文本映射到列 Key;未映射的表头列丢弃。
    /// <b>必须流式</b>(不得先 ToList 再返回),行数上限由调用方边读边计,超限即停 —— 这样恶意大文件
    /// 打不爆内存(设计 §14 稳健性)。
    /// </summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
        Stream file, IReadOnlyDictionary<string, string> headerToKey, CancellationToken cancellationToken = default);
}
