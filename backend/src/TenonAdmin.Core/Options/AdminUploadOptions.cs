namespace TenonAdmin.Core;

/// <summary>
/// 上传配置(对应 <c>TenonAdmin:Upload</c> 节,设计 §3.2/§14)。
/// </summary>
public class AdminUploadOptions
{
    /// <summary>提供者:<c>Local</c>(默认,本地磁盘)| OSS/Minio 等走 <c>IFileStorage</c> 扩展点(v1.x 可选包)</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// 本地存储根目录。相对路径<b>按 ContentRoot 解析</b>(与 SQLite 库文件、JWT 开发密钥同一基准,不随进程 CWD 漂移);
    /// 绝对路径原样使用。正式部署声明为数据卷(§11);<b>用 <c>UseStaticFiles()</c> 托管前端产物时必须挪出 <c>wwwroot</c></b>,
    /// 否则上传物会被静态中间件匿名直出,绕过鉴权下载(见 docs/deployment.md)。
    /// </summary>
    public string RootPath { get; set; } = "./wwwroot/upload";

    /// <summary>单文件大小上限(MB);超过拒收(<see cref="ErrorCode.FileTooLarge"/>)</summary>
    public int MaxSizeMb { get; set; } = 20;

    /// <summary>
    /// 允许的文件后缀白名单(含点、小写)。<b>按后缀而非 Content-Type 判定</b>(§14:不以 Content-Type 为唯一依据)。
    /// 空数组表示不限(不建议)。
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [".jpg", ".png", ".pdf", ".xlsx", ".docx", ".zip"];
}
