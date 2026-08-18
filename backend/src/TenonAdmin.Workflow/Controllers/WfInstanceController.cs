using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程实例运行态(设计方案 §七 / §6.3 审批中心):发起 / 我发起的 / 详情 / 事件流。
/// 全部 <c>[ActiveSession]</c>——登录用户操作自己的单据,userId 取自令牌。
/// </summary>
[ApiController]
[Route("api/v1/workflow/instance")]
[ActiveSession]
public class WfInstanceController(
    IWfInstanceService instanceService,
    ICurrentUser currentUser) : ControllerBase
{
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>当前用户可发起的已发布定义</summary>
    [HttpGet("startable")]
    public async Task<Result<IReadOnlyList<WfStartableDefinitionOutput>>> Startable(
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<WfStartableDefinitionOutput>>.Ok(
            await instanceService.ListStartableAsync(
                CurrentUserId, currentUser.OrgId, cancellationToken));

    /// <summary>当前用户可发起定义的当前发布版本快照</summary>
    [HttpGet("startable/{id:long}")]
    public async Task<Result<WfStartableDefinitionDetailOutput>> StartableDetail(
        long id,
        CancellationToken cancellationToken) =>
        Result<WfStartableDefinitionDetailOutput>.Ok(
            await instanceService.GetStartableAsync(
                id, CurrentUserId, currentUser.OrgId, cancellationToken));

    /// <summary>发起流程(校验 FormBinder → 引擎推进到首停顿节点)</summary>
    [HttpPost("start")]
    [OperationLog("发起流程")]
    public async Task<Result<WfEngineResult>> Start(
        WfStartInput input,
        CancellationToken cancellationToken) =>
        Result<WfEngineResult>.Ok(
            await instanceService.StartAsync(input, CurrentUserId, currentUser.OrgId, cancellationToken));

    /// <summary>我发起的实例分页</summary>
    [HttpGet("page")]
    public async Task<Result<PagedList<WfInstanceListItemOutput>>> Page(
        [FromQuery] WfInstancePageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfInstanceListItemOutput>>.Ok(
            await instanceService.PageMineAsync(CurrentUserId, input, cancellationToken));

    /// <summary>事件流(按时间升序)</summary>
    [HttpGet("history/{id:long}")]
    public async Task<Result<IReadOnlyList<WfHistoryItemOutput>>> History(
        long id,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<WfHistoryItemOutput>>.Ok(
            await instanceService.ListHistoryAsync(id, CurrentUserId, cancellationToken));

    /// <summary>实例详情(含 formComponent 挂载点 + 意见时间线 + 我的待办)</summary>
    [HttpGet("{id:long}")]
    public async Task<Result<WfInstanceDetailOutput>> Get(
        long id,
        CancellationToken cancellationToken) =>
        Result<WfInstanceDetailOutput>.Ok(
            await instanceService.GetAsync(id, CurrentUserId, cancellationToken));
}
