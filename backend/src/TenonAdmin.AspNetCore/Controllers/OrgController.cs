using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 机构管理端点——机构树的增删改查。后端只返回平铺列表,前端凭 <c>ParentId</c> 自行拼树。
/// </summary>
[ApiController]
[Route("api/v1/sys/org")]
public class OrgController(IOrgService orgService) : ControllerBase
{
    /// <summary>取全部机构(平铺列表,按 Sort、Id 排序)</summary>
    [HttpGet("list")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<SysOrg>>> List()
    {
        var data = await orgService.ListAsync();
        return Result<IReadOnlyList<SysOrg>>.Ok(data);
    }

    /// <summary>按 Id 取单个机构</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<SysOrg>> Get(long id)
    {
        var data = await orgService.GetAsync(id);
        return Result<SysOrg>.Ok(data);
    }

    /// <summary>新增机构,返回新 Id</summary>
    [HttpPost("add")]
    [RolePermission]
    public async Task<Result<long>> Add(OrgInput input)
    {
        var id = await orgService.AddAsync(input);
        return Result<long>.Ok(id);
    }

    /// <summary>更新机构</summary>
    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Update(long id, OrgInput input)
    {
        await orgService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除机构(软删除;若仍有子机构挂靠则拒绝)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id)
    {
        await orgService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>复制机构子树(整支克隆挂到源节点同级),返回新根 Id</summary>
    [HttpPost("{id}/copy")]
    [RolePermission]
    public async Task<Result<long>> Copy(long id, [FromBody] OrgCopyInput? input = null)
    {
        var newId = await orgService.CopyAsync(id, input?.Name);
        return Result<long>.Ok(newId);
    }
}
