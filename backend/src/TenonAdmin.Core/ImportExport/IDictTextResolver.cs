namespace TenonAdmin.Core;

/// <summary>
/// 字典文本解析:<c>value ↔ label</c> 双向。<b>为什么单独一个接口</b>:字典表住在 Services 层
/// (<c>IDictService</c>),而 Core 与卫星包都看不见它;导入导出又必须在运行期查真字典
/// (不能用编译期特性,§2)。故在 Core 立此抽象,Services 用 <c>IDictService</c> 实现。
/// </summary>
public interface IDictTextResolver
{
    /// <summary>取某字典类型下"启用中"的全部项(value → label),按 Sort 升序。走 IDictService 的读穿透缓存。</summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetItemsAsync(string dictTypeCode, CancellationToken cancellationToken = default);

    /// <summary>value → label(导出用)。查不到返回原值,<b>不抛</b>——历史脏数据不该让整个导出失败。</summary>
    Task<string?> ToLabelAsync(string dictTypeCode, string? value, CancellationToken cancellationToken = default);

    /// <summary>label → value(导入用)。查不到返回 null,由调用方记 <see cref="ErrorCode.ImportCellDictInvalid"/>。
    /// 比对<b>去空白 + 大小写不敏感</b>(用户手敲的表格里"男 "和"男"必须同解)。</summary>
    Task<string?> ToValueAsync(string dictTypeCode, string? label, CancellationToken cancellationToken = default);
}
