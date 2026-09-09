using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// AI proposal 的严格 JSON parser。它只接受固定的 v1.0 wire schema，拒绝未知、缺失或重复字段，
/// 并在进入 policy 前把所有不可信字符串和数组限制在小而明确的上限内。
/// </summary>
public class AiDecisionProposalParser : IAiDecisionProposalParser
{
    /// <summary>当前唯一支持的 proposal schema wire 版本。</summary>
    public const string SupportedSchemaVersion = "1.0";

    /// <summary>单个未受信任 proposal JSON 的最大 UTF-16 字符数。</summary>
    public const int MaximumJsonCharacters = 16_384;

    /// <summary>rationale 的最大字符数。</summary>
    public const int MaximumRationaleCharacters = 2_000;

    /// <summary>reasonCodes 数组的最大项数。</summary>
    public const int MaximumReasonCodeCount = 16;

    /// <summary>riskFlags 数组的最大项数。</summary>
    public const int MaximumRiskFlagCount = 16;

    /// <summary>evidence 数组的最大项数。</summary>
    public const int MaximumEvidenceCount = 16;

    /// <summary>单个 reason code 的最大字符数。</summary>
    public const int MaximumReasonCodeCharacters = 64;

    /// <summary>单个 risk flag 的最大字符数。</summary>
    public const int MaximumRiskFlagCharacters = 64;

    /// <summary>单个 evidence id 的最大字符数。</summary>
    public const int MaximumEvidenceIdCharacters = 128;

    /// <summary>单个 evidence source 的最大字符数。</summary>
    public const int MaximumEvidenceSourceCharacters = 128;

    /// <summary>sha256 evidence digest 的固定小写十六进制字符数（不含前缀）。</summary>
    public const int Sha256DigestCharacters = 64;

    private static readonly HashSet<string> RootPropertyNames = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "recommendation",
        "confidence",
        "reasonCodes",
        "rationale",
        "evidence",
        "riskFlags",
    };

    private static readonly HashSet<string> EvidencePropertyNames = new(StringComparer.Ordinal)
    {
        "id",
        "source",
        "contentHash",
    };

    /// <summary>
    /// 解析受限 proposal。输入不合法时只返回 <see cref="AiDecisionProposalParseResult.Invalid"/> 结果，
    /// 不把外部正文或异常信息带入 handler 结果。
    /// </summary>
    public virtual AiDecisionProposalParseResult Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaximumJsonCharacters)
        {
            return AiDecisionProposalParseResult.Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });

            return TryParse(document.RootElement, out var proposal)
                ? AiDecisionProposalParseResult.Valid(proposal)
                : AiDecisionProposalParseResult.Invalid();
        }
        catch (JsonException)
        {
            return AiDecisionProposalParseResult.Invalid();
        }
    }

    /// <summary>
    /// reason code 只接受大写 ASCII identifier：首字符 A-Z，后续可为 A-Z、0-9 或单个下划线，
    /// 不接受小写、连字符、首尾下划线或连续下划线。policy 以 Ordinal 精确匹配这些规范码。
    /// </summary>
    public static bool IsCanonicalReasonCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumReasonCodeCharacters
            || value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        var previousWasUnderscore = false;
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                previousWasUnderscore = false;
                continue;
            }

            if (character == '_' && !previousWasUnderscore)
            {
                previousWasUnderscore = true;
                continue;
            }

            return false;
        }

        return !previousWasUnderscore;
    }

    private static bool TryParse(JsonElement root, out AiDecisionProposal proposal)
    {
        proposal = null!;
        if (root.ValueKind != JsonValueKind.Object || !HasExactlyProperties(root, RootPropertyNames))
        {
            return false;
        }

        try
        {
            var schemaVersionElement = root.GetProperty("schemaVersion");
            if (schemaVersionElement.ValueKind != JsonValueKind.String
                || !string.Equals(schemaVersionElement.GetString(), SupportedSchemaVersion, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryParseRecommendation(root.GetProperty("recommendation"), out var recommendation)
                || !TryGetDecimalInClosedRange(root.GetProperty("confidence"), out var confidence)
                || !TryParseReasonCodes(root.GetProperty("reasonCodes"), out var reasonCodes)
                || !TryGetNonEmptyString(root.GetProperty("rationale"), MaximumRationaleCharacters, out var rationale)
                || !TryParseEvidence(root.GetProperty("evidence"), out var evidence)
                || !TryParseRiskFlags(root.GetProperty("riskFlags"), out var riskFlags))
            {
                return false;
            }

            proposal = AiDecisionProposal.Create(
                SupportedSchemaVersion,
                recommendation,
                confidence,
                reasonCodes,
                rationale,
                evidence,
                riskFlags);
            return true;
        }
        catch (ArgumentException)
        {
            proposal = null!;
            return false;
        }
    }

    private static bool HasExactlyProperties(JsonElement element, HashSet<string> expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == expected.Count;
    }

    private static bool TryParseRecommendation(JsonElement element, out AiDecisionRecommendation recommendation)
    {
        recommendation = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        recommendation = element.GetString() switch
        {
            "approve" => AiDecisionRecommendation.Approve,
            "reject" => AiDecisionRecommendation.Reject,
            "manual" => AiDecisionRecommendation.Manual,
            _ => default,
        };

        return recommendation is AiDecisionRecommendation.Approve
            or AiDecisionRecommendation.Reject
            or AiDecisionRecommendation.Manual;
    }

    private static bool TryGetDecimalInClosedRange(JsonElement element, out decimal confidence)
    {
        confidence = default;
        return element.ValueKind == JsonValueKind.Number
               && element.TryGetDecimal(out confidence)
               && confidence is >= 0m and <= 1m;
    }

    private static bool TryParseReasonCodes(JsonElement element, out List<string> reasonCodes)
    {
        reasonCodes = [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            reasonCodes.Add(item.GetString()!);
        }

        return true;
    }

    private static bool TryParseRiskFlags(JsonElement element, out List<string> riskFlags)
    {
        riskFlags = [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            riskFlags.Add(item.GetString()!);
        }

        return true;
    }

    private static bool TryParseEvidence(JsonElement element, out List<AiDecisionEvidence> evidence)
    {
        evidence = [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !HasExactlyProperties(item, EvidencePropertyNames)
                || item.GetProperty("id").ValueKind != JsonValueKind.String
                || item.GetProperty("source").ValueKind != JsonValueKind.String
                || item.GetProperty("contentHash").ValueKind != JsonValueKind.String)
            {
                return false;
            }

            evidence.Add(AiDecisionEvidence.Create(
                item.GetProperty("id").GetString()!,
                item.GetProperty("source").GetString()!,
                item.GetProperty("contentHash").GetString()!));
        }

        return true;
    }

    private static bool TryGetNonEmptyString(JsonElement element, int maximumLength, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength)
        {
            return false;
        }

        value = candidate;
        return true;
    }

}
