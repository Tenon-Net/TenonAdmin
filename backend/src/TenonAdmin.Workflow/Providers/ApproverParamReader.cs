using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>从 <see cref="ApproverResolveContext.Params"/> 读标量/数组的共享小工具(各 Provider 复用)。</summary>
internal static class ApproverParamReader
{
    public static int GetInt(Dictionary<string, JsonElement>? parameters, string key, int defaultValue)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => defaultValue,
        };
    }

    public static long? GetLong(Dictionary<string, JsonElement>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var el))
            return null;
        return ReadLong(el);
    }

    public static IReadOnlyList<long> GetLongList(
        Dictionary<string, JsonElement>? parameters,
        string arrayKey,
        string? singularKey = null)
    {
        if (parameters is null)
            return [];

        if (parameters.TryGetValue(arrayKey, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<long>();
            foreach (var item in arr.EnumerateArray())
            {
                var v = ReadLong(item);
                if (v is long id)
                    list.Add(id);
            }
            return list;
        }

        if (singularKey is not null && GetLong(parameters, singularKey) is long one)
            return [one];

        return [];
    }

    private static long? ReadLong(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number when el.TryGetInt64(out var n) => n,
        JsonValueKind.String when long.TryParse(el.GetString(), out var n) => n,
        _ => null,
    };
}
