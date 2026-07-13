namespace TenonAdmin.Core;

/// <summary>
/// 文件直链签名(设计 §14)。给一个文件 Id 签出<b>不可伪造但可匿名访问</b>的 URL,让 <c>&lt;img src&gt;</c> 这类
/// 无法携带 Authorization 头的场景也能取到受管文件(通知公告正文里的图片是第一个用例)。
/// <para><b>这是一条能力链接(capability URL)</b>:拿得到链接就拿得到文件,拿不到就猜不出来(256 位 HMAC)。
/// 与 S3 presigned URL / 各家图床同一模型。两个天花板要清楚:</para>
/// <list type="bullet">
///   <item><b>没有过期时间</b>——链接会被存进 Markdown 正文这类<b>持久内容</b>里,一条 30 分钟后失效的 URL
///   等于"发布半小时后所有图片一起坏"。要过期语义只能在<b>渲染时现签</b>(正文只存 Id),那是另一件事。</item>
///   <item><b>撤销手段只有两个</b>:删掉文件(记录一软删,直链即 404),或轮换 <c>Jwt:SecretKey</c>(全部直链一起失效)。</item>
/// </list>
/// <para>它同时让"用 <c>UseStaticFiles()</c> 托管上传目录"这条鉴权绕过彻底失去存在理由——
/// 需要匿名可读的图片,走签名直链,而不是把整个上传目录敞开。</para>
/// </summary>
public interface IFileUrlSigner
{
    /// <summary>签出某文件的签名(base64url,无填充)。</summary>
    string Sign(long fileId);

    /// <summary>校验签名是否匹配该文件 Id(定长时间比较,不泄漏前缀信息)。空/畸形签名一律 false。</summary>
    bool Verify(long fileId, string? signature);

    /// <summary>拼出可直接塞进 <c>&lt;img src&gt;</c> 的相对 URL(含签名)。</summary>
    string BuildUrl(long fileId);
}
