using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public sealed record ExpenseEvalCase(
    string CaseId,
    string Split,
    IReadOnlyList<string> Tags,
    string InputJson,
    ExpenseLabel Expected,
    IReadOnlyList<string> RuleIds,
    string Rationale);

public sealed class ExpenseEvalDataset
{
    private static readonly HashSet<string> AllowedFields = new(ExpenseEvalScenario.InputFields, StringComparer.Ordinal);
    private static readonly Regex CaseIdPattern = new("^ER-(D|H)-[0-9]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex MobilePattern = new("1[3-9][0-9]{9}", RegexOptions.CultureInvariant);
    private static readonly Regex IdentityPattern = new("[0-9]{17}[0-9Xx]", RegexOptions.CultureInvariant);

    private ExpenseEvalDataset(string path, IReadOnlyList<ExpenseEvalCase> cases)
    {
        Path = path;
        Cases = cases;
    }

    public string Path { get; }

    public IReadOnlyList<ExpenseEvalCase> Cases { get; }

    public static ExpenseEvalDataset Load(string path) => new(path, Read(path, officialShape: false));

    public static ExpenseEvalDataset LoadOfficial(string path) => new(path, Read(path, officialShape: true));

    public IReadOnlyList<ExpenseEvalCase> Select(string split) => split switch
    {
        "dev" => Cases.Where(item => item.Split == "dev").ToArray(),
        "holdout" => Cases.Where(item => item.Split == "holdout").ToArray(),
        "all" => Cases,
        _ => throw new ExpenseEvalException("split 只能是 dev、holdout 或 all。"),
    };

    private static IReadOnlyList<ExpenseEvalCase> Read(string path, bool officialShape)
    {
        if (!File.Exists(path))
        {
            throw new ExpenseEvalException("找不到评测数据集。");
        }

        List<ExpenseEvalCase> cases = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            cases.Add(ReadCase(rawLine, lineNumber, seen));
        }

        if (officialShape)
        {
            EnsureOfficialShape(cases);
        }

        return cases;
    }

    private static ExpenseEvalCase ReadCase(string line, int lineNumber, HashSet<string> seen)
    {
        EnsureNoDuplicateKeys(line, lineNumber);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            throw new ExpenseEvalException($"第 {lineNumber} 行不是合法 JSON。");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root, CaseProperties))
            {
                throw new ExpenseEvalException($"第 {lineNumber} 行的字段集合不正确。");
            }

            var caseId = RequiredString(root, "caseId", lineNumber);
            if (!CaseIdPattern.IsMatch(caseId) || !seen.Add(caseId))
            {
                throw new ExpenseEvalException($"caseId 缺失、非法或重复：{SafeToken(caseId)}。");
            }

            if (root.GetProperty("synthetic").ValueKind != JsonValueKind.True)
            {
                throw new ExpenseEvalException($"{caseId} 必须标记 synthetic=true。");
            }

            if (!string.Equals(RequiredString(root, "labelSource", lineNumber), ExpenseEvalOracle.LabelSource, StringComparison.Ordinal))
            {
                throw new ExpenseEvalException($"{caseId} 的标注来源必须是事先写定的规则。");
            }

            var split = RequiredString(root, "split", lineNumber);
            var prefix = caseId.StartsWith("ER-D-", StringComparison.Ordinal) ? "dev" : "holdout";
            if (split is not ("dev" or "holdout") || !string.Equals(split, prefix, StringComparison.Ordinal))
            {
                throw new ExpenseEvalException($"{caseId} 的 split 与编号不一致。");
            }

            var tags = ReadStringArray(root.GetProperty("tags"), caseId, "tags");
            if (tags.Count == 0 || tags.Count != tags.Distinct(StringComparer.Ordinal).Count()
                || tags.Any(tag => !ExpenseEvalScenario.RequiredTags.Contains(tag, StringComparer.Ordinal)))
            {
                throw new ExpenseEvalException($"{caseId} 的类别标签不正确。");
            }

            var input = root.GetProperty("input");
            if (input.ValueKind != JsonValueKind.Object)
            {
                throw new ExpenseEvalException($"{caseId} 的输入必须是对象。");
            }

            ValidateInput(input, caseId);
            var inputJson = input.GetRawText();
            using var inputDocument = JsonDocument.Parse(inputJson);
            var decision = ExpenseEvalOracle.Decide(inputDocument.RootElement);
            if (!ExpenseEvalOracle.TryParseWire(RequiredString(root, "expected", lineNumber), out var expected)
                || expected != decision.Label)
            {
                throw new ExpenseEvalException($"{caseId} 的期望结果与规则不一致。");
            }

            var ruleIds = ReadStringArray(root.GetProperty("ruleIds"), caseId, "ruleIds");
            if (!ruleIds.SequenceEqual(decision.RuleIds, StringComparer.Ordinal))
            {
                throw new ExpenseEvalException(
                    $"{caseId} 的规则编号与规则不一致：文件 {string.Join(',', ruleIds)}，规则 {string.Join(',', decision.RuleIds)}。");
            }

            var rationale = RequiredString(root, "rationale", lineNumber);
            if (rationale.Length > 500 || rationale.Any(char.IsControl))
            {
                throw new ExpenseEvalException($"{caseId} 的标注理由无效。");
            }

            var aligned = expected switch
            {
                ExpenseLabel.Approve => tags.Contains("approve-normal", StringComparer.Ordinal),
                ExpenseLabel.Reject => tags.Contains("reject-confirmed", StringComparer.Ordinal),
                _ => tags.Contains("manual-insufficient", StringComparer.Ordinal),
            };
            if (!aligned)
            {
                throw new ExpenseEvalException($"{caseId} 的类别标签与期望结果不一致。");
            }

            return new ExpenseEvalCase(caseId, split, tags, inputJson, expected, ruleIds, rationale);
        }
    }

    private static void ValidateInput(JsonElement input, string caseId)
    {
        var count = 0;
        foreach (var property in input.EnumerateObject())
        {
            count++;
            if (!AllowedFields.Contains(property.Name))
            {
                throw new ExpenseEvalException($"{caseId} 含有白名单之外的输入字段。");
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = property.Value.GetString()!;
                    if (text.Length is 0 or > 4096 || text.Any(char.IsControl) || IsUnsafeText(text))
                    {
                        throw new ExpenseEvalException($"{caseId} 的文本不能为空、过长或触发脱敏风险。");
                    }

                    if (property.Name == "applicantNote" && string.IsNullOrWhiteSpace(text))
                    {
                        throw new ExpenseEvalException($"{caseId} 的申请人说明为空。");
                    }

                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    break;
                default:
                    throw new ExpenseEvalException($"{caseId} 的输入只能是字符串、数字或布尔值。");
            }
        }

        if (count == 0)
        {
            throw new ExpenseEvalException($"{caseId} 的输入为空。");
        }
    }

    private static void EnsureOfficialShape(IReadOnlyList<ExpenseEvalCase> cases)
    {
        if (cases.Count != ExpenseEvalLimits.OfficialCaseCount
            || cases.Count(item => item.Split == "dev") != ExpenseEvalLimits.OfficialDevCount
            || cases.Count(item => item.Split == "holdout") != ExpenseEvalLimits.OfficialHoldoutCount)
        {
            throw new ExpenseEvalException("官方评测集必须是 8 条 dev 和 52 条 holdout。");
        }

        foreach (var tag in ExpenseEvalScenario.RequiredTags)
        {
            if (!cases.Any(item => item.Tags.Contains(tag, StringComparer.Ordinal)))
            {
                throw new ExpenseEvalException($"官方评测集缺少类别 {tag}。");
            }
        }

        if (!cases.Any(item => item.Tags.Contains("long-text", StringComparer.Ordinal) && NoteLength(item) >= 1000))
        {
            throw new ExpenseEvalException("官方评测集缺少长文本案例。");
        }

        foreach (var label in new[] { ExpenseLabel.Approve, ExpenseLabel.Reject, ExpenseLabel.Manual })
        {
            if (!cases.Any(item => item.Expected == label && NoteContains(item, "忽略")))
            {
                throw new ExpenseEvalException("提示注入必须同时覆盖通过、拒绝和转人工三类标注。");
            }
        }

        if (!cases.Any(item => item.Expected == ExpenseLabel.Reject && NoteContains(item, "Ignore previous instructions")))
        {
            throw new ExpenseEvalException("官方评测集缺少英文提示注入拒绝案例。");
        }
    }

    private static int NoteLength(ExpenseEvalCase item)
    {
        using var document = JsonDocument.Parse(item.InputJson);
        return document.RootElement.TryGetProperty("applicantNote", out var note) && note.ValueKind == JsonValueKind.String
            ? note.GetString()!.Length
            : 0;
    }

    private static bool NoteContains(ExpenseEvalCase item, string text)
    {
        using var document = JsonDocument.Parse(item.InputJson);
        return document.RootElement.TryGetProperty("applicantNote", out var note)
            && note.ValueKind == JsonValueKind.String
            && note.GetString()!.Contains(text, StringComparison.Ordinal);
    }

    private static bool IsUnsafeText(string value)
    {
        if (value.Contains('@')
            || value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("-----BEGIN ", StringComparison.Ordinal)
            || MobilePattern.IsMatch(value)
            || IdentityPattern.IsMatch(value))
        {
            return true;
        }

        var digits = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                digits++;
            }
            else if (character is not ('+' or '-' or ' ' or '(' or ')'))
            {
                return value.Contains("token=", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("password=", StringComparison.OrdinalIgnoreCase);
            }
        }

        return digits is >= 7 and <= 15;
    }

    private static List<string> ReadStringArray(JsonElement element, string caseId, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ExpenseEvalException($"{caseId} 的 {name} 必须是数组。");
        }

        List<string> values = [];
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ExpenseEvalException($"{caseId} 的 {name} 含有空值。");
            }

            values.Add(item.GetString()!);
        }

        if (values.Count != values.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ExpenseEvalException($"{caseId} 的 {name} 含有重复值。");
        }

        return values;
    }

    private static string RequiredString(JsonElement root, string name, int lineNumber)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ExpenseEvalException($"第 {lineNumber} 行的 {name} 无效。");
        }

        return value.GetString()!;
    }

    private static bool HasExactProperties(JsonElement element, IReadOnlySet<string> names)
    {
        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            count++;
            if (!names.Contains(property.Name))
            {
                return false;
            }
        }

        return count == names.Count;
    }

    private static void EnsureNoDuplicateKeys(string json, int lineNumber)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        try
        {
            var stack = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        stack.Pop();
                        break;
                    case JsonTokenType.PropertyName when !stack.Peek().Add(reader.GetString()!):
                        throw new ExpenseEvalException($"第 {lineNumber} 行含有重复字段。");
                }
            }
        }
        catch (JsonException)
        {
            throw new ExpenseEvalException($"第 {lineNumber} 行不是合法 JSON。");
        }
        catch (InvalidOperationException)
        {
            throw new ExpenseEvalException($"第 {lineNumber} 行不是合法 JSON。");
        }
    }

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(missing)";
        }

        var limited = value.Length > 64 ? value[..64] : value;
        return new string(limited.Where(character => !char.IsControl(character)).ToArray());
    }

    private static readonly HashSet<string> CaseProperties = new(StringComparer.Ordinal)
    {
        "caseId",
        "synthetic",
        "labelSource",
        "split",
        "tags",
        "input",
        "expected",
        "ruleIds",
        "rationale",
    };
}
