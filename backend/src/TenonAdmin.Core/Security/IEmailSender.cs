namespace TenonAdmin.Core;

/// <summary>
/// 邮件发送通道(设计 §14 投递通道,与 <see cref="ISmsSender"/> 同款抽象)。内核只定义抽象、不带任何第三方邮件库
/// (运行时依赖纪律:仅 SqlSugarCore + Microsoft.*),默认实现仅把邮件写日志(开发期够用);配了 SMTP 主机则用
/// 内置 <c>SmtpEmailSender</c>(BCL <c>System.Net.Mail</c>)。
/// <para>接现代协议(OAuth2 SMTP / 云厂商 API):实现本接口并在 <c>AddTenonAdmin()</c> 之前注册即接管
/// (TryAdd 前置替换,§5.2):<c>services.AddSingleton&lt;IEmailSender, MailKitEmailSender&gt;();</c></para>
/// <para>为后续邮件验证码登录 / 通知邮件扇出铺路(通道先于扇出)。</para>
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// 发送一封邮件。
    /// </summary>
    /// <param name="to">收件人地址</param>
    /// <param name="subject">主题</param>
    /// <param name="htmlBody">正文(HTML;纯文本原样传入亦可)</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
