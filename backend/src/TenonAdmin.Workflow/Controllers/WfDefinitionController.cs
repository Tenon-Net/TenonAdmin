using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程定义管理端点(设计方案 §七):CRUD + 发布/停用 + 版本历史。
/// 全部 <c>[RolePermission]</c>(权限码=路由);写操作挂 <c>[OperationLog]</c>。
/// </summary>
[ApiController]
[Route("api/v1/workflow/definition")]
public class WfDefinitionController(IWfDefinitionService definitionService) : ControllerBase
{
    /// <summary>分页查询流程定义</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<WfDefinition>>> Page(
        [FromQuery] WfDefinitionPageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfDefinition>>.Ok(await definitionService.PageAsync(input, cancellationToken));

    /// <summary>已发布版本历史(按版本号降序)</summary>
    [HttpGet("versions/{id:long}")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<WfDefinitionVersionOutput>>> Versions(
        long id,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<WfDefinitionVersionOutput>>.Ok(
            await definitionService.ListVersionsAsync(id, cancellationToken));

    /// <summary>定义详情(含草稿模型)</summary>
    [HttpGet("{id:long}")]
    [RolePermission]
    public async Task<Result<WfDefinitionDetailOutput>> Get(
        long id,
        CancellationToken cancellationToken) =>
        Result<WfDefinitionDetailOutput>.Ok(await definitionService.GetAsync(id, cancellationToken));

    /// <summary>新增流程定义(草稿)</summary>
    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增流程定义")]
    public async Task<Result<long>> Add(
        WfDefinitionInput input,
        CancellationToken cancellationToken) =>
        Result<long>.Ok(await definitionService.AddAsync(input, cancellationToken));

    /// <summary>更新流程定义(元数据 + 草稿模型)</summary>
    [HttpPost("update")]
    [RolePermission]
    [OperationLog("更新流程定义")]
    public async Task<Result<bool>> Update(
        WfDefinitionInput input,
        CancellationToken cancellationToken)
    {
        await definitionService.UpdateAsync(input, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>发布当前草稿为新版本快照</summary>
    [HttpPost("publish")]
    [RolePermission]
    [OperationLog("发布流程定义")]
    public async Task<Result<int>> Publish(
        WfDefinitionIdInput input,
        CancellationToken cancellationToken) =>
        Result<int>.Ok(await definitionService.PublishAsync(input.Id, cancellationToken));

    /// <summary>停用流程定义(不可新发起)</summary>
    [HttpPost("disable")]
    [RolePermission]
    [OperationLog("停用流程定义")]
    public async Task<Result<bool>> Disable(
        WfDefinitionIdInput input,
        CancellationToken cancellationToken)
    {
        await definitionService.DisableAsync(input.Id, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除流程定义(软删;有在途单据则拒绝)</summary>
    [HttpDelete("{id:long}")]
    [RolePermission]
    [OperationLog("删除流程定义")]
    public async Task<Result<bool>> Delete(long id, CancellationToken cancellationToken)
    {
        await definitionService.DeleteAsync(id, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
