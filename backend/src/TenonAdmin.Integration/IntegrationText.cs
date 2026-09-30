namespace TenonAdmin.Integration;

/// <summary>模块内共用的文本小工具:按列宽截断、原因短码与错误摘要合成一行。</summary>
internal static class IntegrationText
{
    /// <summary>超过 <paramref name="max"/> 个字符即截断;null 原样返回。</summary>
    public static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>合成 <c>{reason}: {summary}</c>;任一为空时只取另一个。</summary>
    public static string? JoinReason(string? reason, string? summary) =>
        string.IsNullOrEmpty(summary) ? reason : string.IsNullOrEmpty(reason) ? summary : $"{reason}: {summary}";
}
