using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 模块/应用管理端点(多应用门户)——模块的增删改查。全 <c>[RolePermission]</c>:超管自动放行,
/// 普通管理员须被授予对应菜单权限码;默认种子<b>不</b>把本组端点授给默认角色,故非超管默认不可管理模块。
/// </summary>
[ApiController]
[Route("api/v1/sys/module")]
public class ModuleController(IModuleService moduleService) : ControllerBase
{
    /// <summary>取全部模块(平铺列表,按 Sort、Id 排序)</summary>
    [HttpGet("list")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SysModule>>> List()
    {
        var data = await moduleService.ListAsync();
        return Result<IReadOnlyList<SysModule>>.Ok(data);
    }

    /// <summary>按 Id 取单个模块</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<SysModule>> Get(long id)
    {
        var data = await moduleService.GetAsync(id);
        return Result<SysModule>.Ok(data);
    }

    /// <summary>新增模块,返回新 Id</summary>
    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增模块")]
    public async Task<Result<long>> Add(ModuleInput input)
    {
        var id = await moduleService.AddAsync(input);
        return Result<long>.Ok(id);
    }

    /// <summary>更新模块</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新模块")]
    public async Task<Result<bool>> Update(long id, ModuleInput input)
    {
        await moduleService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除模块(软删除;内置 system 模块受保护不可删)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除模块")]
    public async Task<Result<bool>> Delete(long id)
    {
        await moduleService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }
}
