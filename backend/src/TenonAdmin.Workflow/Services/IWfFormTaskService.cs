namespace TenonAdmin.Workflow;

/// <summary>支持提交表单变量的审批任务扩展。</summary>
public interface IWfFormTaskService
{
    /// <summary>同意待办并提交表单变量。</summary>
    Task<WfEngineResult> ApproveWithVariablesAsync(
        long taskId,
        long userId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default,
        string? variablesJson = null);

    /// <summary>拒绝待办并提交表单变量。</summary>
    Task<WfEngineResult> RejectWithVariablesAsync(
        long taskId,
        long userId,
        string? comment = null,
        string? requestId = null,
        CancellationToken cancellationToken = default,
        string? variablesJson = null);
}
