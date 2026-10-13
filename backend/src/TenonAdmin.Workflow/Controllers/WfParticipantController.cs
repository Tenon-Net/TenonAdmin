using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批选人。登录即可用,不挂用户管理权限码:
/// 转办、委托、加签和发起人自选都要靠它,普通办理人没有 <c>GET:/api/v1/sys/user/page</c>。
/// </summary>
[ApiController]
[Route("api/v1/workflow/participant")]
[ActiveSession]
public class WfParticipantController(IWfParticipantLookupService lookup) : ControllerBase
{
    /// <summary>启用用户的最小资料,供工作流人员选择器使用。</summary>
    [HttpGet("page")]
    public async Task<Result<PagedList<WfParticipantCandidateOutput>>> Page(
        [FromQuery] WfParticipantLookupInput input,
        CancellationToken cancellationToken) =>
        Result<PagedList<WfParticipantCandidateOutput>>.Ok(
            await lookup.PageAsync(input, cancellationToken));
}
