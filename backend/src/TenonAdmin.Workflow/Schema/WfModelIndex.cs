namespace TenonAdmin.Workflow;

/// <summary>
/// 模型树索引(纯函数,零依赖 SqlSugar/ASP.NET/DI):一次遍历建两张表——
/// <c>nodeById</c>(含分支臂内的所有节点)与 <c>enclosingBranchById</c>(节点 Id → 包住它的 branch 节点;
/// 主链上的节点为 <c>null</c>)。只服务运行期查找与汇合;发布期校验是另一次独立遍历
/// (<see cref="WfDefinitionService.ValidateModelForPublish"/>),两者不共用一次遍历。
/// </summary>
public sealed class WfModelIndex
{
    private readonly Dictionary<string, WfNode> _nodeById;
    private readonly Dictionary<string, WfNode?> _enclosingBranchById;

    private WfModelIndex(Dictionary<string, WfNode> nodeById, Dictionary<string, WfNode?> enclosingBranchById)
    {
        _nodeById = nodeById;
        _enclosingBranchById = enclosingBranchById;
    }

    /// <summary>
    /// 建索引。重复 <c>Id</c> 由发布期校验拦截,本类不做去重保证;
    /// <see cref="WfNode.Conditions"/> 为 <c>null</c>、臂 <see cref="WfBranchArm.Next"/> 为 <c>null</c>、
    /// <see cref="WfNode.Id"/> 为空串均不抛。
    /// </summary>
    public static WfModelIndex Build(WfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var nodeById = new Dictionary<string, WfNode>(StringComparer.Ordinal);
        var enclosingBranchById = new Dictionary<string, WfNode?>(StringComparer.Ordinal);
        WalkChain(model.Root, enclosingBranch: null, nodeById, enclosingBranchById);
        return new WfModelIndex(nodeById, enclosingBranchById);
    }

    /// <summary>沿 <c>.Next</c> 走一条链;遇到 branch 节点额外对每条臂递归。</summary>
    private static void WalkChain(
        WfNode? node,
        WfNode? enclosingBranch,
        Dictionary<string, WfNode> nodeById,
        Dictionary<string, WfNode?> enclosingBranchById)
    {
        for (var n = node; n is not null; n = n.Next)
        {
            if (!string.IsNullOrEmpty(n.Id))
            {
                nodeById.TryAdd(n.Id, n);
                enclosingBranchById.TryAdd(n.Id, enclosingBranch);
            }

            if (n.Type == WfNodeType.Branch && n.Conditions is not null)
            {
                foreach (var arm in n.Conditions)
                {
                    WalkChain(arm.Next, n, nodeById, enclosingBranchById);
                }
            }
        }
    }

    /// <summary>索引中的全部节点(含分支臂内的所有节点),供发起时快照等需要整棵树的场景复用,不必再写第三次遍历。</summary>
    public IEnumerable<WfNode> Nodes => _nodeById.Values;

    /// <summary>按节点 Id 查找(主链或任意分支臂内)。</summary>
    public WfNode? Find(string nodeId) =>
        !string.IsNullOrEmpty(nodeId) && _nodeById.TryGetValue(nodeId, out var node) ? node : null;

    /// <summary>找包住该节点的 branch 节点;节点在主链上则返回 <c>null</c>。</summary>
    public WfNode? FindEnclosingBranch(string nodeId) =>
        !string.IsNullOrEmpty(nodeId) && _enclosingBranchById.TryGetValue(nodeId, out var branch) ? branch : null;

    /// <summary>
    /// 汇合目标:<paramref name="from"/>.Next 非 <c>null</c> 直接返回;否则沿外层 branch 向上找第一个
    /// <c>Next</c> 非 <c>null</c> 的外层 branch 并返回其 <c>Next</c>;一路到顶仍无 → <c>null</c>(实例完结)。
    /// </summary>
    public WfNode? ResolveMergeTarget(WfNode from)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (from.Next is not null)
            return from.Next;

        var enclosing = FindEnclosingBranch(from.Id);
        while (enclosing is not null)
        {
            if (enclosing.Next is not null)
                return enclosing.Next;
            enclosing = FindEnclosingBranch(enclosing.Id);
        }

        return null;
    }
}
