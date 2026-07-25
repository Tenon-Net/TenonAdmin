using TenonAdmin.Core;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例导出档案——消费方给自己的 <see cref="DataEntity"/> 接导出的抄写样板。
/// <para>
/// 档案只声明"能导哪些列";取数仍走 <see cref="ISampleDocService.ListAsync"/>,与列表**同源**。
/// 正因为同源,导出自动继承 <c>IOrgScoped</c> 全局数据范围过滤(设计 §6 招牌能力):
/// 同一个导出端点,总部账号导出全量、分公司账号只导出本机构那几行,
/// 而 <c>SampleDocService</c> 里一行 <c>WHERE create_org_id</c> 都不用写。
/// </para>
/// <para>
/// 内核自带的 <c>sys_user</c> / <c>sys_op_log</c> 都是 <c>BaseEntity</c>(不受机构隔离,按权限码放行),
/// 所以这条能力只能由一张真正的 <c>DataEntity</c> 业务表来演示——这就是本档案存在的理由。
/// </para>
/// </summary>
public class SampleDocExportProfile : IExportProfile
{
    /// <inheritdoc />
    public virtual string Code => "sample-doc";

    /// <inheritdoc />
    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "标题", Width = 24 },
    ];
}
