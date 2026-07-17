using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 邮件通道(B2)——默认零配置走日志实现(不发信、开发期够用);配了 SMTP 主机则内核自动选内置 SmtpEmailSender。
/// 消费方前置注册自有 <see cref="IEmailSender"/> 覆盖此选择,见 <c>ReplaceabilityTests.ReplaceEmailSender_*</c>。
/// </summary>
public class EmailSenderTests
{
    [Fact]
    public async Task Default_no_host_uses_logging_sender_and_does_not_throw()
    {
        using var f = new AdminAppFactory();
        var sender = f.Services.GetRequiredService<IEmailSender>();
        Assert.IsType<LoggingEmailSender>(sender);
        await sender.SendAsync("a@example.com", "hi", "<b>body</b>");   // 日志通道:静默成功
    }

    [Fact]
    public void Configured_host_selects_builtin_smtp_sender()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Email:Host"] = "smtp.example.com",
                ["TenonAdmin:Email:From"] = "noreply@example.com",
            },
        };
        Assert.IsType<SmtpEmailSender>(f.Services.GetRequiredService<IEmailSender>());
    }
}
