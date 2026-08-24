using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批任务端点(设计方案 §七):待办 / 已办 + 同意 / 拒绝 / 转办。
/// 全部 <c>[ActiveSession]</c>——办理人取自令牌,不接受任意 userId。
/// </summary>
[ApiController]
[Route("api/v1/workflow/task")]
[ActiveSession]
public class WfTaskController(
    IWfTaskService taskService,
    ICurrentUser currentUser) : ControllerBase
{
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>我的待办</summary>
    [HttpGet("todo")]
    public async Task<Result<PagedList<WfTodoItemOutput>>> Todo(
        [FromQuery] WfTaskPageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfTodoItemOutput>>.Ok(
            await taskService.PageTodoAsync(CurrentUserId, input, cancellationToken));

    /// <summary>我的已办</summary>
    [HttpGet("done")]
    public async Task<Result<PagedList<WfDoneItemOutput>>> Done(
        [FromQuery] WfTaskPageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfDoneItemOutput>>.Ok(
            await taskService.PageDoneAsync(CurrentUserId, input, cancellationToken));

    /// <summary>同意</summary>
    [HttpPost("approve")]
    [OperationLog("审批同意")]
    public async Task<Result<WfEngineResult>> Approve(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.ApproveAsync(input.TaskId, CurrentUserId, input.Comment, cancellationToken));

    /// <summary>拒绝</summary>
    [HttpPost("reject")]
    [OperationLog("审批拒绝")]
    public async Task<Result<WfEngineResult>> Reject(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.RejectAsync(input.TaskId, CurrentUserId, input.Comment, cancellationToken));

    /// <summary>转办</summary>
    [HttpPost("transfer")]
    [OperationLog("审批转办")]
    public async Task<Result<WfEngineResult>> Transfer(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.TransferAsync(
                input.TaskId, CurrentUserId, input.ToUserId, input.Comment, cancellationToken));

    /// <summary>催办</summary>
    [HttpPost("urge")]
    [OperationLog("催办")]
    public async Task<Result<bool>> Urge(WfTaskActionInput input, CancellationToken cancellationToken)
    {
        await taskService.UrgeAsync(input.TaskId, CurrentUserId, cancellationToken);
        return Result<bool>.Ok(true);
    }

    /// <summary>退回</summary>
    [HttpPost("return")]
    [OperationLog("退回")]
    public async Task<Result<WfEngineResult>> Return(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.ReturnAsync(
                input.TaskId, CurrentUserId, input.TargetNodeId, input.Comment, cancellationToken));
}
