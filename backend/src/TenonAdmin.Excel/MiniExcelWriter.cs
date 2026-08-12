using System.Data;
using MiniExcelLibs;
using TenonAdmin.Core;

namespace TenonAdmin.Excel;

/// <summary>
/// <see cref="TenonAdmin.Core.IExcelWriter"/> 的 MiniExcel 实现:把已完成字典翻译的 <see cref="ExportSheet"/> 写成 xlsx 流。
/// </summary>
public sealed class MiniExcelWriter : IExcelWriter
{
    /// <inheritdoc />
    public async Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        cancellationToken.ThrowIfCancellationRequested();

        // DataTable 能在 0 数据行时仍写出表头(纯 Dictionary 列表空时 MiniExcel 不知道列)
        var table = new DataTable(sheet.SheetName);
        foreach (var col in sheet.Columns)
            table.Columns.Add(col.Title, typeof(object));

        foreach (var row in sheet.Rows)
        {
            var dr = table.NewRow();
            foreach (var col in sheet.Columns)
            {
                if (row.TryGetValue(col.Key, out var val) && val is not null)
                    dr[col.Title] = val is string s ? (object?)ExcelSanitizer.Escape(s) ?? DBNull.Value : val;
                else
                    dr[col.Title] = DBNull.Value;
            }
            table.Rows.Add(dr);
        }

        var ms = new MemoryStream();
        await ms.SaveAsAsync(
            table,
            printHeader: true,
            sheetName: string.IsNullOrWhiteSpace(sheet.SheetName) ? "Sheet1" : sheet.SheetName,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        ms.Position = 0;
        return ms;
    }
}
