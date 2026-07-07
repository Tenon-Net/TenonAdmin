namespace TenonAdmin.Core;

/// <summary>
/// 令牌签发的主体信息——认证服务与令牌实现之间的中立契约,不绑定 JWT 类型(保 Core 零依赖)。
/// </summary>
/// <param name="UserId">用户主键</param>
/// <param name="Account">登录账号(写入令牌便于日志/排查,不作鉴权依据)</param>
/// <param name="SessionId">
/// 会话标识(设计 §15):写入令牌 claim,作为在线用户列举与强退的稳定锚点——
/// 强退即作废该 SessionId,令牌未到期也立刻失效。
/// </param>
/// <param name="IsSuperAdmin">
/// 超管标志:写入令牌 claim,授权管道第一步"超管直接放行"(设计 §6)据此判断,免每请求查库。
/// </param>
/// <param name="OrgId">
/// 归属机构 Id(设计 §6 数据范围锚点):写入令牌 claim,供审计 AOP 把 <c>DataEntity.CreateOrgId</c>
/// 填成创建者当时所属机构,免每次插入查库。为 null 表示用户无归属机构。
/// </param>
public record TokenSubject(long UserId, string Account, string SessionId, bool IsSuperAdmin = false, long? OrgId = null);

/// <summary>令牌自定义 claim 名常量(禁硬编码字符串纪律,设计 §6)</summary>
public static class TokenClaimNames
{
    /// <summary>会话标识(强退锚点,设计 §15)</summary>
    public const string SESSION_ID = "sid";

    /// <summary>超级管理员标志(值为 "true" 时授权直接放行)</summary>
    public const string SUPER_ADMIN = "sadm";

    /// <summary>归属机构 Id(数据范围锚点,设计 §6;审计 AOP 据此填充 CreateOrgId)</summary>
    public const string ORG_ID = "org";
}

/// <summary>
/// 一次签发的令牌对(设计 §15):AccessToken 短期、不落库;RefreshToken 长期、服务端存哈希、支持轮换吊销。
/// </summary>
/// <param name="AccessToken">访问令牌(默认 JWT)</param>
/// <param name="ExpiresAt">访问令牌过期时刻(UTC)</param>
/// <param name="RefreshToken">刷新令牌明文(仅此一次下发;服务端只存其哈希)</param>
/// <param name="RefreshExpiresAt">刷新令牌过期时刻(UTC)</param>
public record TokenPair(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>
/// 令牌提供者(扩展点,设计 §5.5)。
/// <para>默认实现为 JWT(AspNetCore 层,基于 <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> 体系)。
/// 典型替换场景:自定义令牌格式、对接 API 网关统一签发。</para>
/// </summary>
public interface ITokenProvider
{
    /// <summary>为指定主体签发一对新令牌(过期时长由 Jwt 配置节决定)</summary>
    TokenPair Create(TokenSubject subject);
}
