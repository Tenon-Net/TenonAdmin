using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TenonAdmin.Core;

namespace TenonAdmin.Excel;

/// <summary>
/// <see cref="IExcelTemplateBuilder"/> 的 OpenXml 实现。
/// <para>
/// 为什么不用 MiniExcel:它写不了 <c>dataValidation</c>(单元格下拉),而字典列真下拉正是模板的核心能力
/// (excel-ledger §2)。模板只有表头 + 说明,几十个单元格,直接用 SDK 建最干净。
/// </para>
/// </summary>
public sealed class OpenXmlTemplateBuilder : IExcelTemplateBuilder
{
    private const string InstructionSheetName = "填写说明";
    private const string DictOptionsSheetName = "_dict";
    private const uint DataRowCount = 1000; // 下拉覆盖的数据行数(表头之下)

    /// <inheritdoc />
    public Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var columns = spec.Columns ?? [];
        var dictOptions = spec.DictOptions ?? new Dictionary<string, IReadOnlyList<string>>();
        var dataSheetName = string.IsNullOrWhiteSpace(spec.SheetName) ? "数据" : spec.SheetName;

        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            // ── 数据 sheet:表头 + 列宽 + 字典下拉 ──────────────────────────
            var dataPart = wbPart.AddNewPart<WorksheetPart>();
            var dataSheetData = new SheetData();
            var headerRow = new Row { RowIndex = 1 };
            var columnsEl = new Columns();

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var colIndex = (uint)(i + 1);
                var letter = ColumnLetter(colIndex);
                var title = col.Required ? $"{col.Title}*" : col.Title;
                headerRow.AppendChild(TextCell($"{letter}1", title));

                columnsEl.AppendChild(new Column
                {
                    Min = colIndex,
                    Max = colIndex,
                    Width = col.Width > 0 ? col.Width : 16,
                    CustomWidth = true,
                });
            }

            dataSheetData.AppendChild(headerRow);

            var worksheet = new Worksheet();
            if (columns.Count > 0)
                worksheet.AppendChild(columnsEl);
            worksheet.AppendChild(dataSheetData);

            // 字典下拉:选项落隐藏 sheet,dataValidation 引用范围(避免 formula 255 字限制与逗号转义)
            var dictColumnIndexes = new List<(int colIndex, string key, IReadOnlyList<string> labels)>();
            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                if (string.IsNullOrEmpty(col.DictTypeCode))
                    continue;
                if (!dictOptions.TryGetValue(col.Key, out var labels) || labels is null || labels.Count == 0)
                    continue;
                dictColumnIndexes.Add((i + 1, col.Key, labels));
            }

            if (dictColumnIndexes.Count > 0)
            {
                // dataValidation 挂在 worksheet 上,须在 SheetData 之后
                var dvs = new DataValidations { Count = (uint)dictColumnIndexes.Count };
                for (var d = 0; d < dictColumnIndexes.Count; d++)
                {
                    var (colIndex, _, labels) = dictColumnIndexes[d];
                    var letter = ColumnLetter((uint)colIndex);
                    // 隐藏 sheet 第 d+1 列放该字典的 label 列表
                    var optCol = ColumnLetter((uint)(d + 1));
                    var lastRow = Math.Max(1, labels.Count);
                    var formula = $"'{DictOptionsSheetName}'!${optCol}$1:${optCol}${lastRow}";
                    var sqref = $"{letter}2:{letter}{DataRowCount + 1}";

                    dvs.AppendChild(new DataValidation
                    {
                        Type = DataValidationValues.List,
                        AllowBlank = true,
                        ShowErrorMessage = true,
                        ShowInputMessage = true,
                        // ShowDropDown 在 OOXML 里语义反转:true = 隐藏下拉,故不设
                        SequenceOfReferences = new ListValue<StringValue> { InnerText = sqref },
                        Formula1 = new Formula1(formula),
                    });
                }
                worksheet.AppendChild(dvs);
            }

            dataPart.Worksheet = worksheet;
            sheets.AppendChild(new Sheet
            {
                Id = wbPart.GetIdOfPart(dataPart),
                SheetId = sheetId++,
                Name = dataSheetName,
            });

            // ── 隐藏字典选项 sheet ─────────────────────────────────────────
            if (dictColumnIndexes.Count > 0)
            {
                var dictPart = wbPart.AddNewPart<WorksheetPart>();
                var dictSheetData = new SheetData();
                var maxLabels = dictColumnIndexes.Max(x => x.labels.Count);
                for (var r = 0; r < maxLabels; r++)
                {
                    var row = new Row { RowIndex = (uint)(r + 1) };
                    for (var d = 0; d < dictColumnIndexes.Count; d++)
                    {
                        var labels = dictColumnIndexes[d].labels;
                        if (r >= labels.Count) continue;
                        var letter = ColumnLetter((uint)(d + 1));
                        row.AppendChild(TextCell($"{letter}{r + 1}", labels[r]));
                    }
                    dictSheetData.AppendChild(row);
                }

                dictPart.Worksheet = new Worksheet(dictSheetData);
                sheets.AppendChild(new Sheet
                {
                    Id = wbPart.GetIdOfPart(dictPart),
                    SheetId = sheetId++,
                    Name = DictOptionsSheetName,
                    State = SheetStateValues.Hidden,
                });
            }

            // ── 「填写说明」sheet ──────────────────────────────────────────
            var helpPart = wbPart.AddNewPart<WorksheetPart>();
            var helpData = new SheetData();
            var helpHeader = new Row { RowIndex = 1 };
            helpHeader.AppendChild(TextCell("A1", "列名"));
            helpHeader.AppendChild(TextCell("B1", "必填"));
            helpHeader.AppendChild(TextCell("C1", "说明"));
            helpData.AppendChild(helpHeader);

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var rowIndex = (uint)(i + 2);
                var row = new Row { RowIndex = rowIndex };
                row.AppendChild(TextCell($"A{rowIndex}", col.Title));
                row.AppendChild(TextCell($"B{rowIndex}", col.Required ? "是" : "否"));
                var hint = col.Hint;
                if (!string.IsNullOrEmpty(col.DictTypeCode)
                    && dictOptions.TryGetValue(col.Key, out var labels)
                    && labels is { Count: > 0 })
                {
                    var sample = string.Join(" / ", labels.Take(8));
                    hint = string.IsNullOrEmpty(hint) ? $"下拉选择: {sample}" : $"{hint}(可选: {sample})";
                }
                row.AppendChild(TextCell($"C{rowIndex}", hint ?? string.Empty));
                helpData.AppendChild(row);
            }

            var helpColumns = new Columns(
                new Column { Min = 1, Max = 1, Width = 16, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 8, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 48, CustomWidth = true });
            helpPart.Worksheet = new Worksheet(helpColumns, helpData);
            sheets.AppendChild(new Sheet
            {
                Id = wbPart.GetIdOfPart(helpPart),
                SheetId = sheetId,
                Name = InstructionSheetName,
            });

            wbPart.Workbook.Save();
        }

        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    private static Cell TextCell(string cellRef, string text) =>
        new()
        {
            CellReference = cellRef,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text)),
        };

    /// <summary>1-based 列号 → A, B, … Z, AA …</summary>
    internal static string ColumnLetter(uint colIndex1Based)
    {
        if (colIndex1Based == 0)
            throw new ArgumentOutOfRangeException(nameof(colIndex1Based));
        var n = colIndex1Based;
        Span<char> buf = stackalloc char[8];
        var i = buf.Length;
        while (n > 0)
        {
            n--;
            buf[--i] = (char)('A' + (n % 26));
            n /= 26;
        }
        return new string(buf[i..]);
    }
}
