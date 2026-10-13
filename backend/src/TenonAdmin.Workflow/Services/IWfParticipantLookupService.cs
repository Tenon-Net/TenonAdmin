using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批动作里的人员选择。独立于系统用户管理接口:
/// 待办办理人没有用户分页权限时仍要能选人,且只返回姓名和账号。
/// </summary>
public interface IWfParticipantLookupService
{
    /// <summary>分页列出仍启用的用户。单页最多 50 条。</summary>
    Task<PagedList<WfParticipantCandidateOutput>> PageAsync(
        WfParticipantLookupInput input,
        CancellationToken cancellationToken = default);
}
