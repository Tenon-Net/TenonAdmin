using System.Runtime.CompilerServices;
using MiniExcelLibs;
using TenonAdmin.Core;

namespace TenonAdmin.Excel;

/// <summary>
/// <see cref="TenonAdmin.Core.IExcelReader"/> 的 MiniExcel 实现:流式读表头与数据行,不整表 ToList。
/// </summary>
public sealed class MiniExcelReader : IExcelReader
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReadable(file);

        // useHeaderRow:false → 首行是数据,键为 A/B/C…,值为表头文本
        var first = MiniExcel.Query(file, useHeaderRow: false).Cast<IDictionary<string, object?>>().FirstOrDefault();
        if (first is null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        // 按列序(A,B,C…)取非空表头;尾部全空列丢弃
        var headers = first.Values
            .Select(v => v?.ToString()?.Trim() ?? string.Empty)
            .ToList();
        while (headers.Count > 0 && headers[^1].Length == 0)
            headers.RemoveAt(headers.Count - 1);

        return Task.FromResult<IReadOnlyList<string>>(headers);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
        Stream file,
        IReadOnlyDictionary<string, string> headerToKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureReadable(file);
        // MiniExcel.Query 是惰性流式 IEnumerable——逐行 yield,调用方边读边计上限
        foreach (var raw in MiniExcel.Query(file, useHeaderRow: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (raw is not IDictionary<string, object?> dict)
                continue;

            // 整行皆空 → 跳过(Excel 尾部常有空行)
            if (dict.Values.All(v => v is null || string.IsNullOrWhiteSpace(v.ToString())))
                continue;

            var row = new Dictionary<string, string?>(headerToKey.Count, StringComparer.Ordinal);
            foreach (var (header, key) in headerToKey)
            {
                if (dict.TryGetValue(header, out var cell) && cell is not null)
                {
                    var text = cell.ToString();
                    row[key] = string.IsNullOrWhiteSpace(text) ? null : text;
                }
                else
                {
                    row[key] = null;
                }
            }

            yield return row;
            // 让出一点调度点,便于取消令牌在密集读时生效
            await Task.Yield();
        }
    }

    private static void EnsureReadable(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.CanRead)
            throw new ArgumentException("导入流不可读。", nameof(file));
        if (file.CanSeek)
            file.Position = 0;
    }
}
