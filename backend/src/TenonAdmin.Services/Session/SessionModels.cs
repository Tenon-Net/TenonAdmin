using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>会话缓存值(热路径:每请求校验会话是否活跃,免每次查库)</summary>
public record SessionCacheInfo
{
    public required long UserId { get; init; }
    public required DateTime ExpiresAt { get; init; }

    /// <summary>绝对过期(Level3);非 Level3 可与 ExpiresAt 对齐</summary>
    public DateTime AbsoluteExpiresAt { get; init; }

    /// <summary>最近活动(UTC);闲置判定用</summary>
    public DateTime? LastActivityAt { get; init; }

    /// <summary>闲置超时分钟;0 = 不检闲置(非 Level3)</summary>
    public int IdleMinutes { get; init; }

    /// <summary>是否按 MFA 会话策略(并发/闲置)</summary>
    public bool IsMfa { get; init; }
}

/// <summary>刷新换发的结果:新令牌对 + 用户(供上层组装登录出参)</summary>
public record RefreshedSession(TokenPair Pair, SysUser User);

/// <summary>在线会话列表出参(强退按 SessionId)</summary>
public record OnlineSessionItem
{
    public required string SessionId { get; init; }
    public required long UserId { get; init; }
    public required string Account { get; init; }
    public string? Ip { get; init; }

    /// <summary>登录时的 User-Agent 原串(前端解析成浏览器/系统展示,便于同一账号多端分辨)</summary>
    public string? UserAgent { get; init; }

    public DateTime LoginTime { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>在线会话分页入参</summary>
public record SessionPageInput : PageInputBase
{
    /// <summary>按用户过滤(可选)</summary>
    public long? UserId { get; init; }
}
