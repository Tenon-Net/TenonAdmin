namespace TenonAdmin.Workflow;

/// <summary>内置管理员干预:组命令交给引擎。校验、历史和通知都在引擎事务里。</summary>
public class WfAdminInterventionService(IWorkflowEngine engine) : IWfAdminInterventionService
{
    /// <inheritdoc />
    public virtual Task<WfEngineResult> ForceTransferAsync(
        long instanceId,
        long adminUserId,
        long toUserId,
        string? reason,
        string? nodeId = null,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(
            new AdminForceTransferCmd
            {
                InstanceId = instanceId,
                AdminUserId = adminUserId,
                ToUserId = toUserId,
                Reason = reason,
                NodeId = nodeId,
                RequestId = requestId,
            },
            cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> TerminateAsync(
        long instanceId,
        long adminUserId,
        string? reason,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(
            new AdminTerminateCmd
            {
                InstanceId = instanceId,
                AdminUserId = adminUserId,
                Reason = reason,
                RequestId = requestId,
            },
            cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> SkipNodeAsync(
        long instanceId,
        long adminUserId,
        string? reason,
        string? nodeId = null,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(
            new AdminSkipNodeCmd
            {
                InstanceId = instanceId,
                AdminUserId = adminUserId,
                Reason = reason,
                NodeId = nodeId,
                RequestId = requestId,
            },
            cancellationToken);
}
