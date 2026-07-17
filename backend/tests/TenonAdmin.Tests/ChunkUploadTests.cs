using System.Net.Http.Headers;
using System.Security.Cryptography;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 分片 / 断点续传上传 HTTP 级回归:init(秒传探测/已收分片)→ 传片 → 断点续传 → complete(合并+哈希校验+三道关)
/// → 下载往返无损 → 秒传命中;外加缺片报错。
/// </summary>
public class ChunkUploadTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<HttpResponseMessage> PostChunk(HttpClient c, string uploadId, int index, byte[] data)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(uploadId), "uploadId" },
            { new StringContent(index.ToString()), "index" },
            { new ByteArrayContent(data), "chunk", $"chunk{index}" },
        };
        return await c.PostAsync("/api/v1/sys/file/chunk", form);
    }

    private static byte[] Bytes(int start, int count) =>
        Enumerable.Range(start, count).Select(i => (byte)(i % 256)).ToArray();

    [Fact]
    public async Task Chunked_upload_resume_roundtrip_and_instant()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 造 3 片(内容各异),算整文件 SHA-256(客户端职责)
        byte[] c0 = Bytes(0, 1000), c1 = Bytes(1000, 1000), c2 = Bytes(2000, 500);
        var full = c0.Concat(c1).Concat(c2).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(full)).ToLowerInvariant();
        object initBody = new { fileHash = hash, fileName = "big.png", totalSize = full.Length, chunkCount = 3 };

        // init:全新,未传过任何片
        var init1 = (await (await c.PostJson("/api/v1/sys/file/chunk/init", initBody)).ReadEnvelope()).GetProperty("data");
        Assert.False(init1.GetProperty("uploaded").GetBoolean());
        var uploadId = init1.GetProperty("uploadId").GetString()!;
        Assert.Empty(init1.GetProperty("receivedIndexes").EnumerateArray());

        // 传前两片
        Assert.Equal(0, (await (await PostChunk(c, uploadId, 0, c0)).ReadEnvelope()).GetProperty("code").GetInt32());
        Assert.Equal(0, (await (await PostChunk(c, uploadId, 1, c1)).ReadEnvelope()).GetProperty("code").GetInt32());

        // 断点续传:再 init 应报告已收 [0,1]
        var init2 = (await (await c.PostJson("/api/v1/sys/file/chunk/init", initBody)).ReadEnvelope()).GetProperty("data");
        var received = init2.GetProperty("receivedIndexes").EnumerateArray().Select(e => e.GetInt32()).OrderBy(x => x).ToArray();
        Assert.Equal([0, 1], received);

        // 传第三片 + 完成
        Assert.Equal(0, (await (await PostChunk(c, uploadId, 2, c2)).ReadEnvelope()).GetProperty("code").GetInt32());
        var complete = await (await c.PostJson("/api/v1/sys/file/chunk/complete",
            new { uploadId, fileHash = hash, fileName = "big.png", chunkCount = 3 })).ReadEnvelope();
        Assert.Equal(0, complete.GetProperty("code").GetInt32());
        var fileId = complete.GetProperty("data").GetProperty("id").GetInt64();
        Assert.Equal(full.Length, complete.GetProperty("data").GetProperty("sizeBytes").GetInt64());

        // 下载往返无损
        var bytes = await c.GetByteArrayAsync($"/api/v1/sys/file/{fileId}/download");
        Assert.Equal(full, bytes);

        // 秒传(T-D7):同 hash 再 init → uploaded=true,但返回一条<b>独立</b>记录(不同 Id、共享物理文件),
        // 各引用方独立删除互不牵连。下载新 Id 得到同样的字节(证明确实指向同一内容)。
        var init3 = (await (await c.PostJson("/api/v1/sys/file/chunk/init", initBody)).ReadEnvelope()).GetProperty("data");
        Assert.True(init3.GetProperty("uploaded").GetBoolean());
        var refId = init3.GetProperty("file").GetProperty("id").GetInt64();
        Assert.NotEqual(fileId, refId);
        Assert.Equal(full, await c.GetByteArrayAsync($"/api/v1/sys/file/{refId}/download"));
    }

    [Fact]
    public async Task Complete_with_missing_chunk_fails()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var data = Bytes(0, 500);
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var init = (await (await c.PostJson("/api/v1/sys/file/chunk/init",
            new { fileHash = hash, fileName = "x.png", totalSize = data.Length, chunkCount = 2 })).ReadEnvelope()).GetProperty("data");
        var uploadId = init.GetProperty("uploadId").GetString()!;

        await PostChunk(c, uploadId, 0, data);   // 只传 1 片,缺第 2 片

        var complete = await (await c.PostJson("/api/v1/sys/file/chunk/complete",
            new { uploadId, fileHash = hash, fileName = "x.png", chunkCount = 2 })).ReadEnvelope();
        Assert.Equal((int)ErrorCode.ChunkMissing, complete.GetProperty("code").GetInt32());
    }
}
