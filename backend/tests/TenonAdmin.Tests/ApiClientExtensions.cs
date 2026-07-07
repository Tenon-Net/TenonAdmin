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
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public static async Task<string> LoginToken(this HttpClient client, string account, string password)
    {
        var j = await (await client.PostJson("/api/v1/auth/login", new { account, password })).ReadEnvelope();
        return j.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
