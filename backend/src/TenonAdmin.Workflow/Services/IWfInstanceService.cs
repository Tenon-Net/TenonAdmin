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
    /// 管理员监控分页:机构数据范围照旧,再叠发起人 / 办理人 / 抄送人业务过滤。
    /// 办理人 = 当前 Pending actor <b>或</b> <c>wf_his_task</c> 行。
    /// </summary>
    Task<PagedList<WfInstanceListItemOutput>> PageMonitorAsync(
        WfInstanceMonitorPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 实例详情:元数据 + <c>formComponent</c> 挂载点 + 当前用户待办 + 审批意见时间线
    /// + 实例版本快照与最后一次访问收敛的回放节点集。
    /// 抄送接收人打开详情时,其本实例未读 <c>wf_cc</c> 行会被标已读。
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
    /// <remarks>
    /// <c>requestId</c> 是幂等请求键(可空):同一次用户动作的重试携带同一个值。归一化与校验见
    /// <see cref="WfWriteCmd.RequestId"/>。写在 <c>remarks</c> 而非 <c>param</c>:本接口其余参数均无
    /// <c>param</c> 标记,只给一个参数加会触发 CS1573(“有些有、有些没有”)。
    /// </remarks>
    Task<WfEngineResult> CancelAsync(
        long instanceId,
        long callerUserId,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重提:仅发起人、仅退回后尚无活跃待办的 Running 实例可重提。从 <c>start</c> 重新走一遍
    /// (连已批过的节点也重新审),复用同一实例行;可选带新的 <paramref name="variablesJson"/> /
    /// <paramref name="selectedUserIdsByNode"/> 覆盖原发起时提交的值。
    /// </summary>
    /// <remarks>
    /// <c>requestId</c> 是幂等请求键(可空):同一次用户动作的重试携带同一个值。归一化与校验见
    /// <see cref="WfWriteCmd.RequestId"/>。写在 <c>remarks</c> 而非 <c>param</c>:本接口其余参数均无
    /// <c>param</c> 标记,只给一个参数加会触发 CS1573(“有些有、有些没有”)。
    /// </remarks>
    Task<WfEngineResult> ResubmitAsync(
        long instanceId,
        long callerUserId,
        string? variablesJson,
        IReadOnlyDictionary<string, List<long>>? selectedUserIdsByNode,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}
