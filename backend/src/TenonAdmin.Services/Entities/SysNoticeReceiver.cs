using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 通知接收目标表——通知定向到"指定角色 / 指定用户"时,每个目标一行。
/// <para><see cref="ReceiverId"/> 是角色 Id 还是用户 Id,由父通知的 <see cref="SysNotice.ReceiverType"/> 决定(不在本行冗余);
/// 接收范围为 <see cref="ReceiverType.All"/> 时不写任何行。(NoticeId, ReceiverId) 唯一。回执式只增不删,删通知留孤儿无害。</para>
/// </summary>
[SugarTable("sys_notice_receiver", TableDescription = "通知接收目标")]
[SugarIndex("idx_sys_notice_receiver", nameof(NoticeId), OrderByType.Asc, nameof(ReceiverId), OrderByType.Asc, IsUnique = true)]
public class SysNoticeReceiver : BaseEntity
{
    [SugarColumn(ColumnDescription = "通知 Id")]
    public long NoticeId { get; set; }

    [SugarColumn(ColumnDescription = "接收目标 Id(角色 Id 或用户 Id,取决于父通知 ReceiverType)")]
    public long ReceiverId { get; set; }
}
