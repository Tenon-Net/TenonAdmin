namespace TenonAdmin.Workflow;

/// <summary>
/// 业务表单挂载点(SPI):发起校验 / 完结回写由消费者实现;
/// 包内默认 <see cref="NoOpWorkflowFormBinder"/>,经 <c>TryAdd</c> 注册,可整体替换。
/// </summary>
public interface IWorkflowFormBinder
{
    /// <summary>发起前校验业务单据(失败抛 <see cref="TenonAdmin.Core.AdminException"/>)。</summary>
    Task ValidateOnStartAsync(WfFormBindContext context, CancellationToken cancellationToken = default);

    /// <summary>实例完结后回写业务状态(通过/拒绝等)。</summary>
    Task OnInstanceCompletedAsync(WfFormBindContext context, CancellationToken cancellationToken = default);
}

/// <summary>表单挂载点上下文。</summary>
public sealed class WfFormBindContext
{
    public required long InstanceId { get; init; }
    public required long DefinitionVersionId { get; init; }
    public string? BusinessKey { get; init; }
    public string? VariablesJson { get; init; }
    public required WfInstanceStatus Status { get; init; }
    public required long StarterUserId { get; init; }
}

/// <summary>默认空操作挂载点。</summary>
public class NoOpWorkflowFormBinder : IWorkflowFormBinder
{
    /// <inheritdoc />
    public virtual Task ValidateOnStartAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task OnInstanceCompletedAsync(WfFormBindContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
