namespace TenonAdmin.Workflow;

/// <summary>默认高级签核服务；独立于待办查询与基础办理服务。</summary>
public class WfTaskSignService(IWorkflowEngine engine) : IWfTaskSignService
{
    /// <inheritdoc />
    public virtual Task<WfEngineResult> AddSignAsync(
        long taskId,
        long userId,
        long targetUserId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(new AddSignTaskCmd
        {
            TaskId = taskId,
            UserId = userId,
            TargetUserId = targetUserId,
            Comment = comment,
            RequestId = requestId,
        }, cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> RemoveSignAsync(
        long taskId,
        long userId,
        long targetUserId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(new RemoveSignTaskCmd
        {
            TaskId = taskId,
            UserId = userId,
            TargetUserId = targetUserId,
            Comment = comment,
            RequestId = requestId,
        }, cancellationToken);

    /// <inheritdoc />
    public virtual Task<WfEngineResult> TakeBackAsync(
        long taskId,
        long userId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default) =>
        engine.ExecuteAsync(new TakeBackTaskCmd
        {
            TaskId = taskId,
            UserId = userId,
            Comment = comment,
            RequestId = requestId,
        }, cancellationToken);
}
