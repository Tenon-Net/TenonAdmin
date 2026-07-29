using System.Text;
using System.Text.Json;

namespace TenonAdmin.Tests;

/// <summary>集成测试的 HTTP 小助手:发 JSON、读统一信封、登录取 token。</summary>
internal static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object body) =>
        client.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object body) =>
        client.PutAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    /// <summary>读响应体为 JSON 根元素(统一信封 { code, msgKey, args, message, data })。</summary>
    public static async Task<JsonElement> ReadEnvelope(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            // 把状态码与正文前缀带上:CI 上曾出现过 PUT 返回 "Not Found"/Npgsql 纯文本导致只看到
            // "'N' is an invalid start of a value",看不出到底是 404 还是未处理异常。
            var preview = body.Length <= 500 ? body : body[..500] + "…";
            throw new InvalidOperationException(
                $"Expected JSON envelope but got {(int)response.StatusCode} {response.ReasonPhrase}: {preview}",
                ex);
        }
    }

    public static async Task<string> LoginToken(this HttpClient client, string account, string password)
    {
        var j = await (await client.PostJson("/api/v1/auth/login", new { account, password })).ReadEnvelope();
        return j.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
