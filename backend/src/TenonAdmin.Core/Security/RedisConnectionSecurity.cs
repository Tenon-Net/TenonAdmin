namespace TenonAdmin.Core;

/// <summary>
/// Redis 连接串安全属性解析(StackExchange.Redis 风格键值串)。
/// 纯函数、无 IO——供 Level3 预检 / 启动闸门判定认证与 TLS,不建立真实连接。
/// </summary>
public static class RedisConnectionSecurity
{
    /// <summary>
    /// 解析连接串:是否配置了非空密码(认证)、是否声明 TLS。
    /// 接受 <c>password=</c>/<c>pwd=</c> 与 <c>ssl=true</c>/<c>sslHost=</c> 等常见键;
    /// <paramref name="requireTlsOption"/> 为 true 时即使串中无 ssl 也视为声明 TLS(部署显式开关)。
    /// </summary>
    public static (bool HasAuth, bool HasTls) Inspect(string? connectionString, bool requireTlsOption = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, requireTlsOption);

        var map = Parse(connectionString);
        var hasAuth = HasNonEmpty(map, "password") || HasNonEmpty(map, "pwd");
        var hasTls = requireTlsOption
            || IsTruthy(map, "ssl")
            || HasNonEmpty(map, "sslhost")
            || IsTruthy(map, "sslhost");

        return (hasAuth, hasTls);
    }

    /// <summary>
    /// 将连接串脱敏为预检报告可用的摘要(主机/端口/标志位,不含密码与用户名)。
    /// </summary>
    public static string Summarize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "(empty)";

        var map = Parse(connectionString);
        var host = map.GetValueOrDefault("") // 首段无键时常为 host:port
                   ?? map.GetValueOrDefault("host")
                   ?? "?";
        // 去掉 user:pass@ 前缀(若有)
        var at = host.IndexOf('@');
        if (at >= 0 && at < host.Length - 1) host = host[(at + 1)..];

        var (hasAuth, hasTls) = Inspect(connectionString, requireTlsOption: false);
        return $"host={host};auth={(hasAuth ? "yes" : "no")};tls={(hasTls ? "yes" : "no")}";
    }

    private static Dictionary<string, string> Parse(string connectionString)
    {
        // SE.Redis: "host:port,password=x,ssl=true,abortConnect=false"
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = connectionString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                // 无键段:通常是 endpoints 主机列表;仅保留第一条作摘要
                if (!dict.ContainsKey(""))
                    dict[""] = part;
                continue;
            }

            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }

    private static bool HasNonEmpty(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v);

    private static bool IsTruthy(Dictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return false;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
