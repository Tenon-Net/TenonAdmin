using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IDictTextResolver"/> 默认实现:经 <see cref="IDictService.GetItemsByTypeAsync"/> 读穿透缓存,
/// 做 value ↔ label 双向翻译(excel-ledger §4.2)。
/// <para>导出查不到返回原值不抛(历史脏数据);导入查不到返回 null,由 Runner 记
/// <see cref="ErrorCode.ImportCellDictInvalid"/>。</para>
/// </summary>
public class DictTextResolver(IDictService dict) : IDictTextResolver
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<KeyValuePair<string, string>>> GetItemsAsync(
        string dictTypeCode, CancellationToken cancellationToken = default)
    {
        var items = await dict.GetItemsByTypeAsync(dictTypeCode);
        return items.Select(i => KeyValuePair.Create(i.Value, i.Label)).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<string?> ToLabelAsync(
        string dictTypeCode, string? value, CancellationToken cancellationToken = default)
    {
        if (value is null) return null;
        var items = await dict.GetItemsByTypeAsync(dictTypeCode);
        var hit = items.FirstOrDefault(i => i.Value == value);
        return hit?.Label ?? value;   // 查不到返回原值,不抛
    }

    /// <inheritdoc />
    public virtual async Task<string?> ToValueAsync(
        string dictTypeCode, string? label, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var needle = label.Trim();
        var items = await dict.GetItemsByTypeAsync(dictTypeCode);
        // 去空白 + 大小写不敏感:用户手敲"男 "与"男"同解
        return items.FirstOrDefault(i =>
            string.Equals(i.Label.Trim(), needle, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
