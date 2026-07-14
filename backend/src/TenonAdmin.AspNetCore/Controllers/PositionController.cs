using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>职位管理端点——职位字典的标准增改删查,权限码即路由(挂 <c>[RolePermission]</c>,设计 §6)。</summary>
[ApiController]
[Route("api/v1/sys/position")]
public class PositionController(IPositionService positionService) : ControllerBase
{
    /// <summary>分页查询职位</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<SysPosition>>> Page([FromQuery] PositionPageInput input) =>
        Result<PagedList<SysPosition>>.Ok(await positionService.PageAsync(input));

    /// <summary>按 Id 取单条职位</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<SysPosition>> Get(long id) =>
        Result<SysPosition>.Ok(await positionService.GetAsync(id));

    /// <summary>新增职位</summary>
    [HttpPost("add")]
    [RolePermission]
    public async Task<Result<long>> Add(PositionInput input) =>
        Result<long>.Ok(await positionService.AddAsync(input));

    /// <summary>更新职位</summary>
    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Update(long id, PositionInput input)
    {
        await positionService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除职位</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id)
    {
        await positionService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

}
