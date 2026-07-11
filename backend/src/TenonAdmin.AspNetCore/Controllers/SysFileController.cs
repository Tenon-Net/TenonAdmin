using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 文件管理端点(设计 §4/§14)——上传 / 下载 / 分页 / 删除。全部 <c>[RolePermission]</c>。
/// 安全(后缀白名单、大小上限、文件名重写、路径穿越防护)由 <see cref="IFileService"/> / <c>LocalFileStorage</c> 保证。
/// </summary>
[ApiController]
[Route("api/v1/sys/file")]
[Module("Upload")]   // 可经 Api:DisabledModules=["Upload"] 关闭
public class SysFileController(IFileService files) : ControllerBase
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
        return Result<FileUploadOutput>.Ok(output);
    }

    /// <summary>下载文件(按 Id;以原始文件名回传)</summary>
    [HttpGet("{id}/download")]
    [RolePermission]
    public async Task<IActionResult> Download(long id)
    {
        var download = await files.DownloadAsync(id);
        // File(...) 会在写完响应后释放流;文件名做 Content-Disposition 编码由框架处理
        return File(download.Content, download.ContentType, download.OriginalName);
    }

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
}
