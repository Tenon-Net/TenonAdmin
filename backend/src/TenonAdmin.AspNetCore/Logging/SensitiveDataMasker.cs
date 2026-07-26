using System.Text.Json;
using System.Text.Json.Nodes;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 入参脱敏(设计 §14)——把动作入参序列化成 JSON,并把<b>名字像密码/密钥/令牌</b>的字段值替换为 <c>***</c>,
/// 再写进操作日志。避免明文口令随日志落库。
/// <para>按<b>字段名</b>脱敏(不看值):任何名字含 password/pwd/secret/token/credential 的属性都被打码,
/// 递归处理嵌套对象与数组。序列化失败(如含 IFormFile 等不可序列化入参)不阻断请求,记占位串。</para>
/// </summary>
public static class SensitiveDataMasker
{
    /// <summary>敏感字段名关键字(大小写不敏感,子串匹配:newPassword、access_token 等都能命中)</summary>
    // header/authorization/apikey/cookie:定时任务的 HTTP 载荷把请求头整包塞在一个键里,
    // 不加这几个词,Bearer token 会原文落进 sys_op_log.ParamJson
    private static readonly string[] SENSITIVE_KEYS =
        ["password", "pwd", "secret", "token", "credential", "header", "authorization", "apikey", "api_key", "cookie"];

    private const string REDACTED = "***";
    private const string UNSERIALIZABLE = "<unserializable>";

    /// <summary>把动作入参字典(参数名 → 值)脱敏序列化为 JSON 字符串。</summary>
    public static string Mask(IDictionary<string, object?> arguments)
    {
        try
        {
            // 先整体序列化成可变 JSON 树,再原地打码——比逐类型反射简单且对匿名/嵌套类型通用
            var node = JsonSerializer.SerializeToNode(arguments);
            Redact(node);
            return node?.ToJsonString() ?? "null";
        }
        catch
        {
            // 不可序列化的入参(文件流、循环引用等)不能拖垮请求;只记占位
            return UNSERIALIZABLE;
        }
    }

    /// <summary>递归遍历 JSON 树:对象里命中敏感名的属性打码,其余下钻;数组逐元素下钻。</summary>
    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // 先快照键集合:遍历期间要改属性值,不能在原集合上边改边遍历
                foreach (var key in obj.Select(kv => kv.Key).ToArray())
                {
                    if (IsSensitive(key)) obj[key] = REDACTED;
                    else Redact(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Redact(item);
                break;
        }
    }

    private static bool IsSensitive(string name) =>
        SENSITIVE_KEYS.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
