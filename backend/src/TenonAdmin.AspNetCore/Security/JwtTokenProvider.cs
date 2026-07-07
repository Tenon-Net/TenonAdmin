using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// <see cref="ITokenProvider"/> 默认实现:HS256 JWT(设计 §5.5/§15)。
/// <para>AccessToken 携带 sub(用户 Id)、unique_name(账号)、sid(会话)、sadm(超管)四个 claim,
/// 短期有效、不落库;RefreshToken 是 64 字节加密随机串(不是 JWT——它只在换发接口出示一次,
/// 无需自包含结构;服务端将只存其哈希,§15 会话模块接入时落库)。</para>
/// <para>类 public、方法 virtual:要改 claim 集合/换签名算法,继承覆写 <see cref="Create"/> 即可(§5.3)。</para>
/// </summary>
public class JwtTokenProvider(AdminJwtOptions options, SymmetricSecurityKey signingKey, TimeProvider time) : ITokenProvider
{
    // JsonWebTokenHandler 是新一代无状态处理器(替代老 JwtSecurityTokenHandler),线程安全可复用
    private static readonly JsonWebTokenHandler HANDLER = new();

    /// <inheritdoc />
    public virtual TokenPair Create(TokenSubject subject)
    {
        var now = time.GetUtcNow();
        var expiresAt = now.AddMinutes(options.ExpireMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(BuildClaims(subject)),
        };

        return new TokenPair(
            AccessToken: HANDLER.CreateToken(descriptor),
            ExpiresAt: expiresAt,
            RefreshToken: CreateRefreshToken(),
            RefreshExpiresAt: now.AddMinutes(options.RefreshExpireMinutes));
    }

    /// <summary>访问令牌的 claim 集合(要附加自定义 claim,覆写这步)</summary>
    protected virtual IEnumerable<Claim> BuildClaims(TokenSubject subject)
    {
        yield return new Claim(JwtRegisteredClaimNames.Sub, subject.UserId.ToString());
        yield return new Claim(JwtRegisteredClaimNames.UniqueName, subject.Account);
        yield return new Claim(TokenClaimNames.SESSION_ID, subject.SessionId);
        if (subject.IsSuperAdmin)
            yield return new Claim(TokenClaimNames.SUPER_ADMIN, "true");
        if (subject.OrgId is { } orgId)
            yield return new Claim(TokenClaimNames.ORG_ID, orgId.ToString());
    }

    /// <summary>刷新令牌:64 字节加密随机 → Base64Url(512 位熵,不可猜测)</summary>
    protected virtual string CreateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
}
