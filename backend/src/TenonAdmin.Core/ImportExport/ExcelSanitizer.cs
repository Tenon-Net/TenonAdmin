namespace TenonAdmin.Core;

/// <summary>
/// Excel 公式注入防御:以危险前缀开头的单元格值前置单引号,阻止 Excel 将其解析为公式。
/// OWASP CSV Injection 建议的字符集:= + - @ \t \r
/// </summary>
public static class ExcelSanitizer
{
    public static string? Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var first = value[0];
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
            return "'" + value;
        return value;
    }
}
