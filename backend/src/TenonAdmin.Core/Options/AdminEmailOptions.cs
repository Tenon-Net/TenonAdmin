namespace TenonAdmin.Core;

/// <summary>
/// 邮件通道配置(对应 <c>TenonAdmin:Email</c> 节,设计 §14 投递通道)。
/// <para><see cref="Host"/> 为空(默认)时用日志实现(<c>LoggingEmailSender</c>,开发期把邮件写日志);
/// 配了 <see cref="Host"/> 则内核用 <c>SmtpEmailSender</c>(BCL SMTP)。消费方需现代协议(OAuth2 SMTP、
/// 云厂商 API)时前置注册自有 <see cref="IEmailSender"/> 即接管,本配置随之不生效。</para>
/// </summary>
public class AdminEmailOptions
{
    /// <summary>SMTP 主机名;<b>为空则用日志实现</b>(不真正发信,开发期够用)</summary>
    public string? Host { get; set; }

    /// <summary>SMTP 端口(默认 587,STARTTLS 提交端口)</summary>
    public int Port { get; set; } = 587;

    /// <summary>是否启用 SSL/TLS(默认启用)</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>发件人地址(SMTP 的 MAIL FROM;多数服务商要求与认证账号一致)</summary>
    public string From { get; set; } = "";

    /// <summary>SMTP 认证用户名;为空则不带凭据(如内网匿名中继)</summary>
    public string? User { get; set; }

    /// <summary>SMTP 认证密码 / 授权码</summary>
    public string? Password { get; set; }
}
