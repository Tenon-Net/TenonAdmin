namespace TenonAdmin.Core;

/// <summary>
/// 文件存储扩展点(设计 §5 扩展点表)。默认实现 <c>LocalFileStorage</c> 写本地磁盘;
/// 换 OSS/Minio/S3 只需实现本接口并前置注册(v1.x 可选包 <c>TenonAdmin.Storage.*</c>)。
/// <para>接口只认<b>流 + 相对路径</b>,不认 HTTP/IFormFile——保持存储层与 Web 层解耦。
/// 相对路径由上传服务生成(重写过的安全名),实现方仍须做<b>路径穿越防护</b>(§14),
/// 把相对路径解析进存储根之外一律拒绝——纵深防御,不信任调用方。</para>
/// </summary>
public interface IFileStorage
{
    /// <summary>把内容流写到相对路径(目录自动创建),返回最终存储相对路径。相对路径越出存储根须抛异常。</summary>
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>打开相对路径的读流;文件不存在返回 null。调用方负责释放流。</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>删除相对路径的文件;不存在则静默。</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
