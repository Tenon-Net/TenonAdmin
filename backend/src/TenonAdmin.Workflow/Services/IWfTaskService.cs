using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批任务服务(M1:待办/已办列表 + 同意/拒绝/转办)。Controller 调本接口 → 引擎 Cmd;
/// 方法全 <c>virtual</c>,消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
public interface IWfTaskService
{
    /// <summary>我的待办分页(<c>wf_task_actor</c> Pending Approver)。</summary>
    Task<PagedList<WfTodoItemOutput>> PageTodoAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>我的已办分页(<c>wf_his_task</c> 本用户办理记录)。</summary>
    Task<PagedList<WfDoneItemOutput>> PageDoneAsync(
        long userId,
        WfTaskPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>同意待办并推进(或签一票 / 顺序下一位 / 会签计票)。</summary>
    Task<WfEngineResult> ApproveAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>拒绝待办;M1 一律终止实例(节点 onReject=toNode 属 M2)。</summary>
    Task<WfEngineResult> RejectAsync(
        long taskId,
        long userId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>任务级转办:把待办交给 <paramref name="toUserId"/>,不推进 token。</summary>
    Task<WfEngineResult> TransferAsync(
        long taskId,
        long userId,
        long toUserId,
        string? comment = null,
        CancellationToken cancellationToken = default);
}
