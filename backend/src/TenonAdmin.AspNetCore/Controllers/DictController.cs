using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 字典管理端点(类型 + 项的标准增删改查)。管理端点全部 <c>[RolePermission]</c>——超管放行,
/// 普通用户需被授予对应路由权限码。<c>items/{typeCode}</c> 例外(QA12):它是前端下拉/字典标签渲染的
/// 热路径读取,任何已登录用户都该能取到(表单/列表到处要显示字典项),故降为 <c>[ActiveSession]</c>
/// ——只要求会话有效,不占用一个专门的权限码;管理端点(增删改)仍是 <c>[RolePermission]</c>。
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

    /// <summary>批量删除字典类型(各自级联删项)</summary>
    [HttpPost("type/batch-delete")]
    [RolePermission]
    [OperationLog("批量删除字典类型")]
    public async Task<Result<bool>> BatchDeleteTypes(BatchDeleteInput input)
    {
        await dictService.DeleteTypesBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// 按类型编码取启用中的字典项列表(前端下拉数据源,读穿透缓存)。
    /// <c>[ActiveSession]</c>(QA12):任何已登录用户都能取,不挂专门权限码。
    /// </summary>
    [HttpGet("items/{typeCode}")]
    [ActiveSession]
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

    /// <summary>批量删除字典项</summary>
    [HttpPost("item/batch-delete")]
    [RolePermission]
    [OperationLog("批量删除字典项")]
    public async Task<Result<bool>> BatchDeleteItems(BatchDeleteInput input)
    {
        await dictService.DeleteItemsBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }
}
