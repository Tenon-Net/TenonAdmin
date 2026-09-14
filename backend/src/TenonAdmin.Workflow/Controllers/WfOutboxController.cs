using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>工作流 outbox 死信监控与人工重放(Task 8c)。不开发前端页面。</summary>
[ApiController]
[Route("api/v1/workflow/outbox")]
[ActiveSession]
public class WfOutboxController(IWfOutboxService service) : ControllerBase
{
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<WfOutboxOutput>>> Page(
        [FromQuery] WfOutboxPageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfOutboxOutput>>.Ok(await service.PageAsync(input, cancellationToken));

    [HttpPost("{id:long}/replay")]
    [RolePermission]
    [OperationLog("重放工作流 outbox 死信")]
    public async Task<Result<WfOutboxOutput>> Replay(
        long id,
        [FromQuery] string? requestId,
        CancellationToken cancellationToken) =>
        Result<WfOutboxOutput>.Ok(await service.ReplayAsync(id, requestId, cancellationToken));
}
