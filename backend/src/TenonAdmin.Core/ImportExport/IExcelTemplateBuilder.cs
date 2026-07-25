namespace TenonAdmin.Core;

/// <summary>导入模板生成(codec 层)。字典列要出真下拉,故此实现走 OpenXml 而非 MiniExcel(§2)。</summary>
public interface IExcelTemplateBuilder
{
    Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default);
}
