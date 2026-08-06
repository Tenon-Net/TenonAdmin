using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 会话(设计 §15)——一次登录 = 一个会话,SessionId(JWT sid claim)是<b>强退与在线用户的稳定锚点</b>。
/// 令牌本身短命且不落库;会话落库 + 落缓存,支撑"每请求校验状态""在线列表""强退即失效"。
/// <para>状态用 <see cref="RevokedAt"/> 表达(null=活跃);过期由 <see cref="ExpiresAt"/> 兜底。不物理删,便于审计。</para>
/// </summary>
[SugarTable("sys_session", TableDescription = "会话")]
[SugarIndex("idx_sys_session_sid", nameof(SessionId), OrderByType.Asc, IsUnique = true)]
// 按 UserId 精确匹配是真实热路径(每次登录都走并发/单端策略;停用/删除/改密时的全量吊销也走它),
// 而本表刻意"不物理删,便于审计"——即在一张只增不减的表上全表扫。索引只有建表这一次机会。
[SugarIndex("idx_sys_session_user", nameof(UserId), OrderByType.Asc)]
public class SysSession : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "会话标识(JWT sid)")]
    public string SessionId { get; set; } = "";

    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    /// <summary>登录账号(冗余,在线列表直接展示,免联表)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "登录账号")]
    public string Account { get; set; } = "";

    /// <summary>登录 IP(原文);登录开会话时由当前请求填充</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "登录 IP")]
    public string? Ip { get; set; }

    /// <summary>User-Agent(原文);登录开会话时由当前请求填充,前端解析成浏览器/系统展示</summary>
    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "User-Agent")]
    public string? UserAgent { get; set; }

    [SugarColumn(ColumnDescription = "会话过期时刻(= 刷新令牌有效期)")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 绝对过期时刻(Level3:最长 8 小时;刷新不得突破)。
    /// 默认与 <see cref="ExpiresAt"/> 同语义;Level3 登录时按策略写入,CodeFirst 自动补列。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "绝对过期时刻")]
    public DateTime? AbsoluteExpiresAt { get; set; }

    /// <summary>
    /// 最近活动时间(Level3 闲置判定)。热路径经 Redis 节流后回写,不每请求落库。
    /// null = 功能上线前的存量会话或尚未回写。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最近活动时间")]
    public DateTime? LastActivityAt { get; set; }

    /// <summary>吊销时刻;null = 活跃。登出/强退/单端踢出/复用检测吊销时置值。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "吊销时刻(null=活跃)")]
    public DateTime? RevokedAt { get; set; }
}
