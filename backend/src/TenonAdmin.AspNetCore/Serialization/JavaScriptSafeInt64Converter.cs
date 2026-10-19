using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenonAdmin.AspNetCore;

/// <summary>安全范围内保留数字契约；超出 JavaScript 安全整数范围的 long 使用十进制字符串。</summary>
internal sealed class JavaScriptSafeInt64Converter : JsonConverter<long>
{
    private const long MaxSafeInteger = 9007199254740991L;

    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetInt64() : ReadDecimalString(ref reader);

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        if (value is >= -MaxSafeInteger and <= MaxSafeInteger)
            writer.WriteNumberValue(value);
        else
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    // 字典的 long 键本来就是 JSON 属性名，仍按原始十进制值读写。
    public override long ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadDecimalString(ref reader);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));

    private static long ReadDecimalString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName &&
            long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new JsonException("值必须是 int64 范围内的整数或十进制整数字符串。");
    }
}
