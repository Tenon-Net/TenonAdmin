namespace TenonAdmin.Workflow;

/// <summary>高级任务签核动作。与基础办理服务分开，便于消费者独立授权或替换。</summary>
public interface IWfTaskSignService
{
    /// <summary>给当前审批任务追加一名办理人。</summary>
    Task<WfEngineResult> AddSignAsync(
        long taskId,
        long userId,
        long targetUserId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>移除当前任务中尚未表态的一名办理人。</summary>
    Task<WfEngineResult> RemoveSignAsync(
        long taskId,
        long userId,
        long targetUserId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>拿回当前用户最近通过的审批并重入原审批节点。</summary>
    Task<WfEngineResult> TakeBackAsync(
        long taskId,
        long userId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}
