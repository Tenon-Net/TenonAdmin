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
