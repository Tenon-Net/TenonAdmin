namespace TenonAdmin.Core;

/// <summary>
/// JWT 配置(对应 <c>TenonAdmin:Jwt</c> 节,设计 §3.2/§14)。
/// </summary>
public class AdminJwtOptions
{
    /// <summary>
    /// 签名密钥(至少 32 字节的随机串)。<b>null(默认)= 开发密钥模式</b>:
    /// 自动生成并持久化到 <c>./data/dev-jwt.key</c>(重启令牌不失效),同时打印醒目警告。
    /// 生产环境必须显式配置——这是安全基线,不是建议(设计 §14)。
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>令牌签发者(iss claim)</summary>
    public string Issuer { get; set; } = "TenonAdmin";

    /// <summary>访问令牌有效期(分钟),默认 2 小时——短命令牌 + 刷新续期(设计 §15)</summary>
    public int ExpireMinutes { get; set; } = 120;

    /// <summary>刷新令牌有效期(分钟),默认 7 天</summary>
    public int RefreshExpireMinutes { get; set; } = 10080;
}
