namespace TenonAdmin.Core;

/// <summary>
/// 上传配置(对应 <c>TenonAdmin:Upload</c> 节,设计 §3.2/§14)。
/// </summary>
public class AdminUploadOptions
{
    /// <summary>提供者:<c>Local</c>(默认,本地磁盘)| OSS/Minio 等走 <c>IFileStorage</c> 扩展点(v1.x 可选包)</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>本地存储根目录(相对进程工作目录;正式部署声明为数据卷,§11)</summary>
    public string RootPath { get; set; } = "./wwwroot/upload";

    /// <summary>单文件大小上限(MB);超过拒收(<see cref="ErrorCode.FileTooLarge"/>)</summary>
    public int MaxSizeMb { get; set; } = 20;

    /// <summary>
    /// 允许的文件后缀白名单(含点、小写)。<b>按后缀而非 Content-Type 判定</b>(§14:不以 Content-Type 为唯一依据)。
    /// 空数组表示不限(不建议)。
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [".jpg", ".png", ".pdf", ".xlsx", ".docx", ".zip"];
}
