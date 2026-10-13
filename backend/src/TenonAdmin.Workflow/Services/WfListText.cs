using System.Text.Json;
using SqlSugar;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>列表和详情里给人看的姓名、表单摘要。不回退成原始 Id。</summary>
internal static class WfListText
{
    public const int MaxFields = 3;
    public const int MaxValueLength = 40;

    public static async Task<Dictionary<long, string>> UserNamesAsync(
        ISqlSugarClient db,
        IEnumerable<long> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var rows = await db.Queryable<SysUser>()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Name })
            .ToListAsync(cancellationToken);
        return rows
            .Where(user => !string.IsNullOrWhiteSpace(user.Name))
            .ToDictionary(user => user.Id, user => user.Name);
    }

    /// <summary>取前几个已填字段，带上表单标签。空值和对象不进摘要。</summary>
    public static string? Summarize(WfModel? model, string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
            return null;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(variablesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        var parts = new List<string>();
        var fields = model?.FormSchema?.Fields;
        if (fields is { Count: > 0 })
        {
            foreach (var field in fields)
            {
                if (parts.Count >= MaxFields) break;
                if (string.IsNullOrWhiteSpace(field.Key) || !root.TryGetProperty(field.Key, out var value))
                    continue;
                var text = FormatValue(value);
                if (text is null) continue;
                var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label.Trim();
                parts.Add($"{label}：{text}");
            }
        }
        else
        {
            foreach (var property in root.EnumerateObject())
            {
                if (parts.Count >= MaxFields) break;
                var text = FormatValue(property.Value);
                if (text is null) continue;
                parts.Add($"{property.Name}：{text}");
            }
        }

        return parts.Count == 0 ? null : string.Join("；", parts);
    }

    private static string? FormatValue(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "是",
            JsonValueKind.False => "否",
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= MaxValueLength ? text : string.Concat(text.AsSpan(0, MaxValueLength), "…");
    }
}
