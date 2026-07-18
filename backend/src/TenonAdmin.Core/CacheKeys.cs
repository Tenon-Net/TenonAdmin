namespace TenonAdmin.Core;

/// <summary>
/// 缓存逻辑键(禁硬编码字符串纪律,设计 §6/§15)——所有缓存键在此集中定义,不散落魔法串。
/// <para>这里给的是<b>不含前缀</b>的逻辑键;<c>Cache:KeyPrefix</c>(默认 <c>tenon:</c>)由
/// <see cref="ICacheProvider"/> 实现统一追加。故实际 Redis 键形如 <c>tenon:perm:123</c>(对应设计 §15 命名)。</para>
/// </summary>
public static class CacheKeys
{
    /// <summary>某用户当前生效的权限码集合(RBAC 聚合结果;授权变更时按 userId 精确失效)</summary>
    public static string UserPermissions(long userId) => $"perm:{userId}";

    /// <summary>某用户当前生效的数据范围(多角色范围合并结果;角色范围/用户角色变更时按 userId 失效)</summary>
    public static string UserDataScope(long userId) => $"scope:{userId}";

    /// <summary>会话活跃状态(热路径:每受保护请求校验;登出/强退时移除,设计 §15)</summary>
    public static string Session(string sessionId) => $"session:{sessionId}";

    /// <summary>某字典类型下的字典项列表(前端下拉数据源;字典变更时按类型码失效)</summary>
    public static string DictItems(string typeCode) => $"dict:{typeCode}";

    /// <summary>某系统配置项的值(按键缓存;配置变更时按键失效)</summary>
    public static string Config(string key) => $"config:{key}";

    /// <summary>某账号的连续登录失败次数(登录锁定用;成功登录即清除,窗口过期自动归零,设计 §14)</summary>
    public static string LoginFail(string account) => $"loginfail:{account}";

    /// <summary>某验证码票据对应的明文(登录时一次性校验并消费,短 TTL 过期,设计 §14)</summary>
    public static string Captcha(string captchaId) => $"captcha:{captchaId}";

    /// <summary>某短信验证码明文(§14 登录加固)。<paramref name="purpose"/>=<c>mfa</c> 时 key 为挑战 Id(码绑定已过密码的用户)、<c>login</c> 时为规范化手机号;一次性消费,TTL 过期。</summary>
    public static string SmsCode(string purpose, string key) => $"sms:code:{purpose}:{key}";

    /// <summary>某短信验证码的错误尝试计数(达上限即作废该码,与码同 TTL)</summary>
    public static string SmsCodeAttempts(string purpose, string key) => $"sms:try:{purpose}:{key}";

    /// <summary>某手机号的发送冷却标记(存在即冷却中,TTL = 重发间隔;防高频骚扰发码)</summary>
    public static string SmsCooldown(string phone) => $"sms:cd:{phone}";

    /// <summary>某手机号当日发送计数(日期编进键:换天即换键自动归零,同 <see cref="RateLimit"/> 的成法;TTL 24h 回收)</summary>
    public static string SmsDailyCount(string phone, string day) => $"sms:day:{phone}:{day}";

    /// <summary>MFA 挑战票据 → 已过密码校验的 userId(短信码必须绑定该用户而非仅手机号;一次性消费,TTL 同码)</summary>
    public static string MfaChallenge(string challengeId) => $"mfa:{challengeId}";

    /// <summary>外部登录授权态(<c>state</c>)→ {providerCode, nonce, codeVerifier, redirectUri}(设计批次 D)。防 CSRF 兼承载 PKCE/nonce;回调时按 <c>state</c> 一次性消费,短 TTL 过期。</summary>
    public static string OAuthState(string state) => $"oauth:state:{state}";

    /// <summary>外部登录成功后令牌对的一次性交付票据 → LoginOutput(设计批次 D)。令牌<b>不进</b>重定向 URL,前端凭票据换取;一次性消费(<see cref="ICacheProvider.GetAndRemoveAsync{T}"/>),短 TTL 过期。</summary>
    public static string OAuthTicket(string ticket) => $"oauth:ticket:{ticket}";

    /// <summary>
    /// 固定窗口限流计数(设计 §12/§14)。<paramref name="bucket"/> 为 <c>auth</c>(认证端点,更严)或 <c>all</c>;
    /// <paramref name="windowIndex"/> 是<b>窗口序号</b>(<c>unixSeconds / windowSeconds</c>)。
    /// <para>把窗口序号<b>编进键</b>:换窗口即换键,计数自然归零,无需任何清理逻辑(旧键靠 TTL 回收)。
    /// 计数走 <see cref="ICacheProvider.IncrementAsync"/> ⇒ 装 Redis 即<b>跨副本共享</b>(否则 N 个副本 = N × 阈值);
    /// 不装则退回进程内,与单实例行为一致。</para>
    /// </summary>
    public static string RateLimit(string bucket, string clientIp, long windowIndex) =>
        $"rl:{bucket}:{clientIp}:{windowIndex}";

    /// <summary>
    /// 磁盘回收某一轮的<b>租约</b>(<paramref name="tickBucket"/> = <c>unixSeconds / 回收间隔</c>)。
    /// <para>GC 后台任务在每个副本上都注册着;拿这个键做一次原子自增,只有得到 1 的副本干活,
    /// 其余跳过 —— 免得 N 个副本同时扫同一批文件(不会出错,但白白重复 I/O 并刷告警)。</para>
    /// </summary>
    public static string FileGcTick(long tickBucket) => $"filegc:tick:{tickBucket}";

    /// <summary>
    /// 门户缓存<b>代际计数器</b>:菜单/模块 CRUD、角色-菜单、用户-角色变更时自增,令下方门户键整体惰性失效。
    /// <para>因 <see cref="ICacheProvider"/> 无前缀/批量删除,而门户结果受影响面是全局的、菜单树键又是二维,
    /// 用递增代际做 O(1) 失效(旧代际键不再被读到、由 TTL 回收)最简且正确。计数器本身不设过期(进程内重启即归零、
    /// 与缓存同生共死;Redis 下跨实例共享保证分布式一致)。</para>
    /// </summary>
    public const string PortalGeneration = "portal:gen";

    /// <summary>某用户可访问模块列表(门户导航;随 <see cref="PortalGeneration"/> 代际失效)</summary>
    public static string PortalModules(long userId, long generation) => $"portal:mod:{userId}:{generation}";

    /// <summary>某用户在某模块下的侧边栏菜单树(门户导航;随 <see cref="PortalGeneration"/> 代际失效)</summary>
    public static string PortalMenuTree(long userId, long moduleId, long generation) => $"portal:menu:{userId}:{moduleId}:{generation}";
}
