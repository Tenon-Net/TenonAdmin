using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 用户导出档案:可导列声明(excel-ledger §9 G3)。实际取数走 <see cref="IUserService.ExportAsync"/>
/// (与列表共用 <c>BuildListQuery</c>,坑 1),字典 value→label 在端点组装 <see cref="ExportSheet"/> 时完成。
/// </summary>
public class UserExportProfile : IExportProfile
{
    /// <inheritdoc />
    public virtual string Code => "sys-user";

    /// <inheritdoc />
    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Account", Title = "登录账号", Width = 18 },
        new() { Key = "Name", Title = "姓名", Width = 14 },
        new() { Key = "Nickname", Title = "昵称", Width = 14 },
        new() { Key = "Phone", Title = "手机号", Width = 14 },
        new() { Key = "Email", Title = "邮箱", Width = 22 },
        new() { Key = "Gender", Title = "性别", DictTypeCode = "gender", Width = 10 },
        new() { Key = "OrgName", Title = "所属机构", Width = 18 },
        new() { Key = "PositionName", Title = "职位", Width = 14 },
        new() { Key = "DirectorName", Title = "直属主管", Width = 14 },
        new() { Key = "Enabled", Title = "启用状态", Width = 12 },
        new() { Key = "IsSuperAdmin", Title = "超级管理员", DefaultSelected = false, Width = 12 },
        new() { Key = "CreateTime", Title = "创建时间", Width = 20 },
    ];
}
