namespace TenonAdmin.Core;

/// <summary>
/// 原生 TOTP(RFC 6238 / RFC 4226)——HMAC-SHA1、30 秒步长、6 位、Base32 种子。
/// 兼容 Google Authenticator / Microsoft Authenticator 等主流应用。零第三方依赖(纯 BCL)。
/// </summary>
public interface ITotpService
{
    /// <summary>生成 20 字节随机种子并以 Base32(无填充)编码返回。</summary>
    string GenerateSeed();

    /// <summary>
    /// 组装 otpauth URI(<c>otpauth://totp/{issuer}:{account}?secret=...&amp;issuer=...&amp;algorithm=SHA1&amp;digits=6&amp;period=30</c>),
    /// 供前端生成二维码。
    /// </summary>
    string GetUri(string account, string issuer, string seed);

    /// <summary>
    /// 校验动态口令。<paramref name="window"/> 为允许的时间步偏移(默认 ±1,即前后各 30 秒)。
    /// 种子为 Base32;码为 6 位数字串。
    /// </summary>
    bool Verify(string seed, string code, int window = 1);

    /// <summary>计算指定时刻(或当前 UTC)的 6 位码——供单测与诊断;生产校验请用 <see cref="Verify"/>。</summary>
    string ComputeCode(string seed, DateTimeOffset? utcNow = null);
}
