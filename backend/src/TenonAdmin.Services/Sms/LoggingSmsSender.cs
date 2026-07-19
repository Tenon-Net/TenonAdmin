using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISmsSender"/> 默认实现:只把验证码写进日志(Information)。
/// <para>这是<b>开发/演示通道</b>——本地跑通全流程不需要任何短信账号,码在后端控制台可见。
/// 生产接真实短信:实现 <see cref="ISmsSender"/> 并在 <c>AddTenonAdmin()</c> 前注册即接管(§5.2)。</para>
/// </summary>
public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    /// <inheritdoc />
    public Task SendCodeAsync(string phone, string code, string purpose, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[SMS:{Purpose}] 验证码 {Code} → {Phone}(日志通道,生产请注册真实 ISmsSender)",
            purpose, code, phone);
        return Task.CompletedTask;
    }
}
