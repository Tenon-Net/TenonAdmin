using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>通知类型:普通通知 / 系统公告。前端按类型展示不同标签/图标。</summary>
public enum NoticeType
{
    /// <summary>普通通知</summary>
    Notice = 1,

    /// <summary>系统公告</summary>
    Announcement = 2,
}

/// <summary>
/// 系统通知表(设计 §4 消息中心,轮询模型)——管理员发布的广播消息,发布即对全体用户可见。
/// <para>每用户的已读状态单独记在 <see cref="SysNoticeRead"/>;发布时间即审计字段 <c>CreateTime</c>,不再另存。</para>
/// </summary>
[SugarTable("sys_notice", TableDescription = "系统通知")]
public class SysNotice : BaseEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";

    // ponytail: 正文限 2000 字符(经全局 string→nvarchar 映射跨方言安全);要富文本/超长正文再改 text 列 + 按方言映射。
    [SugarColumn(Length = 2000, IsNullable = true, ColumnDescription = "正文")]
    public string? Content { get; set; }

    [SugarColumn(ColumnDescription = "类型(1 通知 / 2 公告)")]
    public NoticeType Type { get; set; } = NoticeType.Notice;
}
