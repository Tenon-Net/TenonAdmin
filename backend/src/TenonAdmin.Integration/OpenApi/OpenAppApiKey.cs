namespace TenonAdmin.Integration;

/// <summary>
/// 接入凭据的线上格式 <c>tna_{keyId}.{secret}</c>(实现契约 §4.1):<c>keyId</c> 16 位小写 hex(公开、唯一索引),
/// <c>secret</c> 43 位 base64url(32 字节随机数)。格式不合法直接判无效,不查库。
/// </summary>
public static class OpenAppApiKey
{
    /// <summary>固定前缀(便于日志扫描与密钥泄露检测工具识别)。</summary>
    public const string Prefix = "tna_";

    /// <summary>公开标识长度。</summary>
    public const int KeyIdLength = 16;

    /// <summary>秘密长度(32 字节 base64url 无填充)。</summary>
    public const int SecretLength = 43;

    /// <summary>完整凭据长度。</summary>
    public const int Length = 4 + KeyIdLength + 1 + SecretLength;

    /// <summary>拼出完整凭据。</summary>
    public static string Format(string keyId, string secret) => $"{Prefix}{keyId}.{secret}";

    /// <summary>
    /// 严格解析:前缀、分隔符、长度与字符集全部吻合才成功。失败时两个输出为空串——调用方不得把原文写进日志或异常。
    /// </summary>
    public static bool TryParse(string? value, out string keyId, out string secret)
    {
        keyId = "";
        secret = "";
        if (value is null || value.Length != Length || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        if (value[Prefix.Length + KeyIdLength] != '.') return false;

        var id = value.AsSpan(Prefix.Length, KeyIdLength);
        foreach (var c in id)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;

        var s = value.AsSpan(Prefix.Length + KeyIdLength + 1);
        foreach (var c in s)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return false;

        keyId = id.ToString();
        secret = s.ToString();
        return true;
    }
}
