using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>角色授权入参:把一组菜单(权限码)全量授予某角色</summary>
public record SetRoleMenusInput
{
    /// <summary>目标角色 Id</summary>
    public long RoleId { get; init; }

    /// <summary>授予的菜单 Id 列表(全量替换该角色现有授权;空列表 = 收回全部)</summary>
    public IReadOnlyCollection<long> MenuIds { get; init; } = [];
}

/// <summary>角色授权用户入参:把一组用户全量关联到某角色</summary>
public record SetRoleUsersInput
{
    /// <summary>目标角色 Id</summary>
    public long RoleId { get; init; }

    /// <summary>关联的用户 Id 列表(全量替换;空列表 = 收回全部)</summary>
    public IReadOnlyCollection<long> UserIds { get; init; } = [];
}

/// <summary>角色数据范围入参</summary>
public record SetRoleDataScopeInput
{
    /// <summary>目标角色 Id</summary>
    public long RoleId { get; init; }

    /// <summary>范围类型(全部/本机构/本机构及以下/仅本人/自定义)</summary>
    public DataScopeType ScopeType { get; init; }

    /// <summary>自定义机构 Id 列表,仅 ScopeType=Custom 时有意义</summary>
    public IReadOnlyCollection<long>? CustomOrgIds { get; init; }
}
