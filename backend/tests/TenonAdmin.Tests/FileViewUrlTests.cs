using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 签名直链(<c>GET /sys/file/{id}/view?sig=</c>)。
/// <para><c>&lt;img src&gt;</c> 带不了 Authorization 头,所以受管文件里的图片要么走匿名直链,要么就是坏链——
/// 而"开 UseStaticFiles 托管上传目录"这条路是鉴权绕过(整个上传目录任人取)。签名直链是第三条路:
/// 匿名可取,但签名是文件 Id 的 HMAC,伪造不了。</para>
/// </summary>
public class FileViewUrlTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>上传一个 png,返回 (id, viewUrl, 原始字节)。</summary>
    private static async Task<(long Id, string ViewUrl, byte[] Bytes)> UploadAsync(HttpClient c)
    {
        var bytes = Encoding.UTF8.GetBytes("fake-png-bytes-" + Guid.NewGuid());
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "shot.png" } };
        var data = (await (await c.PostAsync("/api/v1/sys/file/upload", form)).ReadEnvelope()).GetProperty("data");
        return (data.GetProperty("id").GetInt64(), data.GetProperty("viewUrl").GetString()!, bytes);
    }

    [Fact]
    public async Task ViewUrl_serves_the_file_anonymously_and_inline()
    {
        using var f = new AdminAppFactory();
        var (_, viewUrl, bytes) = await UploadAsync(await SuperAdminClient(f));

        // 关键:全新的、没有任何 Authorization 头的客户端 —— 浏览器加载 <img> 时就是这个样子
        var anonymous = f.CreateClient();
        var response = await anonymous.GetAsync(viewUrl);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());   // 字节无损
        // inline 而非 attachment:浏览器要就地渲染这张图,不是弹"另存为"(那是 /download 的活)
        Assert.NotEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Tampered_signature_is_rejected()
    {
        using var f = new AdminAppFactory();
        var (id, viewUrl, _) = await UploadAsync(await SuperAdminClient(f));
        var anonymous = f.CreateClient();

        var sig = viewUrl.Split("sig=")[1];
        var tampered = sig[0] == 'A' ? 'B' + sig[1..] : 'A' + sig[1..];

        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/v1/sys/file/{id}/view?sig={tampered}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/v1/sys/file/{id}/view?sig=")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/v1/sys/file/{id}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/v1/sys/file/{id}/view?sig=not-base64url!!")).StatusCode);
    }

    [Fact]
    public async Task Signature_is_bound_to_its_own_file_id()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);
        var (idA, urlA, _) = await UploadAsync(c);
        var (idB, _, _) = await UploadAsync(c);

        // 拿甲的合法签名去取乙 —— 签名绑的是 Id,换个 Id 就不成立
        var sigA = urlA.Split("sig=")[1];
        var anonymous = f.CreateClient();

        Assert.NotEqual(idA, idB);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/v1/sys/file/{idB}/view?sig={sigA}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_file_kills_its_link()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);
        var (id, viewUrl, _) = await UploadAsync(c);

        await c.DeleteAsync($"/api/v1/sys/file/{id}");   // 软删:直链的两个撤销手段之一(另一个是轮换 JWT 密钥)

        var envelope = await (await f.CreateClient().GetAsync(viewUrl)).ReadEnvelope();
        Assert.Equal((int)ErrorCode.FileNotFound, envelope.GetProperty("code").GetInt32());
    }
}
