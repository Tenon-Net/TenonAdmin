using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IEmailSender"/> 的内置 SMTP 实现(BCL <c>System.Net.Mail</c>,零第三方依赖)。配了
/// <c>TenonAdmin:Email:Host</c> 时由 DI 选中(见 <c>ServicesSetup</c>)。步骤 <c>virtual</c> 可覆写。
/// <para>ponytail: 天花板是 BCL <see cref="SmtpClient"/>——基础 SMTP(STARTTLS/凭据)够用,但不支持
/// OAuth2 SMTP、现代认证与富协议。需要这些的消费方前置注册基于 MailKit 的 <see cref="IEmailSender"/> 即接管
/// (这正是把它做成可替换接口的原因,内核不引第三方邮件库)。</para>
/// </summary>
public class SmtpEmailSender(AdminEmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public virtual async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.User))
            client.Credentials = new NetworkCredential(options.User, options.Password);

        using var message = new MailMessage(options.From, to, subject, htmlBody) { IsBodyHtml = true };
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            // 发信失败要让调用方知情(不同于日志通道的静默)——但先留一条诊断,便于排 SMTP 配置错。
            logger.LogError(ex, "SMTP 邮件发送失败:{To} | {Subject}", to, subject);
            throw;
        }
    }
}
