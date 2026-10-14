namespace TenonAdmin.Workflow;

/// <summary>
/// 运行中但没有待办的停顿:空审批人阻断,或退回到发起节点等待修改。
/// </summary>
internal static class WfRuntimePause
{
    public readonly record struct Snapshot(
        IReadOnlyList<string> CurrentNodeIds,
        IReadOnlyList<string> BlockedNodeIds,
        bool ReturnedToStarter,
        bool HasPendingTask);

    public static Snapshot Describe(
        WfModel? model,
        IEnumerable<WfToken> tokens,
        IEnumerable<(long TokenId, string NodeId)> tasks)
    {
        var taskList = tasks.ToList();
        var taskTokenIds = taskList.Select(task => task.TokenId).ToHashSet();
        var current = taskList
            .Select(task => task.NodeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var blocked = new List<string>();
        var returned = false;
        var index = model is null ? null : WfModelIndex.Build(model);

        foreach (var token in tokens)
        {
            if (token.Status != WfTokenStatus.Active || taskTokenIds.Contains(token.Id))
                continue;
            if (string.IsNullOrWhiteSpace(token.NodeId))
                continue;
            if (!current.Contains(token.NodeId, StringComparer.Ordinal))
                current.Add(token.NodeId);

            var type = index?.Find(token.NodeId)?.Type;
            if (type == WfNodeType.Start)
                returned = true;
            else
                blocked.Add(token.NodeId);
        }

        return new Snapshot(
            current,
            blocked.Distinct(StringComparer.Ordinal).ToList(),
            returned,
            taskList.Count > 0);
    }

    /// <summary>审批节点的空人策略最终是阻断(含未配置转交人的转交)。</summary>
    public static bool MayBlockWithoutApprover(WfModel model)
    {
        foreach (var node in WfModelIndex.Build(model).Nodes)
        {
            if (node.Type != WfNodeType.Approval)
                continue;
            var action = node.Props?.Nobody ?? model.Nobody ?? WfNobodyAction.Block;
            if (action == WfNobodyAction.Block)
                return true;
            if (action == WfNobodyAction.Transfer)
            {
                var userId = node.Props?.NobodyTransferUserId ?? model.NobodyTransferUserId ?? 0;
                if (userId <= 0)
                    return true;
            }
        }

        return false;
    }
}
