using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 字典管理端点(类型 + 项的标准增删改查)。<c>items/{typeCode}</c> 是前端下拉的缓存数据源;
/// 全部 <c>[RolePermission]</c>——超管放行,普通用户需被授予对应路由权限码。
/// </summary>
[ApiController]
[Route("api/v1/sys/dict")]
[Module("Dict")]   // 可经 Api:DisabledModules=["Dict"] 关闭
public class DictController(IDictService dictService) : ControllerBase
{
    /// <summary>分页查询字典类型</summary>
    [HttpGet("type/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysDictType>>> PageTypes([FromQuery] DictTypePageInput input) =>
        Result<PagedList<SysDictType>>.Ok(await dictService.PageTypesAsync(input));

    /// <summary>按 Id 取字典类型</summary>
    [HttpGet("type/{id}")]
    [RolePermission]
    public async Task<Result<SysDictType>> GetType(long id) =>
        Result<SysDictType>.Ok(await dictService.GetTypeAsync(id));

    /// <summary>新增字典类型</summary>
    [HttpPost("type")]
    [RolePermission]
    public async Task<Result<long>> AddType(DictTypeInput input) =>
        Result<long>.Ok(await dictService.AddTypeAsync(input));

    /// <summary>更新字典类型(Code 创建后不可变,此处仅 Name/Sort/Enabled/Remark 生效)</summary>
    [HttpPut("type/{id}")]
    [RolePermission]
    public async Task<Result<bool>> UpdateType(long id, DictTypeInput input)
    {
        await dictService.UpdateTypeAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除字典类型(级联删除其下全部字典项)</summary>
    [HttpDelete("type/{id}")]
    [RolePermission]
    public async Task<Result<bool>> DeleteType(long id)
    {
        await dictService.DeleteTypeAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>按类型编码取启用中的字典项列表(前端下拉数据源,读穿透缓存)</summary>
    [HttpGet("items/{typeCode}")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SysDictItem>>> GetItems(string typeCode) =>
        Result<IReadOnlyList<SysDictItem>>.Ok(await dictService.GetItemsByTypeAsync(typeCode));

    /// <summary>分页查询字典项(管理端:含停用项,不走缓存)</summary>
    [HttpGet("item/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysDictItem>>> PageItems([FromQuery] DictItemPageInput input) =>
        Result<PagedList<SysDictItem>>.Ok(await dictService.PageItemsAsync(input));

    /// <summary>新增字典项</summary>
    [HttpPost("item")]
    [RolePermission]
    public async Task<Result<long>> AddItem(DictItemInput input) =>
        Result<long>.Ok(await dictService.AddItemAsync(input));

    /// <summary>更新字典项</summary>
    [HttpPut("item/{id}")]
    [RolePermission]
    public async Task<Result<bool>> UpdateItem(long id, DictItemInput input)
    {
        await dictService.UpdateItemAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除字典项</summary>
    [HttpDelete("item/{id}")]
    [RolePermission]
    public async Task<Result<bool>> DeleteItem(long id)
    {
        await dictService.DeleteItemAsync(id);
        return Result<bool>.Ok(true);
    }
}
