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
