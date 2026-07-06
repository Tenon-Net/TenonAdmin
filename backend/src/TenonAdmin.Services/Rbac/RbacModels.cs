namespace TenonAdmin.Services;

/// <summary>角色授权入参:把一组菜单(权限码)全量授予某角色</summary>
public record SetRoleMenusInput
{
    /// <summary>目标角色 Id</summary>
    public long RoleId { get; init; }

    /// <summary>授予的菜单 Id 列表(全量替换该角色现有授权;空列表 = 收回全部)</summary>
    public IReadOnlyCollection<long> MenuIds { get; init; } = [];
}
