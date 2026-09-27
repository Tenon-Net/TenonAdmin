using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批任务端点(设计方案 §七):待办 / 已办 + 基础办理动词与独立加签 / 减签端点。
/// 全部 <c>[ActiveSession]</c>——办理人取自令牌,不接受任意 userId。
/// </summary>
[ApiController]
[Route("api/v1/workflow/task")]
[ActiveSession]
public class WfTaskController(
    IWfTaskService taskService,
    IWfTaskSignService signService,
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
        CancellationToken cancellationToken)
    {
        if (input.VariablesJson is not null && taskService is not IWfFormTaskService)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.FormRuntimeUnavailable);

        var result = taskService is IWfFormTaskService formTaskService
            ? await formTaskService.ApproveWithVariablesAsync(
                input.TaskId, CurrentUserId, input.Comment, input.RequestId, cancellationToken, input.VariablesJson)
            : await taskService.ApproveAsync(
                input.TaskId, CurrentUserId, input.Comment, input.RequestId, cancellationToken);
        return Result<WfEngineResult>.Ok(result);
    }

    /// <summary>拒绝</summary>
    [HttpPost("reject")]
    [OperationLog("审批拒绝")]
    public async Task<Result<WfEngineResult>> Reject(
        WfTaskActionInput input,
        CancellationToken cancellationToken)
    {
        if (input.VariablesJson is not null && taskService is not IWfFormTaskService)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.FormRuntimeUnavailable);

        var result = taskService is IWfFormTaskService formTaskService
            ? await formTaskService.RejectWithVariablesAsync(
                input.TaskId, CurrentUserId, input.Comment, input.RequestId, cancellationToken, input.VariablesJson)
            : await taskService.RejectAsync(
                input.TaskId, CurrentUserId, input.Comment, input.RequestId, cancellationToken);
        return Result<WfEngineResult>.Ok(result);
    }

    /// <summary>转办</summary>
    [HttpPost("transfer")]
    [OperationLog("审批转办")]
    public async Task<Result<WfEngineResult>> Transfer(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.TransferAsync(
                input.TaskId, CurrentUserId, input.ToUserId, input.Comment, input.RequestId, cancellationToken));

    /// <summary>委托(一次性:把当前待办指给别人代办)</summary>
    [HttpPost("delegate")]
    [OperationLog("委托")]
    public async Task<Result<WfEngineResult>> Delegate(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await taskService.DelegateAsync(
                input.TaskId, CurrentUserId, input.ToUserId, input.Comment, input.RequestId, cancellationToken));

    /// <summary>催办</summary>
    [HttpPost("urge")]
    [OperationLog("催办")]
    public async Task<Result<bool>> Urge(WfTaskActionInput input, CancellationToken cancellationToken)
    {
        // input.RequestId 刻意不透传:催办不进引擎(只追加事件 + 推通知),可重复催办、不做幂等。
        // 透传一个没人读的值只会暗示它有幂等语义(台账 ## 语义契约「催办」)。
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
                input.TaskId, CurrentUserId, input.TargetNodeId, input.Comment, input.RequestId, cancellationToken));

    /// <summary>加签(追加一名当前节点办理人)</summary>
    [HttpPost("add-sign")]
    [RolePermission]
    [OperationLog("审批加签")]
    public async Task<Result<WfEngineResult>> AddSign(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await signService.AddSignAsync(
                input.TaskId, CurrentUserId, input.ToUserId, input.Comment, input.RequestId, cancellationToken));

    /// <summary>减签(移除一名尚未表态的当前节点办理人)</summary>
    [HttpPost("remove-sign")]
    [RolePermission]
    [OperationLog("审批减签")]
    public async Task<Result<WfEngineResult>> RemoveSign(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await signService.RemoveSignAsync(
                input.TaskId, CurrentUserId, input.ToUserId, input.Comment, input.RequestId, cancellationToken));

    /// <summary>拿回自己最近通过的审批并重入原节点</summary>
    [HttpPost("take-back")]
    [RolePermission]
    [OperationLog("审批拿回")]
    public async Task<Result<WfEngineResult>> TakeBack(
        WfTaskActionInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await signService.TakeBackAsync(
                input.TaskId, CurrentUserId, input.Comment, input.RequestId, cancellationToken));
}
