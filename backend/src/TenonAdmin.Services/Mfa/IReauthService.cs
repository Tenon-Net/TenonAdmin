namespace TenonAdmin.Services;

/// <summary>
/// 高风险操作短时再次认证授予/判定(约 5 分钟窗口)。
/// 缓存支撑;按用户 + 会话 sid 绑定,避免跨会话复用。
/// 控制器/过滤器经本接口检查,不在 AuthService 内硬编码路由列表。
/// </summary>
public interface IReauthService
{
    /// <summary>授予再次认证(写入缓存,TTL = 配置窗口,默认 5 分钟)。</summary>
    /// <param name="userId">用户 Id</param>
    /// <param name="method">认证方法标记(totp/password 等,仅审计/诊断)</param>
    /// <param name="sessionId">JWT sid;空则仅按用户(兼容测试/无会话场景)</param>
    Task GrantAsync(long userId, string method, string? sessionId = null);

    /// <summary>
    /// 是否在有效窗口内持有 reauth 授予。
    /// <paramref name="within"/> 为 null 时使用配置默认窗口(仅检查键是否仍存在——键 TTL 即窗口)。
    /// </summary>
    Task<bool> IsGrantedAsync(long userId, TimeSpan? within = null, string? sessionId = null);

    /// <summary>主动吊销单会话(登出/会话吊销/安全上下文变化时调用)。</summary>
    Task RevokeAsync(long userId, string? sessionId = null);

    /// <summary>吊销该用户全部会话上的 reauth(改密/强退全部等)。</summary>
    Task RevokeAllForUserAsync(long userId);

    /// <summary>
    /// 校验 TOTP 或当前密码后授予 reauth。
    /// method=totp 需用户已绑定;method=password 验当前口令。
    /// </summary>
    Task VerifyAndGrantAsync(long userId, ReauthInput input, string? sessionId = null);
}
