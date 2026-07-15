using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>通知类型:普通通知 / 系统公告 / 站内消息。前端按类型展示不同标签/图标。</summary>
public enum NoticeType
{
    /// <summary>普通通知</summary>
    Notice = 1,

    /// <summary>系统公告</summary>
    Announcement = 2,

    /// <summary>站内消息</summary>
    Message = 3,
}

/// <summary>
/// 接收范围:全体 / 指定角色 / 指定用户。<b>All = 0 是刻意的</b>——CodeFirst 给旧行补的默认值即 0,
/// 历史广播通知自动读作 All,继续对全体可见,无需手写迁移。Role/User 时具体目标记在 <see cref="SysNoticeReceiver"/>。
/// </summary>
public enum ReceiverType
{
    /// <summary>全体用户(不写接收行)</summary>
    All = 0,

    /// <summary>指定角色</summary>
    Role = 1,

    /// <summary>指定用户</summary>
    User = 2,
}

/// <summary>
/// 系统通知表(设计 §4 消息中心,轮询模型)——管理员发布的消息,可发给全体 / 指定角色 / 指定用户。
/// <para>每用户的已读状态单独记在 <see cref="SysNoticeRead"/>;定向目标记在 <see cref="SysNoticeReceiver"/>;
/// 发布时间即审计字段 <c>CreateTime</c>,不再另存。</para>
/// </summary>
[SugarTable("sys_notice", TableDescription = "系统通知")]
public class SysNotice : BaseEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";

    // ponytail: 正文限 2000 字符(经全局 string→nvarchar 映射跨方言安全);要富文本/超长正文再改 text 列 + 按方言映射。
    [SugarColumn(Length = 2000, IsNullable = true, ColumnDescription = "正文")]
    public string? Content { get; set; }

    [SugarColumn(ColumnDescription = "类型(1 通知 / 2 公告 / 3 消息)")]
    public NoticeType Type { get; set; } = NoticeType.Notice;

    [SugarColumn(ColumnDescription = "接收范围(0 全体 / 1 角色 / 2 用户)")]
    public ReceiverType ReceiverType { get; set; } = ReceiverType.All;
}
