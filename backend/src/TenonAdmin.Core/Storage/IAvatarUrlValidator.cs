namespace TenonAdmin.Core;

/// <summary>
/// 头像 URL 校验(QA25.3 安全加固)——收窄"头像"字段可接受的取值,拦截存储型 XSS/开放重定向/内容注入面:
/// 头像字段本是自由文本(<see cref="ISoftDelete"/> 之外无约束),原样存入并原样吐给 <c>&lt;img src&gt;</c>,
/// 若允许任意外部地址或未签名的同源路径,等于放开一条可控 URL 注入通道。
/// <para>默认实现(<c>TenonAdmin.Services</c> 层)只放行 <c>null</c>/空白,或 <see cref="IFileUrlSigner"/> 签出的
/// 本地直链(<c>/api/v1/sys/file/{id}/view?sig=...</c>,签名核验通过);其余(外部 http(s)://、同源但无签名/伪造签名)一律拒绝。</para>
/// <para>类 public、方法 virtual,注册用 TryAdd:消费者可放宽规则(如接入外部图床白名单)整体替换。</para>
/// </summary>
public interface IAvatarUrlValidator
{
    /// <summary>头像取值是否合法;<c>null</c>/空白或有效的本地签名直链视为合法。</summary>
    bool IsValid(string? avatar);
}
