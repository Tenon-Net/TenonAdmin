using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 文件服务(设计 §4/§14)——上传(校验 + 重写名 + 落存储 + 记账)、下载、分页列表、删除。
/// 安全不变量:后缀白名单、大小上限、文件名重写、路径穿越防护(见 <c>FileService</c> / <c>LocalFileStorage</c>)。
/// </summary>
public interface IFileService
{
    /// <summary>
    /// 上传:校验后缀白名单/大小 → 重写成安全存储名 → 交 <c>IFileStorage</c> 落盘 → 写 <c>sys_file</c> 记录。
    /// 失败抛 <see cref="ErrorCode"/> 44xxx 段(空文件/超大/后缀不允许)。
    /// </summary>
    Task<FileUploadOutput> UploadAsync(FileUploadInput input);

    /// <summary>按 Id 打开下载载荷;记录或物理文件不存在抛 <see cref="ErrorCode.FileNotFound"/>。</summary>
    Task<FileDownload> DownloadAsync(long id);

    /// <summary>分页查询文件记录(按上传时间倒序)。</summary>
    Task<PagedList<SysFile>> PageAsync(FilePageInput input);

    /// <summary>软删除文件记录(物理文件保留,回收留给清理任务);不存在抛 <see cref="ErrorCode.FileNotFound"/>。</summary>
    Task DeleteAsync(long id);

    /// <summary>批量软删除文件记录(物理文件同样保留);不存在的 Id 静默跳过。</summary>
    Task DeleteBatchAsync(IReadOnlyCollection<long> ids);
}
