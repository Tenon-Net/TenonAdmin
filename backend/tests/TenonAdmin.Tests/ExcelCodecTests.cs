using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Excel;

namespace TenonAdmin.Tests;

/// <summary>
/// G2 卫星包 codec 测试:DI 前置替换 / 默认 Missing 抛 46001 / 模板真出 dataValidation。
/// 不启宿主(离线),与 <see cref="RedisCacheTests"/> 的 DI 段同型。
/// </summary>
public class ExcelCodecTests
{
    // ── DI ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 不调 <c>AddTenonAdminExcel()</c> 时,内核 TryAdd 的 <see cref="MissingExcelProvider"/> 生效;
    /// 调用模板构建必须抛 <see cref="ErrorCode.ExcelProviderMissing"/>(46001)——不是 500、不是 NRE、不是空流。
    /// </summary>
    [Fact]
    public async Task WithoutExcelPackage_TemplateBuilder_ThrowsExcelProviderMissing()
    {
        var services = new ServiceCollection();
        // 模拟 ServicesSetup 的内核默认注册
        services.TryAddSingleton<IExcelReader, MissingExcelProvider>();
        services.TryAddSingleton<IExcelWriter, MissingExcelProvider>();
        services.TryAddSingleton<IExcelTemplateBuilder, MissingExcelProvider>();

        using var sp = services.BuildServiceProvider();
        var builder = sp.GetRequiredService<IExcelTemplateBuilder>();
        Assert.IsType<MissingExcelProvider>(builder);

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            builder.BuildAsync(new TemplateSpec
            {
                Columns = [new ImportColumn { Key = "Account", Title = "登录账号" }],
            }));
        Assert.Equal(ErrorCode.ExcelProviderMissing, ex.Code);
        Assert.Equal(46001, (int)ex.Code);
    }

    /// <summary>
    /// 在内核 TryAdd <b>之前</b>调 <c>AddTenonAdminExcel()</c> → 解析到真实现(TryAdd 前置替换)。
    /// 变异:把 ExcelSetup 里的 TryAdd 注释掉或改到之后注册 → 本条必须红。
    /// </summary>
    [Fact]
    public void AddTenonAdminExcel_BeforeKernel_WinsTryAdd()
    {
        var services = new ServiceCollection();
        services.AddTenonAdminExcel(); // 前置
        services.TryAddSingleton<IExcelReader, MissingExcelProvider>();
        services.TryAddSingleton<IExcelWriter, MissingExcelProvider>();
        services.TryAddSingleton<IExcelTemplateBuilder, MissingExcelProvider>();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<MiniExcelReader>(sp.GetRequiredService<IExcelReader>());
        Assert.IsType<MiniExcelWriter>(sp.GetRequiredService<IExcelWriter>());
        Assert.IsType<OpenXmlTemplateBuilder>(sp.GetRequiredService<IExcelTemplateBuilder>());
    }

    // ── 模板 dataValidation ────────────────────────────────────────────

    /// <summary>
    /// 真生成带字典下拉的模板:落盘到仓库外目录,并沿完整引用链断言——
    /// <c>formula1</c> 引用的那张 sheet(经 workbook.xml + rels 解析,不假设 sheetN 编号)
    /// 才含字典 label,且该 sheet 在 workbook 里 <c>state="hidden"</c>。
    /// 变异:builder 把 label 写进别的 sheet 而让 formula 指向的 sheet 留空 → 本条必须红。
    /// </summary>
    [Fact]
    public async Task OpenXmlTemplateBuilder_WritesDataValidation_WithDictLabels()
    {
        const string maleLabel = "男";
        const string femaleLabel = "女";
        var artifactDir = Path.GetFullPath(@"C:\Project\HuHuHu\excel-artifacts");
        Directory.CreateDirectory(artifactDir);
        var outPath = Path.Combine(artifactDir, "user-template.xlsx");

        var builder = new OpenXmlTemplateBuilder();
        await using var stream = await builder.BuildAsync(new TemplateSpec
        {
            SheetName = "数据",
            Columns =
            [
                new ImportColumn { Key = "Account", Title = "登录账号", Required = true, Width = 18 },
                new ImportColumn { Key = "Name", Title = "姓名", Required = true, Width = 14 },
                new ImportColumn
                {
                    Key = "Gender",
                    Title = "性别",
                    DictTypeCode = "gender",
                    Hint = "下拉选择性别",
                    Width = 10,
                },
            ],
            DictOptions = new Dictionary<string, IReadOnlyList<string>>
            {
                ["Gender"] = [maleLabel, femaleLabel],
            },
        });

        await using (var fs = File.Create(outPath))
        {
            await stream.CopyToAsync(fs);
        }

        Assert.True(File.Exists(outPath), $"模板未落盘: {outPath}");
        Assert.True(new FileInfo(outPath).Length > 0);

        using var zip = ZipFile.OpenRead(outPath);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace relsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        // 1) 从数据 sheet 的 dataValidation.formula1 取出跨 sheet 引用
        string? formula1 = null;
        string? matchedSnippet = null;
        foreach (var entry in zip.Entries
                     .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                 && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var es = entry.Open();
            using var reader = new StreamReader(es, Encoding.UTF8);
            var xml = await reader.ReadToEndAsync();
            if (!xml.Contains("dataValidation", StringComparison.OrdinalIgnoreCase))
                continue;

            var doc = XDocument.Parse(xml);
            var dv = doc.Descendants(main + "dataValidation").FirstOrDefault()
                     ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "dataValidation");
            if (dv is null)
                continue;

            formula1 = dv.Descendants(main + "formula1").FirstOrDefault()?.Value
                       ?? dv.Descendants().FirstOrDefault(e => e.Name.LocalName == "formula1")?.Value;
            matchedSnippet = dv.ToString(SaveOptions.DisableFormatting);
            if (matchedSnippet.Length > 500)
                matchedSnippet = matchedSnippet[..500];
            break;
        }

        Assert.NotNull(matchedSnippet);
        Assert.Contains("dataValidation", matchedSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(formula1), "dataValidation 应有 formula1");

        // 形如 '_dict'!$A$1:$A$2 或 _dict!$A$1:$A$2
        var sheetRefMatch = Regex.Match(
            formula1!,
            @"^(?:'((?:[^']|'')+)'|([^'!]+))!",
            RegexOptions.CultureInvariant);
        Assert.True(sheetRefMatch.Success,
            $"formula1 应引用某张 sheet(got: {formula1})");
        var dictSheetName = (sheetRefMatch.Groups[1].Success
                ? sheetRefMatch.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal)
                : sheetRefMatch.Groups[2].Value)
            .Trim();
        Assert.False(string.IsNullOrEmpty(dictSheetName), $"未能从 formula1 解析 sheet 名: {formula1}");

        // 2) workbook.xml: sheet 名 → r:id,并断言 state=hidden(不假设 sheetN 编号)
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        Assert.NotNull(wbEntry);
        XDocument wbDoc;
        using (var wbStream = wbEntry!.Open())
            wbDoc = XDocument.Load(wbStream);

        var sheetEl = wbDoc.Descendants(main + "sheet")
                          .FirstOrDefault(s =>
                              string.Equals((string?)s.Attribute("name"), dictSheetName, StringComparison.Ordinal))
                      ?? wbDoc.Descendants()
                          .FirstOrDefault(e => e.Name.LocalName == "sheet"
                                               && string.Equals((string?)e.Attribute("name"), dictSheetName,
                                                   StringComparison.Ordinal));
        Assert.NotNull(sheetEl);
        Assert.Equal("hidden", (string?)sheetEl!.Attribute("state"), ignoreCase: true);

        var relId = (string?)sheetEl.Attribute(rNs + "id")
                    ?? (string?)sheetEl.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
        Assert.False(string.IsNullOrEmpty(relId),
            $"workbook sheet「{dictSheetName}」缺少 r:id");

        // 3) workbook.xml.rels: r:id → worksheet part 路径
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        Assert.NotNull(relsEntry);
        XDocument relsDoc;
        using (var relsStream = relsEntry!.Open())
            relsDoc = XDocument.Load(relsStream);

        var target = relsDoc.Descendants(relsNs + "Relationship")
                         .FirstOrDefault(r => (string?)r.Attribute("Id") == relId)
                         ?.Attribute("Target")?.Value
                     ?? relsDoc.Descendants()
                         .FirstOrDefault(e => e.Name.LocalName == "Relationship"
                                              && (string?)e.Attribute("Id") == relId)
                         ?.Attribute("Target")?.Value;
        Assert.False(string.IsNullOrEmpty(target),
            $"workbook.xml.rels 找不到 Id={relId} 的 Relationship");

        // Target 相对 xl/(如 worksheets/sheet2.xml);也容忍已带 xl/ 前缀
        var partPath = target!.Replace('\\', '/');
        if (partPath.StartsWith('/'))
            partPath = partPath.TrimStart('/');
        if (!partPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            partPath = "xl/" + partPath;

        var dictEntry = zip.GetEntry(partPath)
                        ?? zip.Entries.FirstOrDefault(e =>
                            string.Equals(e.FullName, partPath, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dictEntry);

        // 4) 公式指向的那张 sheet(不是包内任意 sheet)必须含字典 label
        string dictXml;
        using (var ds = dictEntry!.Open())
        using (var reader = new StreamReader(ds, Encoding.UTF8))
            dictXml = await reader.ReadToEndAsync();

        Assert.Contains(maleLabel, dictXml, StringComparison.Ordinal);
        Assert.Contains(femaleLabel, dictXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MiniExcelWriter_ThenReader_RoundTripsHeadersAndRows()
    {
        var writer = new MiniExcelWriter();
        await using var written = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "导出",
            Columns =
            [
                new ExportColumn { Key = "Account", Title = "登录账号" },
                new ExportColumn { Key = "Gender", Title = "性别" },
            ],
            Rows =
            [
                new Dictionary<string, object?> { ["Account"] = "alice", ["Gender"] = "男" },
                new Dictionary<string, object?> { ["Account"] = "bob", ["Gender"] = "女" },
            ],
        });

        var reader = new MiniExcelReader();
        // Write 返回的流已在 0;再读一次
        if (written.CanSeek) written.Position = 0;
        var headers = await reader.ReadHeadersAsync(written);
        Assert.Equal(["登录账号", "性别"], headers);

        if (written.CanSeek) written.Position = 0;
        var mapping = new Dictionary<string, string>
        {
            ["登录账号"] = "Account",
            ["性别"] = "Gender",
        };
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        await foreach (var row in reader.ReadRowsAsync(written, mapping))
            rows.Add(row);

        Assert.Equal(2, rows.Count);
        Assert.Equal("alice", rows[0]["Account"]);
        Assert.Equal("男", rows[0]["Gender"]);
        Assert.Equal("bob", rows[1]["Account"]);
    }
}
