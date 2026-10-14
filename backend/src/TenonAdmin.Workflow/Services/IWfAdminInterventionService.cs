namespace TenonAdmin.Workflow;

/// <summary>
/// 流程监控上的管理员干预。独立于 <see cref="IWfInstanceService"/>,
/// 消费者替换实例服务时不会把这三条端点一起换掉;要换干预行为就前置注册本接口。
/// </summary>
public interface IWfAdminInterventionService
{
    /// <summary>把运行中实例的当前节点强制转给启用用户。阻断节点没有待办时补建待办。</summary>
    Task<WfEngineResult> ForceTransferAsync(
        long instanceId,
        long adminUserId,
        long toUserId,
        string? reason,
        string? nodeId = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>终止运行中的实例。原因必填,状态为已终止。</summary>
    Task<WfEngineResult> TerminateAsync(
        long instanceId,
        long adminUserId,
        string? reason,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>跳过没有待办的阻断审批节点。</summary>
    Task<WfEngineResult> SkipNodeAsync(
        long instanceId,
        long adminUserId,
        string? reason,
        string? nodeId = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}
