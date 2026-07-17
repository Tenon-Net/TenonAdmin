namespace TenonAdmin.Core;

/// <summary>
/// 短信验证码发送通道(设计 §14 登录加固)。内核只定义抽象、不带任何云厂商 SDK
/// (运行时依赖纪律:仅 SqlSugarCore + Microsoft.*),默认实现仅把验证码写日志(开发期够用)。
/// <para>接真实短信:实现本接口(按 purpose 映射厂商模板),
/// 在 <c>AddTenonAdmin()</c> 之前注册即接管(TryAdd 前置替换,§5.2):
/// <c>services.AddSingleton&lt;ISmsSender, AliyunSmsSender&gt;();</c></para>
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// 发送一条验证码短信。
    /// </summary>
    /// <param name="phone">目标手机号(原样传入,不做格式假设)</param>
    /// <param name="code">验证码明文(定长数字,长度见 <c>AdminSmsOtpOptions.CodeLength</c>)</param>
    /// <param name="purpose">用途:<c>mfa</c>(登录二次验证)| <c>login</c>(验证码免密登录);实现据此选厂商模板</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendCodeAsync(string phone, string code, string purpose, CancellationToken cancellationToken = default);
}
