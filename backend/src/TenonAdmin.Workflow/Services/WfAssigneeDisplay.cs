namespace TenonAdmin.Workflow;

/// <summary>从模型里收集「指定成员」用户，并按原顺序换成显示名。不回写模型，也不公开用户 Id。</summary>
internal static class WfAssigneeDisplay
{
    public static IReadOnlyList<long> CollectUserIds(WfModel? model)
    {
        if (model?.Root is null)
            return [];

        var ids = new List<long>();
        foreach (var node in WfModelIndex.Build(model).Nodes)
        {
            var assignee = node.Props?.Assignee;
            if (!IsUserProvider(assignee))
                continue;
            ids.AddRange(ApproverParamReader.GetLongList(assignee!.Params, "userIds", "userId"));
        }

        return ids;
    }

    public static IReadOnlyList<string>? NamesFor(
        WfAssignee? assignee,
        IReadOnlyDictionary<long, string>? userNames)
    {
        if (userNames is null || !IsUserProvider(assignee))
            return null;

        var seen = new HashSet<long>();
        var names = new List<string>();
        foreach (var id in ApproverParamReader.GetLongList(assignee!.Params, "userIds", "userId"))
        {
            if (id <= 0 || !seen.Add(id))
                continue;
            if (userNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names.Count == 0 ? null : names;
    }

    private static bool IsUserProvider(WfAssignee? assignee) =>
        assignee is not null
        && string.Equals(assignee.Provider, "user", StringComparison.OrdinalIgnoreCase);
}
