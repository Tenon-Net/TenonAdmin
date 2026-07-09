using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 会话与刷新令牌服务(设计 §15)——登录建会话、每请求校验活跃、刷新轮换 + 复用检测、登出/强退、在线列表。
/// <para>类 public、方法 virtual 便于覆写(设计 §5.3)。</para>
/// </summary>
public interface ISessionService
{
    /// <summary>登录成功后开会话:落库 + 落缓存 + 存刷新令牌哈希;按单端/限并发策略吊销旧会话。</summary>
    Task OpenAsync(SysUser user, string sessionId, TokenPair pair);

    /// <summary>会话是否活跃(热路径:先读缓存,未命中查库回填)。强退/登出/过期后即为 false。</summary>
    Task<bool> IsActiveAsync(string sessionId);

    /// <summary>用刷新令牌换发新令牌对:校验 + 轮换(旧置 Used)+ 复用检测(重放整会话吊销)。失败抛 40007。</summary>
    Task<RefreshedSession> RefreshAsync(string refreshToken);

    /// <summary>吊销会话(登出 / 强退):标记会话与其刷新令牌失效 + 清缓存。原访问令牌下次请求即 401。</summary>
    Task RevokeAsync(string sessionId);

    /// <summary>吊销某用户的<b>全部</b>活跃会话(停用/删除用户时调用),使其持有的所有访问令牌下次请求即 401。</summary>
    Task RevokeAllForUserAsync(long userId);

    /// <summary>在线会话分页(活跃且未过期)。</summary>
    Task<PagedList<OnlineSessionItem>> ListOnlineAsync(SessionPageInput input);
}
