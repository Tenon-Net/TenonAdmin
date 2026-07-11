using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 上传入参——只带<b>流 + 元数据</b>,不带 IFormFile(保持 Services 层与 Web 层解耦)。
/// 控制器从 IFormFile 组装它:<c>OpenReadStream()</c> / <c>FileName</c> / <c>Length</c> / <c>ContentType</c>。
/// </summary>
public record FileUploadInput
{
    /// <summary>文件内容读流(调用方打开,服务读完即用完)</summary>
    public required Stream Content { get; init; }

    /// <summary>原始文件名(含后缀;仅用于取后缀 + 展示,绝不拼进物理路径)</summary>
    public required string FileName { get; init; }

    /// <summary>文件大小(字节)</summary>
    public required long Size { get; init; }

    /// <summary>声明的 Content-Type(仅记录,不作安全依据)</summary>
    public string? ContentType { get; init; }
}

/// <summary>上传出参:新文件 Id + 展示信息。</summary>
public record FileUploadOutput
{
    public required long Id { get; init; }
    public required string OriginalName { get; init; }
    public required string StoragePath { get; init; }
    public required long SizeBytes { get; init; }
}

/// <summary>下载载荷:读流 + 原始名 + Content-Type(控制器据此回传 File 结果)。</summary>
public record FileDownload
{
    public required Stream Content { get; init; }
    public required string OriginalName { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}

/// <summary>文件分页查询入参:按原始文件名模糊过滤。</summary>
public record FilePageInput : PageInputBase
{
    /// <summary>原始文件名(模糊匹配,可选)</summary>
    public string? FileName { get; init; }
}

// ── 分片 / 断点续传上传(设计 §4 分片上传) ──────────────────────────

/// <summary>分片上传初始化入参:客户端切片前先算整文件 SHA-256,携文件元信息问询秒传/断点。</summary>
public record ChunkInitInput
{
    /// <summary>整文件内容 SHA-256(hex,小写);兼作 uploadId 与秒传去重键</summary>
    public required string FileHash { get; init; }

    /// <summary>原始文件名(含后缀)</summary>
    public required string FileName { get; init; }

    /// <summary>整文件大小(字节;仅参考,最终以合并结果为准)</summary>
    public long TotalSize { get; init; }

    /// <summary>分片总数</summary>
    public int ChunkCount { get; init; }

    /// <summary>声明的 Content-Type(仅记录)</summary>
    public string? ContentType { get; init; }
}

/// <summary>分片上传初始化出参:秒传命中直接给文件,否则给 uploadId + 已收分片(断点续传)。</summary>
public record ChunkInitOutput
{
    /// <summary>是否秒传命中(同 hash 已存在,无需再传)</summary>
    public bool Uploaded { get; init; }

    /// <summary>秒传命中时的已存在文件(<see cref="Uploaded"/> 为 true 时非空)</summary>
    public FileUploadOutput? File { get; init; }

    /// <summary>上传会话 Id(= FileHash);后续传分片/完成时回传</summary>
    public string? UploadId { get; init; }

    /// <summary>服务端已收到的分片下标(断点续传:客户端据此跳过已传)</summary>
    public IReadOnlyCollection<int> ReceivedIndexes { get; init; } = [];
}

/// <summary>单个分片入参——流 + uploadId + 下标(控制器从 multipart 组装)。</summary>
public record ChunkSaveInput
{
    /// <summary>分片内容读流</summary>
    public required Stream Content { get; init; }

    /// <summary>上传会话 Id</summary>
    public required string UploadId { get; init; }

    /// <summary>分片下标(从 0 起)</summary>
    public int Index { get; init; }
}

/// <summary>分片上传完成入参:触发合并 + 完整性校验 + 三道关 + 落库。</summary>
public record ChunkCompleteInput
{
    /// <summary>上传会话 Id(= FileHash)</summary>
    public required string UploadId { get; init; }

    /// <summary>整文件 SHA-256(hex,小写);服务端合并后重算并比对</summary>
    public required string FileHash { get; init; }

    /// <summary>原始文件名(含后缀)</summary>
    public required string FileName { get; init; }

    /// <summary>分片总数(合并时校验 0..ChunkCount-1 齐全)</summary>
    public int ChunkCount { get; init; }

    /// <summary>声明的 Content-Type(仅记录)</summary>
    public string? ContentType { get; init; }
}
