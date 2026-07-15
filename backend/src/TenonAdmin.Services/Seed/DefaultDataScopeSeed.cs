using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

internal sealed class DefaultDataScopeSeed : ISeedData<SysRoleDataScope>
{
    /// <summary>连接表:唯一索引在 RoleId,代理主键会漂,按 RoleId 判存(见 <see cref="ISeedData{T}.DedupColumns"/>)。</summary>
    public string[] DedupColumns => [nameof(SysRoleDataScope.RoleId)];

    public IEnumerable<SysRoleDataScope> HasData() =>
    [
        new SysRoleDataScope { Id = 1, RoleId = 2, ScopeType = DataScopeType.All,            CustomOrgIds = "" },
        new SysRoleDataScope { Id = 2, RoleId = 3, ScopeType = DataScopeType.Org,            CustomOrgIds = "" },
        new SysRoleDataScope { Id = 3, RoleId = 4, ScopeType = DataScopeType.OrgAndChildren, CustomOrgIds = "" },
        new SysRoleDataScope { Id = 4, RoleId = 5, ScopeType = DataScopeType.Self,           CustomOrgIds = "" },
        new SysRoleDataScope { Id = 5, RoleId = 6, ScopeType = DataScopeType.Custom,         CustomOrgIds = "2,6" },
    ];
}
