namespace TenonAdmin.Core;

/// <summary>导出档案:一个实体"能导哪些列"的声明。</summary>
public interface IExportProfile
{
    string Code { get; }
    IReadOnlyList<ExportColumn> Columns { get; }
}
