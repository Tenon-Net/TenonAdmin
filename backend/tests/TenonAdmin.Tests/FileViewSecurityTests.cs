using System.Net.Http.Headers;
using System.Text;

namespace TenonAdmin.Tests;

/// <summary>
/// 签名直链的内容安全:匿名 inline 端点绝不能按客户端自报的 Content-Type 回吐(否则是存储型 XSS)。
/// <para>攻击链:低权用户上传 <c>x.png</c>(过后缀白名单),其内容是 <c>&lt;script&gt;</c>、自报
/// <c>Content-Type: text/html</c>;浏览器认 Content-Type 不认后缀,匿名 view 即以 HTML 渲染,
/// 与前端同源 → 偷 localStorage 里的令牌。修复:view 按<b>后缀</b>解析安全媒体类型 + nosniff。</para>
/// </summary>
public class FileViewSecurityTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>上传一个文件,可指定客户端自报的 Content-Type(攻击者可控)。返回 (id, viewUrl)。</summary>
    private static async Task<(long Id, string ViewUrl)> UploadAsync(HttpClient c, string fileName, string declaredContentType, string body)
    {
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        part.Headers.ContentType = new MediaTypeHeaderValue(declaredContentType);
        using var form = new MultipartFormDataContent { { part, "file", fileName } };
        var data = (await (await c.PostAsync("/api/v1/sys/file/upload", form)).ReadEnvelope()).GetProperty("data");
        return (data.GetProperty("id").GetInt64(), data.GetProperty("viewUrl").GetString()!);
    }

    [Fact]
    public async Task View_never_echoes_client_declared_html_content_type()
    {
        using var f = new AdminAppFactory();
        // 上传名为 .png(过白名单)但自报 text/html、内容是脚本的文件
        var (_, viewUrl) = await UploadAsync(await SuperAdminClient(f), "evil.png", "text/html", "<script>alert(document.cookie)</script>");

        var anon = f.CreateClient();
        var resp = await anon.GetAsync(viewUrl);

        Assert.True(resp.IsSuccessStatusCode);
        // 关键:服务端按后缀 .png 权威判为 image/png,绝不回吐攻击者自报的 text/html
        Assert.Equal("image/png", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", resp.Content.Headers.ContentType?.MediaType);
        // 纵深防御:禁 MIME 嗅探
        Assert.Contains("nosniff", resp.Headers.TryGetValues("X-Content-Type-Options", out var v) ? string.Join(",", v) : "");
    }

    [Fact]
    public async Task View_forces_download_for_non_inline_safe_types()
    {
        using var f = new AdminAppFactory();
        // .zip 在默认白名单内,但不是"可安全内联"的图片/PDF → 必须强制另存,不能 inline 渲染
        var (_, viewUrl) = await UploadAsync(await SuperAdminClient(f), "payload.zip", "text/html", "<script>alert(1)</script>");

        var resp = await (f.CreateClient()).GetAsync(viewUrl);

        Assert.True(resp.IsSuccessStatusCode);
        Assert.Equal("application/octet-stream", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", resp.Content.Headers.ContentDisposition?.DispositionType);
    }
}
