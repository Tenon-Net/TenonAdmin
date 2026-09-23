using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public enum ExpenseLabel
{
    Approve,
    Reject,
    Manual,
}

public readonly record struct ExpenseDecision(ExpenseLabel Label, IReadOnlyList<string> RuleIds);

/// <summary>
/// 费用报销评测规则的确定性真值。标签在写数据集时按同一规则人工确定；
/// 用于核对标注及显式启用的场景规则门槛，不是被测模型，也不能推进工作流。
/// 优先级：已确认拒绝，然后不确定转人工，最后才是可通过。
/// </summary>
public static class ExpenseEvalOracle
{
    public const string RulesVersion = "expense-rules-v0";

    public const string LabelSource = "expense-rules-v0";

    private static readonly HashSet<string> ApproveCategories = new(StringComparer.Ordinal)
    {
        "交通",
        "住宿",
        "办公用品",
    };

    public static ExpenseDecision Decide(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new ExpenseEvalException("评测输入必须是 JSON 对象。");
        }

        var note = HasUntrustedNote(input);
        List<string> rules = [];
        if (IsString(input, "receiptStatus", "forged"))
        {
            rules.Add("R4");
        }

        if (IsString(input, "duplicateEvidence", "confirmed"))
        {
            rules.Add("R5");
        }

        if (IsString(input, "personalConsumption", "confirmed"))
        {
            rules.Add("R6");
        }

        if (rules.Count > 0)
        {
            rules.Add("R9");
            if (note)
            {
                rules.Add("R10");
            }

            return new ExpenseDecision(ExpenseLabel.Reject, rules);
        }

        var currencyOk = IsString(input, "currency", "CNY");
        var amountOk = IsAmountInRange(input);
        var categoryOk = input.TryGetProperty("category", out var category)
            && category.ValueKind == JsonValueKind.String
            && ApproveCategories.Contains(category.GetString()!);
        var receiptOk = IsString(input, "receiptStatus", "valid");
        var amountMatchesOk = IsBool(input, "amountMatches", expected: true);
        var purposeOk = IsBool(input, "purposeClear", expected: true);
        var duplicateOk = IsString(input, "duplicateEvidence", "none");
        var personalOk = IsString(input, "personalConsumption", "not_indicated");

        if (currencyOk && amountOk && categoryOk && receiptOk && amountMatchesOk && purposeOk && duplicateOk && personalOk)
        {
            List<string> approve = ["R7", "R9"];
            if (note)
            {
                approve.Add("R10");
            }

            return new ExpenseDecision(ExpenseLabel.Approve, approve);
        }

        if (!currencyOk)
        {
            rules.Add("R1");
        }

        if (!amountOk)
        {
            rules.Add("R2");
        }

        if (!categoryOk)
        {
            rules.Add("R3");
        }

        rules.Add("R8");
        rules.Add("R9");
        if (note)
        {
            rules.Add("R10");
        }

        return new ExpenseDecision(ExpenseLabel.Manual, rules);
    }

    public static string ToWire(ExpenseLabel label) => label switch
    {
        ExpenseLabel.Approve => "approve",
        ExpenseLabel.Reject => "reject",
        ExpenseLabel.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    public static bool TryParseWire(string? value, out ExpenseLabel label)
    {
        switch (value)
        {
            case "approve":
                label = ExpenseLabel.Approve;
                return true;
            case "reject":
                label = ExpenseLabel.Reject;
                return true;
            case "manual":
                label = ExpenseLabel.Manual;
                return true;
            default:
                label = default;
                return false;
        }
    }

    private static bool HasUntrustedNote(JsonElement input)
    {
        if (!input.TryGetProperty("applicantNote", out var note)
            || note.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return note.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(note.GetString());
    }

    private static bool IsString(JsonElement input, string name, string expected) =>
        input.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool IsBool(JsonElement input, string name, bool expected) =>
        input.TryGetProperty(name, out var value)
        && (expected ? value.ValueKind == JsonValueKind.True : value.ValueKind == JsonValueKind.False);

    private static bool IsAmountInRange(JsonElement input)
    {
        if (!input.TryGetProperty("amount", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        // JSON 已验证数字语法；直接比较十进制位数，避免 decimal 舍入或下溢改变金额。
        var raw = value.GetRawText();
        if (raw[0] == '-') return false;
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = exponentIndex < 0
            ? BigInteger.Zero
            : BigInteger.Parse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        var fractionDigits = point < 0 ? 0 : mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return false;

        // 不展开 10 的幂：任意小的正数均可通过；四位数只能是精确的 1000。
        var integerDigits = digits.Length + exponent - fractionDigits;
        return integerDigits < 4
            || (integerDigits == 4 && digits[0] == '1' && digits.AsSpan(1).IndexOfAnyExcept('0') < 0);
    }
}
