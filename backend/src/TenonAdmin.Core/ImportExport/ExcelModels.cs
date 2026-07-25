namespace TenonAdmin.Core;

/// <summary>导入列声明。</summary>
public sealed class ImportColumn
{
    /// <summary>列 Key = 属性名,如 <c>Account</c>。前端按它索引单元格。</summary>
    public required string Key { get; init; }
    /// <summary>表头文本(模板里写这个,也是列映射的匹配对象),如 <c>登录账号</c>。</summary>
    public required string Title { get; init; }
    /// <summary>必填(模板表头加 <c>*</c>,空值记 ImportCellRequired)。</summary>
    public bool Required { get; init; }
    /// <summary>非空 = 字典列:模板出下拉、导入 label→value、导出 value→label。</summary>
    public string? DictTypeCode { get; init; }
    /// <summary>「填写说明」sheet 的备注,如「填机构名称,如:深圳分公司」。</summary>
    public string? Hint { get; init; }
    /// <summary>模板列宽(字符数)。</summary>
    public int Width { get; init; } = 16;
}

/// <summary>导出列声明。</summary>
public sealed class ExportColumn
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    /// <summary>非空 = 字典列,导出时 value→label。</summary>
    public string? DictTypeCode { get; init; }
    /// <summary>前端「选列」弹窗的默认勾选态。</summary>
    public bool DefaultSelected { get; init; } = true;
    public int Width { get; init; } = 16;
}

/// <summary>
/// 一行导入数据。<b>单元格一律是字符串</b>——刻意不做泛型 <c>TRow</c>:数据来自表格本就是文本,
/// 又要在"预览→前端改错→重验"之间经 JSON 往返,字符串是唯一诚实的模型;类型转换在
/// <see cref="IImportProfile.CommitRowAsync"/> 里一次完成。这个决定省掉了整套泛型编排,别改回去。
/// </summary>
public sealed class ImportRow
{
    /// <summary>数据行号(1 起,对应 Excel 里表头之后的第几行)。前端展示用,错误定位靠它。</summary>
    public int Index { get; set; }
    /// <summary>列 Key → 单元格原文。</summary>
    public Dictionary<string, string?> Cells { get; set; } = [];
    /// <summary>服务端算出的错误;前端只读,提交时原样带回也会被服务端覆盖重算。</summary>
    public List<CellError> Errors { get; set; } = [];
}

/// <summary>单元格级错误。<b>只带码不带文案</b>(设计 §13.2):前端按 code 查 i18n 渲染。</summary>
public sealed record CellError(string ColumnKey, ErrorCode Code, IReadOnlyDictionary<string, object?>? Args = null);

/// <summary>预览/重验结果。</summary>
public sealed class ImportPreview
{
    /// <summary>文件里的原始表头(列映射 UI 的左侧)。重验时为空。</summary>
    public IReadOnlyList<string> Headers { get; set; } = [];
    /// <summary>实际生效的映射:表头文本 → 列 Key。前端据此回显并允许改。</summary>
    public IReadOnlyDictionary<string, string> Mapping { get; set; } = new Dictionary<string, string>();
    /// <summary>列定义(前端建预览表用,免得再拉一次档案)。</summary>
    public IReadOnlyList<ImportColumn> Columns { get; set; } = [];
    public IReadOnlyList<ImportRow> Rows { get; set; } = [];
    public int Total { get; set; }
    public int ErrorRows { get; set; }
    /// <summary>必填列没有被任何表头映射上 —— 这类错误不属于任何一行,单列在此。</summary>
    public IReadOnlyList<CellError> ColumnErrors { get; set; } = [];
}

/// <summary>重复处理策略。</summary>
public enum DuplicateStrategy
{
    /// <summary>跳过已存在的行(默认)。</summary>
    Skip = 0,
    /// <summary>覆盖已存在的行。</summary>
    Overwrite = 1,
    /// <summary>已存在即记为错误行,不导。</summary>
    Error = 2,
}

/// <summary>提交结果。</summary>
public sealed class ImportCommitResult
{
    public int Total { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    /// <summary>失败行(含原单元格 + 失败原因码),前端直接接回预览表继续改。</summary>
    public IReadOnlyList<ImportRow> Failures { get; set; } = [];
}

/// <summary>导出的一张表。</summary>
public sealed class ExportSheet
{
    public string SheetName { get; set; } = "Sheet1";
    public IReadOnlyList<ExportColumn> Columns { get; set; } = [];
    /// <summary>已完成字典翻译的行(列 Key → 单元格值)。codec 只负责写,不做业务转换。</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];
}

/// <summary>模板规格。</summary>
public sealed class TemplateSpec
{
    public string SheetName { get; set; } = "数据";
    public IReadOnlyList<ImportColumn> Columns { get; set; } = [];
    /// <summary>字典列的候选值(列 Key → label 列表),由调用方经 IDictTextResolver 备好;codec 只管写下拉。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DictOptions { get; set; }
        = new Dictionary<string, IReadOnlyList<string>>();
}
