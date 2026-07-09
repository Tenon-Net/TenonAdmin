using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 菜单管理端点(设计 §16)——菜单树(目录/页面/按钮三级)的增删改查,供前端菜单管理页。
/// <para>全 <c>[RolePermission]</c>:超管自动放行;普通管理员须被授予对应菜单权限码
/// (默认种子<b>不</b>把本组端点授给默认角色,故非超管默认不可管理菜单,安全默认拒绝 §14)。</para>
/// <para>只读的门户接口("我的模块/菜单树")在 <c>PersonalController</c>;本控制器只管管理端全量 CRUD。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/menu")]
public class MenuController(IMenuService menuService) : ControllerBase
{
    /// <summary>全量菜单树(管理端,含停用节点与按钮,全字段)</summary>
    [HttpGet("tree")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<MenuTreeNode>>> Tree()
    {
        var data = await menuService.GetTreeAsync();
        return Result<IReadOnlyList<MenuTreeNode>>.Ok(data);
    }

    /// <summary>新增菜单,返回新 Id</summary>
    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增菜单")]
    public async Task<Result<long>> Add(MenuInput input)
    {
        var id = await menuService.CreateAsync(input);
        return Result<long>.Ok(id);
    }

    /// <summary>更新菜单</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新菜单")]
    public async Task<Result<bool>> Update(long id, MenuInput input)
    {
        await menuService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除菜单(软删;带子节点则拒删)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除菜单")]
    public async Task<Result<bool>> Delete(long id)
    {
        await menuService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }
}
