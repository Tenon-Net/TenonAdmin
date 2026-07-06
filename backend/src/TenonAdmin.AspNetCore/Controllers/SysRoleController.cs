using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 角色授权端点(设计 §6,T1)。当前只暴露"给角色配菜单(权限码)"——用户/角色的 CRUD 属 T2。
/// <para>挂 <c>[RolePermission]</c>:超管直接放行;普通用户需被授予本接口的权限码
/// <c>PUT:/api/v1/sys/role/menu</c>(即种子里"角色授权"菜单)才能代管授权(安全默认拒绝,§14)。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/role")]
public class SysRoleController(IRbacService rbac) : ControllerBase
{
    /// <summary>全量设置某角色授予的菜单;成功后受影响用户权限缓存即时失效(设计 §6)。</summary>
    [HttpPut("menu")]
    [RolePermission]
    public async Task<Result<bool>> SetMenus(SetRoleMenusInput input)
    {
        await rbac.SetRoleMenusAsync(input.RoleId, input.MenuIds);
        return Result<bool>.Ok(true);
    }
}
