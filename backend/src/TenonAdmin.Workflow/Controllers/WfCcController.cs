using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 抄送列表(设计方案 §十三:抄送≠待办,独立列表 + 已读)。
/// <c>[ActiveSession]</c>——接收人取自令牌,不接受任意 userId。
/// </summary>
[ApiController]
[Route("api/v1/workflow/cc")]
[ActiveSession]
public class WfCcController(
    IWfCcService ccService,
    ICurrentUser currentUser) : ControllerBase
{
    private long CurrentUserId => currentUser.UserId ?? throw new AdminException(ErrorCode.TokenInvalid);

    /// <summary>我的抄送</summary>
    [HttpGet("page")]
    public async Task<Result<PagedList<WfCcItemOutput>>> Page(
        [FromQuery] WfCcPageInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfCcItemOutput>>.Ok(
            await ccService.PageMineAsync(CurrentUserId, input, cancellationToken));

    /// <summary>标已读(仅行主人;已读幂等)</summary>
    [HttpPost("read")]
    public async Task<Result<bool>> MarkRead(
        WfCcMarkReadInput input,
        CancellationToken cancellationToken)
    {
        await ccService.MarkReadAsync(input.Id, CurrentUserId, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
