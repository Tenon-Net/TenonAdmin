using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>把未受信任的实例变量收敛为 AI Provider 可见的确定性安全快照。</summary>
internal static class AiDecisionSafeInputProjector
{
    internal const int MaximumInstructionCharacters = 2000;
    internal const int MaximumInputFieldCount = 32;
    internal const int MaximumInputFieldCharacters = 64;
    internal const int MaximumVariablesJsonCharacters = 65_536;
    internal const int MaximumDepth = 8;
    internal const int MaximumValueCount = 256;
    internal const int MaximumTextCharacters = 4096;
    internal const int MaximumCanonicalInputBytes = 32_768;

    private const string Redacted = "***";
    private static readonly string[] SensitiveNames =
        ["password", "pwd", "secret", "token", "credential", "header", "authorization", "apikey", "api_key", "cookie"];

    internal static Projection Create(WfNodeProps? props, string? variablesJson)
    {
        var instructions = props?.AiInstructions;
        ValidateInstructions(instructions);
        var fields = ValidateFields(props?.AiInputFields);
        if (variablesJson is null || variablesJson.Length > MaximumVariablesJsonCharacters)
        {
            throw new ArgumentException("AI 输入变量无效。", nameof(variablesJson));
        }

        using var document = JsonDocument.Parse(variablesJson, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth + 2,
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("AI 输入变量根必须是对象。", nameof(variablesJson));
        }

        var valueCount = 0;
        ValidateTree(document.RootElement, depth: 0, ref valueCount);
        var root = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            foreach (var field in fields)
            {
                if (!root.TryGetValue(field, out var value))
                {
                    continue;
                }

                writer.WritePropertyName(field);
                WriteSafeValue(writer, field, value);
            }
            writer.WriteEndObject();
        }

        if (bytes.Length > MaximumCanonicalInputBytes)
        {
            throw new ArgumentException("AI 安全输入超过大小限制。", nameof(variablesJson));
        }

        var canonicalInputsJson = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
        using var safeDocument = JsonDocument.Parse(canonicalInputsJson);
        var inputs = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in safeDocument.RootElement.EnumerateObject())
        {
            inputs.Add(property.Name, property.Value.Clone());
        }

        return new Projection(
            instructions!,
            new ReadOnlyDictionary<string, JsonElement>(inputs),
            canonicalInputsJson,
            ComputeHash(instructions!, canonicalInputsJson));
    }

    internal static void Validate(AiDecisionProviderRequest request)
    {
        ValidateInstructions(request.Instructions, allowEmpty: true);
        if (request.CanonicalInputsJson.Length > MaximumCanonicalInputBytes
            || request.Inputs.Count > MaximumInputFieldCount
            || !string.Equals(SerializeInputs(request.Inputs), request.CanonicalInputsJson, StringComparison.Ordinal)
            || !string.Equals(ComputeHash(request.Instructions, request.CanonicalInputsJson), request.InputHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("AI Provider 安全输入快照无效。", nameof(request));
        }
    }

    internal static string Redact(string value) =>
        value.Any(character => char.IsControl(character) && character is not '\r' and not '\n')
        || IsSensitiveName(value)
        || IsSensitiveValue(value)
            ? Redacted
            : value;

    /// <summary>模型可控审计文本只保存稳定摘要，避免启发式脱敏漏掉姓名、地址或未知密钥格式。</summary>
    internal static string HashAuditValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static void ValidateInstructions(string? instructions, bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(instructions))
            || instructions is null
            || instructions.Length > MaximumInstructionCharacters
            || instructions.Any(character => char.IsControl(character) && character is not '\r' and not '\n')
            || IsSensitiveValue(instructions))
        {
            throw new ArgumentException("AI 节点指令无效。", nameof(instructions));
        }
    }

    private static string[] ValidateFields(IReadOnlyCollection<string>? fields)
    {
        if (fields is not { Count: > 0 } || fields.Count > MaximumInputFieldCount)
        {
            throw new ArgumentException("AI 输入白名单无效。", nameof(fields));
        }

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)
                || field.Length > MaximumInputFieldCharacters
                || !string.Equals(field, field.Trim(), StringComparison.Ordinal)
                || field.Any(char.IsControl)
                || IsSensitiveValue(field)
                || !result.Add(field))
            {
                throw new ArgumentException("AI 输入白名单无效。", nameof(fields));
            }
        }

        return [.. result];
    }

    private static void ValidateTree(JsonElement value, int depth, ref int count)
    {
        if (depth > MaximumDepth || ++count > MaximumValueCount)
        {
            throw new ArgumentException("AI 输入结构超过限制。", nameof(value));
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name.Length > MaximumInputFieldCharacters
                        || IsSensitiveValue(property.Name)
                        || !names.Add(property.Name))
                    {
                        throw new ArgumentException("AI 输入对象属性无效。", nameof(value));
                    }
                    ValidateTree(property.Value, depth + 1, ref count);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    ValidateTree(item, depth + 1, ref count);
                }
                break;
            case JsonValueKind.String when value.GetString()!.Length > MaximumTextCharacters:
                throw new ArgumentException("AI 输入文本超过限制。", nameof(value));
            case JsonValueKind.Undefined:
                throw new ArgumentException("AI 输入值无效。", nameof(value));
        }
    }

    private static void WriteSafeValue(Utf8JsonWriter writer, string? propertyName, JsonElement value)
    {
        if (propertyName is not null && IsSensitiveName(propertyName))
        {
            writer.WriteStringValue(Redacted);
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSafeValue(writer, property.Name, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteSafeValue(writer, propertyName: null, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                writer.WriteStringValue(IsSensitiveValue(text) ? Redacted : text);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveName(string name) =>
        SensitiveNames.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // 有限、确定性的值规则：常见认证头、JWT、PEM、含敏感键的 URI/连接串、邮箱及纯电话号码。
    private static bool IsSensitiveValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Basic ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("-----BEGIN ", StringComparison.Ordinal)
            || LooksLikeJwt(trimmed)
            || LooksLikeEmail(trimmed)
            || LooksLikePhone(trimmed))
        {
            return true;
        }

        return SensitiveNames.Any(keyword =>
            trimmed.Contains(keyword + "=", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(keyword + "%3D", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeJwt(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length >= 8 && part.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '='));
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at + 2) > at + 1;
    }

    private static bool LooksLikePhone(string value)
    {
        var digits = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character)) digits++;
            else if (character is not ('+' or '-' or ' ' or '(' or ')')) return false;
        }
        return digits is >= 7 and <= 15;
    }

    private static string SerializeInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            foreach (var item in inputs.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(item.Key);
                item.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }

    private static string ComputeHash(string instructions, string canonicalInputsJson)
    {
        using var inputs = JsonDocument.Parse(canonicalInputsJson);
        var envelope = JsonSerializer.Serialize(new { instructions, inputs = inputs.RootElement });
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope))).ToLowerInvariant();
    }

    internal sealed record Projection(
        string Instructions,
        IReadOnlyDictionary<string, JsonElement> Inputs,
        string CanonicalInputsJson,
        string InputHash);
}
