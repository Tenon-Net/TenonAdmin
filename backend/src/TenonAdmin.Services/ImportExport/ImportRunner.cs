using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IImportRunner"/> 默认实现:解析 → 映射 → 校验 → 判重 → 落库(excel-ledger §4.4)。
/// 对 xlsx 一无所知,只调 <see cref="IExcelReader"/>;各步 <c>protected virtual</c> 便于消费者覆写单步。
/// </summary>
public class ImportRunner(
    IExcelReader reader,
    IDictTextResolver dict,
    AdminExcelOptions? excel = null) : IImportRunner
{
    private AdminExcelOptions Excel => excel ?? new AdminExcelOptions();

    /// <inheritdoc />
    public virtual async Task<ImportPreview> PreviewAsync(
        Stream file,
        IReadOnlyDictionary<string, string>? mapping,
        IImportProfile profile,
        CancellationToken cancellationToken = default)
    {
        var headers = await reader.ReadHeadersAsync(file, cancellationToken);
        if (headers.Count == 0)
            throw new AdminException(ErrorCode.ImportFileEmpty);

        var effective = mapping ?? SuggestMapping(headers, profile.Columns);
        var columnErrors = CheckRequiredColumns(effective, profile.Columns);

        // 读流可能已前进;codec 若不可回绕需调用方重开流。MiniExcel 读头通常已消费流——
        // 约定 Preview 的 file 在 ReadHeaders 后若不可 Seek 则调用方应重开。可 Seek 时复位。
        if (file.CanSeek) file.Position = 0;

        var rows = new List<ImportRow>();
        var index = 0;
        await foreach (var cells in reader.ReadRowsAsync(file, effective, cancellationToken))
        {
            index++;
            if (index > Excel.MaxImportRows)
                throw new AdminException(ErrorCode.ImportRowLimitExceeded);

            rows.Add(new ImportRow
            {
                Index = index,
                Cells = cells.ToDictionary(kv => kv.Key, kv => kv.Value),
            });
        }

        await ValidateAllAsync(rows, profile, cancellationToken);

        return new ImportPreview
        {
            Headers = headers,
            Mapping = effective,
            Columns = profile.Columns,
            Rows = rows,
            Total = rows.Count,
            ErrorRows = rows.Count(r => r.Errors.Count > 0),
            ColumnErrors = columnErrors,
        };
    }

    /// <inheritdoc />
    public virtual async Task<ImportPreview> ValidateAsync(
        IReadOnlyList<ImportRow> rows,
        IImportProfile profile,
        CancellationToken cancellationToken = default)
    {
        // 拷一份可变行,避免调用方持有的列表与内部共用引用时的意外
        var working = rows.Select(r => new ImportRow
        {
            Index = r.Index,
            Cells = new Dictionary<string, string?>(r.Cells),
            Errors = [],
        }).ToList();

        await ValidateAllAsync(working, profile, cancellationToken);

        return new ImportPreview
        {
            Headers = [],
            Mapping = new Dictionary<string, string>(),
            Columns = profile.Columns,
            Rows = working,
            Total = working.Count,
            ErrorRows = working.Count(r => r.Errors.Count > 0),
            ColumnErrors = [],
        };
    }

    /// <inheritdoc />
    public virtual async Task<ImportCommitResult> CommitAsync(
        IReadOnlyList<ImportRow> rows,
        IImportProfile profile,
        DuplicateStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        // 坑 6:不信任前端送来的 Errors——重新完整校验,把送上来的 Errors 直接丢弃
        var working = rows.Select(r => new ImportRow
        {
            Index = r.Index,
            Cells = new Dictionary<string, string?>(r.Cells),
            Errors = [],
        }).ToList();

        await ValidateAllAsync(working, profile, cancellationToken);

        var result = new ImportCommitResult { Total = working.Count };
        var failures = new List<ImportRow>();

        foreach (var row in working)
        {
            var hardErrors = row.Errors.Where(e => e.Code != ErrorCode.ImportDuplicateInDb).ToList();
            if (hardErrors.Count > 0)
            {
                // 硬错误:部分提交跳过,进 Failures
                row.Errors = hardErrors.Concat(
                    row.Errors.Where(e => e.Code == ErrorCode.ImportDuplicateInDb)).ToList();
                failures.Add(row);
                result.Failed++;
                continue;
            }

            var dupInDb = row.Errors.Any(e => e.Code == ErrorCode.ImportDuplicateInDb);
            if (dupInDb)
            {
                switch (strategy)
                {
                    case DuplicateStrategy.Error:
                        failures.Add(row);
                        result.Failed++;
                        continue;
                    case DuplicateStrategy.Skip:
                        result.Skipped++;
                        continue;
                    case DuplicateStrategy.Overwrite:
                        try
                        {
                            await profile.CommitRowAsync(row, overwrite: true, cancellationToken);
                            result.Updated++;
                        }
                        catch (AdminException ex)
                        {
                            row.Errors =
                            [
                                new CellError(profile.BusinessKeys.FirstOrDefault() ?? "", ex.Code, ex.Args),
                            ];
                            failures.Add(row);
                            result.Failed++;
                        }
                        continue;
                }
            }

            try
            {
                await profile.CommitRowAsync(row, overwrite: false, cancellationToken);
                result.Inserted++;
            }
            catch (AdminException ex)
            {
                row.Errors =
                [
                    new CellError(profile.BusinessKeys.FirstOrDefault() ?? "", ex.Code, ex.Args),
                ];
                failures.Add(row);
                result.Failed++;
            }
        }

        result.Failures = failures;
        return result;
    }

    /// <summary>
    /// 唯一校验入口(Preview / Validate / Commit 共用,避免两条链漂移)。
    /// 顺序:必填 → 字典 label→value(成功则就地替换) → 文件内业务键重复 → profile 行级 → 库内判重标记。
    /// </summary>
    protected virtual async Task ValidateAllAsync(
        IList<ImportRow> rows,
        IImportProfile profile,
        CancellationToken cancellationToken)
    {
        foreach (var row in rows)
            row.Errors = [];

        // ── 通用:必填 + 字典 ──────────────────────────────────────────
        foreach (var row in rows)
        {
            foreach (var col in profile.Columns)
            {
                row.Cells.TryGetValue(col.Key, out var raw);
                if (col.Required && string.IsNullOrWhiteSpace(raw))
                {
                    row.Errors.Add(new CellError(col.Key, ErrorCode.ImportCellRequired));
                    continue;
                }

                if (col.DictTypeCode is { Length: > 0 } dtc && !string.IsNullOrWhiteSpace(raw))
                {
                    var value = await dict.ToValueAsync(dtc, raw, cancellationToken);
                    if (value is null)
                    {
                        // 本步是**幂等**的:预览已把 label 就地换成 value 并回给前端,前端改完错原样送回,
                        // 重验/提交会再走一遍这里 —— 此时 raw 已经是 value 而不是 label。
                        // 不认这种情况的话,任何字典列在预览通过后都会在「重新校验」和「提交」上被判 46006,
                        // 即整条向导对带字典列的档案完全不可用(浏览器实走发现,单测因手工造行而漏掉)。
                        var items = await dict.GetItemsAsync(dtc, cancellationToken);
                        if (items.Any(kv => string.Equals(kv.Key, raw.Trim(), StringComparison.OrdinalIgnoreCase)))
                            value = raw.Trim();
                    }

                    if (value is null)
                        row.Errors.Add(new CellError(col.Key, ErrorCode.ImportCellDictInvalid));
                    else
                        row.Cells[col.Key] = value;   // 就地 label→value,Commit 拿到的已是字典 value
                }
            }
        }

        // ── 通用:业务键在文件内重复(第 2 次及以后记错) ────────────────
        if (profile.BusinessKeys.Count > 0)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var bk = BuildBusinessKey(row, profile.BusinessKeys);
                if (bk is null) continue;
                if (seen.ContainsKey(bk))
                    row.Errors.Add(new CellError(
                        profile.BusinessKeys[0], ErrorCode.ImportDuplicateInFile));
                else
                    seen[bk] = row.Index;
            }
        }

        // ── profile 行级:外键/越权/跨列 ────────────────────────────────
        foreach (var row in rows)
        {
            var custom = await profile.ValidateRowAsync(row, cancellationToken);
            if (custom.Count > 0)
                row.Errors.AddRange(custom);
        }

        // ── 库内判重(一次查完;仅作标记,是否算错由 Commit 策略定) ──────
        if (profile.BusinessKeys.Count > 0)
        {
            var keys = rows
                .Select(r => BuildBusinessKey(r, profile.BusinessKeys))
                .Where(k => k is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (keys.Count > 0)
            {
                var existing = await profile.FindExistingKeysAsync(keys, cancellationToken);
                foreach (var row in rows)
                {
                    var bk = BuildBusinessKey(row, profile.BusinessKeys);
                    if (bk is not null && existing.Contains(bk))
                        row.Errors.Add(new CellError(
                            profile.BusinessKeys[0], ErrorCode.ImportDuplicateInDb));
                }
            }
        }
    }

    /// <summary>
    /// 表头模糊匹配 → 映射:去空白/去尾部 <c>*「」()</c> 后精确比 Title,再比 Key(大小写不敏感)。
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string> SuggestMapping(
        IReadOnlyList<string> headers, IReadOnlyList<ImportColumn> columns)
    {
        var map = new Dictionary<string, string>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var norm = NormalizeHeader(header);
            if (norm.Length == 0) continue;

            ImportColumn? hit = columns.FirstOrDefault(c =>
                string.Equals(NormalizeHeader(c.Title), norm, StringComparison.Ordinal));
            hit ??= columns.FirstOrDefault(c =>
                string.Equals(c.Key, norm, StringComparison.OrdinalIgnoreCase));

            if (hit is null || !claimed.Add(hit.Key)) continue;
            map[header] = hit.Key;
        }
        return map;
    }

    /// <summary>必填列是否被某表头映射到;缺失记 <see cref="ErrorCode.ImportColumnMissing"/>。</summary>
    protected virtual IReadOnlyList<CellError> CheckRequiredColumns(
        IReadOnlyDictionary<string, string> mapping, IReadOnlyList<ImportColumn> columns)
    {
        var mappedKeys = mapping.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return columns
            .Where(c => c.Required && !mappedKeys.Contains(c.Key))
            .Select(c => new CellError(c.Key, ErrorCode.ImportColumnMissing))
            .ToList();
    }

    /// <summary>业务键拼接;任一键列为空则返回 null(不参与判重)。</summary>
    protected virtual string? BuildBusinessKey(ImportRow row, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return null;
        var parts = new string[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            row.Cells.TryGetValue(keys[i], out var v);
            if (string.IsNullOrWhiteSpace(v)) return null;
            parts[i] = v.Trim();
        }
        return string.Join('\u001f', parts);
    }

    /// <summary>去空白与尾部 <c>*「」()（）</c>,供表头模糊匹配。</summary>
    protected virtual string NormalizeHeader(string header)
    {
        var t = header.Trim();
        while (t.Length > 0)
        {
            var c = t[^1];
            if (c is '*' or '「' or '」' or '（' or '）' or '(' or ')' or ' ')
                t = t[..^1].TrimEnd();
            else
                break;
        }
        return t;
    }
}
