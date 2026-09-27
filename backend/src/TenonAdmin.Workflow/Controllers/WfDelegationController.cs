using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>长期委托规则管理。规则权限独立于一次性任务委托权限。</summary>
[ApiController]
[Route("api/v1/workflow/delegation")]
[ActiveSession]
public class WfDelegationController(IWfDelegationService service) : ControllerBase
{
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<WfDelegationRuleOutput>>> Page(
        [FromQuery] WfDelegationRulePageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfDelegationRuleOutput>>.Ok(await service.PageAsync(input, cancellationToken));

    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增长期委托规则")]
    public async Task<Result<WfDelegationRuleOutput>> Add(
        WfDelegationRuleInput input,
        CancellationToken cancellationToken) =>
        Result<WfDelegationRuleOutput>.Ok(await service.AddAsync(input, cancellationToken));

    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新长期委托规则")]
    public async Task<Result<WfDelegationRuleOutput>> Update(
        long id,
        WfDelegationRuleInput input,
        CancellationToken cancellationToken) =>
        Result<WfDelegationRuleOutput>.Ok(await service.UpdateAsync(id, input, cancellationToken));

    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除长期委托规则")]
    public async Task<Result<bool>> Delete(
        long id,
        [FromQuery] string? requestId,
        CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, requestId, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
