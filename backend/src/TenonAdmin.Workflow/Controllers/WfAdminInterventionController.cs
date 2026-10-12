using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程监控的管理员干预。三条路由各自挂 <c>[RolePermission]</c>,
/// 权限码与 <c>WorkflowMenuSeed</c> 里的监控按钮一致。
/// </summary>
[ApiController]
[Route("api/v1/workflow/instance")]
[ActiveSession]
public class WfAdminInterventionController(
    IWfAdminInterventionService interventions,
    ICurrentUser currentUser) : ControllerBase
{
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>强制转办:待办换人,或给没有待办的阻断节点指定办理人。</summary>
    [HttpPost("force-transfer")]
    [RolePermission]
    [OperationLog("强制转办")]
    public async Task<Result<WfEngineResult>> ForceTransfer(
        WfAdminForceTransferInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await interventions.ForceTransferAsync(
                input.InstanceId,
                CurrentUserId,
                input.ToUserId,
                input.Reason,
                input.NodeId,
                input.RequestId,
                cancellationToken));

    /// <summary>终止运行中的实例,原因必填。</summary>
    [HttpPost("terminate")]
    [RolePermission]
    [OperationLog("终止流程")]
    public async Task<Result<WfEngineResult>> Terminate(
        WfAdminTerminateInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await interventions.TerminateAsync(
                input.InstanceId,
                CurrentUserId,
                input.Reason,
                input.RequestId,
                cancellationToken));

    /// <summary>跳过没有待办的阻断节点。</summary>
    [HttpPost("skip")]
    [RolePermission]
    [OperationLog("跳过节点")]
    public async Task<Result<WfEngineResult>> Skip(
        WfAdminSkipNodeInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await interventions.SkipNodeAsync(
                input.InstanceId,
                CurrentUserId,
                input.Reason,
                input.NodeId,
                input.RequestId,
                cancellationToken));
}
