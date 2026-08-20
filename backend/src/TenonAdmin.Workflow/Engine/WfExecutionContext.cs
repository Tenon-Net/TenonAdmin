using System.Text.Json;
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 一次 <see cref="IWorkflowEngine.ExecuteAsync"/> 的事务内共享状态。
/// 无跨请求内存;崩溃恢复靠表状态,本对象仅存活于单个 Agenda 循环。
/// </summary>
public sealed class WfExecutionContext
{
    public required ISqlSugarClient Db { get; init; }
    public required WfAgenda Agenda { get; init; }
    public required IApproverResolver ApproverResolver { get; init; }
    public required IWorkflowFormBinder FormBinder { get; init; }
    public required WorkflowOptions Options { get; init; }
    public required TimeProvider TimeProvider { get; init; }
    public required IWfConditionEvaluator ConditionEvaluator { get; init; }

    public required WfInstance Instance { get; set; }
    public required WfToken Token { get; set; }
    public required WfModel Model { get; init; }
    public required WfDefinitionVersion DefinitionVersion { get; init; }

    /// <summary>按节点 Id 持久化的发起人自选审批人。</summary>
    public IReadOnlyDictionary<string, List<long>> SelectedUserIdsByNode { get; init; } =
        new Dictionary<string, List<long>>(StringComparer.Ordinal);

    /// <summary>发起人主属机构。</summary>
    public long? StarterOrgId { get; init; }

    /// <summary>
    /// 发起时按 <c>level</c> 快照的连续多级主管链;<c>null</c>=无快照(老实例或模型无 multiLeader 节点)。
    /// 命中 level 的空数组表示快照过但该级链为空。透传给 <see cref="ApproverResolveContext.LeaderChainByLevel"/>。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<long>>? LeaderChainByLevel { get; init; }

    /// <summary>当前 Agenda 步关联的节点(Enter/Leave 时更新)。</summary>
    public WfNode? CurrentNode { get; set; }

    // ── 结果累加(事务提交后读) ──

    public long? CreatedTaskId { get; set; }
    public List<long> NewAssigneeUserIds { get; } = [];
    public List<long> NewCcUserIds { get; } = [];

    private WfModelIndex? _modelIndex;

    /// <summary>模型树索引:懒建一次并缓存;ctx 是单事务对象,树在整个 Agenda 循环内不变,无失效问题。</summary>
    private WfModelIndex ModelIndex => _modelIndex ??= WfModelIndex.Build(Model);

    /// <summary>按节点 Id 查找(含分支臂内的所有节点)。</summary>
    public WfNode? FindNode(string nodeId) => ModelIndex.Find(nodeId);

    /// <summary>
    /// 汇合目标:节点 <c>Next</c> 非 null 直接用;否则沿外层 branch 向上找第一个 <c>Next</c> 非 null 的
    /// 外层 branch 并返回其 <c>Next</c>;一路到顶仍无 → null(实例完结)。
    /// </summary>
    public WfNode? ResolveMergeTarget(WfNode from) => ModelIndex.ResolveMergeTarget(from);

    public IReadOnlyList<long>? GetSelectedUserIds(string nodeId) =>
        SelectedUserIdsByNode.TryGetValue(nodeId, out var ids) ? ids : null;

    /// <summary>append-only 写一条历史事件。</summary>
    public async Task AppendHistoryAsync(
        WfHistoryEventType eventType,
        string? nodeId = null,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = new WfHistory
        {
            InstanceId = Instance.Id,
            EventType = eventType,
            NodeId = nodeId,
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, WfModelJson.Options),
        };
        await Db.Insertable(row).ExecuteCommandAsync();
    }

    public WfEngineResult ToResult() => new()
    {
        InstanceId = Instance.Id,
        InstanceStatus = Instance.Status,
        CreatedTaskId = CreatedTaskId,
        NewAssigneeUserIds = NewAssigneeUserIds,
        NewCcUserIds = NewCcUserIds,
    };
}
