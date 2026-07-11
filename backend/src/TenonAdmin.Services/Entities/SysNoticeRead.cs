using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 通知已读回执表——记录"某用户读过某通知"。存在一行即视为已读;不存在即未读。
/// <para>(UserId, NoticeId) 唯一,重复标记幂等;已读时间即审计字段 <c>CreateTime</c>。回执行只增不删。</para>
/// </summary>
[SugarTable("sys_notice_read", TableDescription = "通知已读回执")]
[SugarIndex("idx_sys_notice_read", nameof(UserId), OrderByType.Asc, nameof(NoticeId), OrderByType.Asc, IsUnique = true)]
public class SysNoticeRead : BaseEntity
{
    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDescription = "通知 Id")]
    public long NoticeId { get; set; }
}
