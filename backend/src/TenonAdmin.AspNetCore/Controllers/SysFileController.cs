using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 文件管理端点(设计 §4/§14)——上传 / 下载 / 直链 / 分页 / 删除。
/// 除签名直链(<see cref="View"/>,显式匿名)外全部 <c>[RolePermission]</c>。
/// 安全(后缀白名单、大小上限、文件名重写、路径穿越防护)由 <see cref="IFileService"/> / <c>LocalFileStorage</c> 保证。
/// </summary>
[ApiController]
[Route("api/v1/sys/file")]
[Module("Upload")]   // 可经 Api:DisabledModules=["Upload"] 关闭
public class SysFileController(IFileService files, IFileUrlSigner signer) : ControllerBase
{
    /// <summary>上传单个文件,返回文件 Id 与展示信息</summary>
    [HttpPost("upload")]
    [RolePermission]
    [OperationLog("上传文件")]   // 入参是 IFormFile,脱敏器序列化不了会记占位串,不影响记录本次操作
    public async Task<Result<FileUploadOutput>> Upload(IFormFile file)
    {
        // 控制器只负责把 IFormFile 拆成"流 + 元数据"喂给服务;IFormFile 不入 Services 层
        await using var stream = file.OpenReadStream();
        var output = await files.UploadAsync(new FileUploadInput
        {
            Content = stream,
            FileName = file.FileName,
            Size = file.Length,
            ContentType = file.ContentType,
        });
        return Result<FileUploadOutput>.Ok(WithViewUrl(output));
    }

    /// <summary>下载文件(按 Id;以原始文件名回传,浏览器另存)</summary>
    [HttpGet("{id}/download")]
    [RolePermission]
    public async Task<IActionResult> Download(long id)
    {
        var download = await files.DownloadAsync(id);
        // File(...) 会在写完响应后释放流;文件名做 Content-Disposition 编码由框架处理
        return File(download.Content, download.ContentType, download.OriginalName);
    }

    /// <summary>
    /// 签名直链(匿名,inline 展示)——给 <c>&lt;img src&gt;</c> 这类带不了 Authorization 头的场景用。
    /// <para>匿名是<b>刻意的</b>,但不可伪造:签名是文件 Id 的 HMAC(见 <see cref="IFileUrlSigner"/>)。
    /// 拿得到链接才拿得到文件,签名错一个字符就是 403。它的存在正是为了让人<b>不必</b>去
    /// <c>UseStaticFiles()</c> 敞开整个上传目录——那才是真正的鉴权绕过。</para>
    /// </summary>
    [HttpGet("{id}/view")]
    [AllowAnonymous]
    public async Task<IActionResult> View(long id, [FromQuery] string? sig)
    {
        // 校验先于查库:签名不对就不该让人拿它探测"这个 Id 存不存在"
        if (!signer.Verify(id, sig)) return StatusCode(StatusCodes.Status403Forbidden);

        var download = await files.DownloadAsync(id);   // 文件已删/物理丢失 → FileNotFound(44004)
        // 不给 fileDownloadName:留空即 Content-Disposition: inline,浏览器直接渲染而不是弹另存
        return File(download.Content, download.ContentType);
    }

    /// <summary>给上传出参补上签名直链(URL 是 HTTP 层的事,Services 层不掺和路由)。</summary>
    private FileUploadOutput WithViewUrl(FileUploadOutput output) =>
        output with { ViewUrl = signer.BuildUrl(output.Id) };

    /// <summary>分页查询文件记录(按上传时间倒序)</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<SysFile>>> Page([FromQuery] FilePageInput input) =>
        Result<PagedList<SysFile>>.Ok(await files.PageAsync(input));

    /// <summary>软删除文件记录</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除文件")]
    public async Task<Result<bool>> Delete(long id)
    {
        await files.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>批量软删除文件记录</summary>
    [HttpPost("batch-delete")]
    [RolePermission]
    [OperationLog("批量删除文件")]
    public async Task<Result<bool>> BatchDelete(BatchDeleteInput input)
    {
        await files.DeleteBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }

    // ── 分片 / 断点续传上传(设计 §4 分片上传) ──────────────────────────

    /// <summary>分片上传-初始化:秒传探测(同哈希已存在直接复用)+ 返回已收分片(断点续传)</summary>
    [HttpPost("chunk/init")]
    [RolePermission]
    public async Task<Result<ChunkInitOutput>> ChunkInit(ChunkInitInput input)
    {
        var output = await files.ChunkInitAsync(input);
        // 秒传命中时也要带上直链——调用方拿到的是"一个能用的文件",不该因为走了哪条路径而少个字段
        return Result<ChunkInitOutput>.Ok(
            output.File is null ? output : output with { File = WithViewUrl(output.File) });
    }

    /// <summary>分片上传-上传一个分片(multipart:uploadId + index + chunk 文件部件)</summary>
    [HttpPost("chunk")]
    [RolePermission]
    public async Task<Result<bool>> Chunk([FromForm] string uploadId, [FromForm] int index, IFormFile chunk)
    {
        // 同 Upload:控制器把 IFormFile 拆成流喂服务,IFormFile 不入 Services 层。
        await using var stream = chunk.OpenReadStream();
        await files.SaveChunkAsync(new ChunkSaveInput { UploadId = uploadId, Index = index, Content = stream });
        return Result<bool>.Ok(true);
    }

    /// <summary>分片上传-完成:合并 + 哈希校验 + 三道关 + 落库,返回文件记录</summary>
    [HttpPost("chunk/complete")]
    [RolePermission]
    [OperationLog("分片上传")]
    public async Task<Result<FileUploadOutput>> ChunkComplete(ChunkCompleteInput input) =>
        Result<FileUploadOutput>.Ok(WithViewUrl(await files.ChunkCompleteAsync(input)));
}
