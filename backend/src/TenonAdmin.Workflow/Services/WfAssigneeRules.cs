using System.Text.Json;
using SqlSugar;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批/抄送办理人在发布和发起时共用的校验。
/// 已发布的旧快照绕过了发布校验,发起前必须再查一遍。
/// </summary>
internal static class WfAssigneeRules
{
    /// <summary>整棵树:审批和抄送必须有办理人来源;指定成员不能为空。</summary>
    public static void EnsureConfigured(WfModel model)
    {
        foreach (var node in WfModelIndex.Build(model).Nodes)
            EnsureConfigured(node);
    }

    /// <summary>单个节点。主管、发起人、自选等来源不要求预先填写用户 Id。</summary>
    public static void EnsureConfigured(WfNode node)
    {
        if (node.Type is not (WfNodeType.Approval or WfNodeType.Cc))
            return;

        var provider = node.Props?.Assignee?.Provider;
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid, NodeArgs(node, "assigneeRequired"));
        }

        if (string.Equals(provider, ApproverProviderKeys.User, StringComparison.Ordinal)
            && !PositiveUserIds(node.Props?.Assignee?.Params).Any())
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid, NodeArgs(node, "assigneeUsersRequired"));
        }
    }

    /// <summary>指定成员和空审批人转交目标必须仍存在且启用。</summary>
    public static async Task EnsureEnabledUsersAsync(
        ISqlSugarClient db,
        WfModel model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = WfModelIndex.Build(model);
        var refs = new List<(string? NodeId, long UserId)>();
        foreach (var node in index.Nodes)
        {
            if (node.Props?.NobodyTransferUserId is long transferUserId and > 0)
                refs.Add((node.Id, transferUserId));

            if (node.Type is not (WfNodeType.Approval or WfNodeType.Cc))
                continue;
            if (!string.Equals(node.Props?.Assignee?.Provider, ApproverProviderKeys.User, StringComparison.Ordinal))
                continue;

            foreach (var userId in PositiveUserIds(node.Props?.Assignee?.Params))
                refs.Add((node.Id, userId));
        }

        if (model.NobodyTransferUserId is long modelTransferUserId and > 0)
            refs.Add((null, modelTransferUserId));
        if (refs.Count == 0)
            return;

        var ids = refs.Select(item => item.UserId).Distinct().ToList();
        var enabledIds = await db.Queryable<SysUser>()
            .Where(user => ids.Contains(user.Id) && user.Enabled == true)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        var enabled = enabledIds.ToHashSet();
        foreach (var (nodeId, userId) in refs)
        {
            if (enabled.Contains(userId))
                continue;

            var node = string.IsNullOrEmpty(nodeId) ? null : index.Find(nodeId);
            var args = new Dictionary<string, object?>
            {
                ["reason"] = "assigneeUserUnavailable",
                ["userId"] = userId,
            };
            if (node is not null)
            {
                args["nodeId"] = node.Id;
                if (!string.IsNullOrWhiteSpace(node.Name))
                    args["nodeName"] = node.Name;
            }
            else if (!string.IsNullOrEmpty(nodeId))
            {
                args["nodeId"] = nodeId;
            }

            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid, args);
        }
    }

    private static Dictionary<string, object?> NodeArgs(WfNode node, string reason)
    {
        var args = new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["nodeId"] = node.Id,
        };
        if (!string.IsNullOrWhiteSpace(node.Name))
            args["nodeName"] = node.Name;
        return args;
    }

    private static IEnumerable<long> PositiveUserIds(Dictionary<string, JsonElement>? parameters) =>
        ApproverParamReader.GetLongList(parameters, "userIds", "userId")
            .Where(id => id > 0)
            .Distinct();
}
