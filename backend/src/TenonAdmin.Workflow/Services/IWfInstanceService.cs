using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程实例运行态:发起 / 我发起的分页 / 详情(含 FormBinder 挂载点) / 事件流。
/// 方法全 <c>virtual</c>;消费者可继承覆写或前置 <c>TryAdd</c> 整体替换。
/// </summary>
public interface IWfInstanceService
{
    /// <summary>当前用户可发起的已发布定义。</summary>
    Task<IReadOnlyList<WfStartableDefinitionOutput>> ListStartableAsync(
        long userId,
        long? orgId,
        CancellationToken cancellationToken = default);

    /// <summary>当前用户可发起定义的当前发布版本快照。</summary>
    Task<WfStartableDefinitionDetailOutput> GetStartableAsync(
        long definitionId,
        long userId,
        long? orgId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发起实例:解析定义当前已发布版本 → <see cref="IWorkflowFormBinder.ValidateOnStartAsync"/>
    /// (引擎内) → Agenda 推进到首个停顿节点。返回引擎结果。
    /// </summary>
    Task<WfEngineResult> StartAsync(
        WfStartInput input,
        long starterUserId,
        long? starterOrgId,
        CancellationToken cancellationToken = default);

    /// <summary>我发起的实例分页。</summary>
    Task<PagedList<WfInstanceListItemOutput>> PageMineAsync(
        long starterUserId,
        WfInstancePageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 实例详情:元数据 + <c>formComponent</c> 挂载点 + 当前用户待办 + 审批意见时间线。
    /// </summary>
    Task<WfInstanceDetailOutput> GetAsync(
        long instanceId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>实例事件流(<c>wf_history</c>,按时间升序)。</summary>
    Task<IReadOnlyList<WfHistoryItemOutput>> ListHistoryAsync(
        long instanceId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>撤销实例:仅发起人、仅无人已批的 Running 实例可撤销。</summary>
    Task<WfEngineResult> CancelAsync(
        long instanceId,
        long callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重提:仅发起人、仅退回后尚无活跃待办的 Running 实例可重提。从 <c>start</c> 重新走一遍
    /// (连已批过的节点也重新审),复用同一实例行;可选带新的 <paramref name="variablesJson"/> /
    /// <paramref name="selectedUserIdsByNode"/> 覆盖原发起时提交的值。
    /// </summary>
    Task<WfEngineResult> ResubmitAsync(
        long instanceId,
        long callerUserId,
        string? variablesJson,
        IReadOnlyDictionary<string, List<long>>? selectedUserIdsByNode,
        CancellationToken cancellationToken = default);
}
