using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>内置抄送列表:只返回当前用户的 <c>wf_cc</c> 行;标已读必须是行主人。</summary>
public class WfCcService(
    IRepository<WfCc> ccs,
    IRepository<WfInstance> instances,
    IRepository<WfDefinition> definitions,
    IRepository<WfDefinitionVersion> versions) : IWfCcService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<WfCcItemOutput>> PageMineAsync(
        long userId,
        WfCcPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scopedInstanceIds = await ResolveInstanceIdsByDefinitionAsync(input.DefinitionId, cancellationToken);
        if (input.DefinitionId is > 0 && scopedInstanceIds is { Count: 0 })
            return EmptyPage(input);

        var page = await ccs.AsQueryable()
            .Where(c => c.UserId == userId)
            .WhereIF(input.OnlyUnread == true, c => !c.IsRead)
            .WhereIF(scopedInstanceIds is not null, c => scopedInstanceIds!.Contains(c.InstanceId))
            .ToPagedListAsync(input, q => q.OrderBy(c => c.Id, OrderByType.Desc));

        return await MapPageAsync(page, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task MarkReadAsync(
        long ccId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ccId <= 0 || userId <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CcNotFound);

        var row = await ccs.AsQueryable()
            .Where(c => c.Id == ccId && c.UserId == userId)
            .FirstAsync();
        if (row is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CcNotFound,
                new Dictionary<string, object?> { ["ccId"] = ccId });
        if (row.IsRead)
            return;

        row.IsRead = true;
        row.ReadTime = DateTime.Now;
        await ccs.UpdateAsync(row);
    }

    /// <summary>按定义收窄实例 Id;未指定定义返回 null(不过滤)。</summary>
    protected virtual async Task<List<long>?> ResolveInstanceIdsByDefinitionAsync(
        long? definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definitionId is not > 0)
            return null;

        var versionIds = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId.Value && v.Version >= 1)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count == 0)
            return [];

        return await instances.AsQueryable()
            .ClearFilter<IOrgScoped>()
            .Where(i => versionIds.Contains(i.DefinitionVersionId))
            .Select(i => i.Id)
            .ToListAsync();
    }

    protected virtual async Task<PagedList<WfCcItemOutput>> MapPageAsync(
        PagedList<WfCc> page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page.Items.Count == 0)
            return new PagedList<WfCcItemOutput>
            {
                Current = page.Current,
                Size = page.Size,
                Total = page.Total,
                Items = [],
            };

        var instanceIds = page.Items.Select(c => c.InstanceId).Distinct().ToList();
        var instanceMap = (await instances.AsQueryable()
                .ClearFilter<IOrgScoped>()
                .Where(i => instanceIds.Contains(i.Id))
                .ToListAsync())
            .ToDictionary(i => i.Id);

        var versionIds = instanceMap.Values.Select(i => i.DefinitionVersionId).Distinct().ToList();
        var versionRows = versionIds.Count == 0
            ? []
            : await versions.AsQueryable().Where(v => versionIds.Contains(v.Id)).ToListAsync();
        var versionMap = versionRows.ToDictionary(v => v.Id);

        var defIds = versionRows.Select(v => v.DefinitionId).Distinct().ToList();
        var defMap = defIds.Count == 0
            ? new Dictionary<long, WfDefinition>()
            : (await definitions.AsQueryable()
                    .ClearFilter<IOrgScoped>()
                    .Where(d => defIds.Contains(d.Id))
                    .ToListAsync())
                .ToDictionary(d => d.Id);

        var nodeNameByVersion = new Dictionary<long, Dictionary<string, string?>>();
        foreach (var ver in versionRows)
        {
            var model = WfModelJson.Deserialize(ver.ModelJson);
            if (model is null) continue;
            nodeNameByVersion[ver.Id] = WfModelIndex.Build(model).Nodes
                .ToDictionary(n => n.Id, n => string.IsNullOrWhiteSpace(n.Name) ? (string?)null : n.Name);
        }

        var items = page.Items.Select(c =>
        {
            instanceMap.TryGetValue(c.InstanceId, out var inst);
            var defId = 0L;
            var defName = "";
            string? nodeName = null;
            if (inst is not null && versionMap.TryGetValue(inst.DefinitionVersionId, out var ver))
            {
                defId = ver.DefinitionId;
                if (defMap.TryGetValue(ver.DefinitionId, out var def))
                    defName = def.Name;
                if (nodeNameByVersion.TryGetValue(ver.Id, out var names))
                    names.TryGetValue(c.NodeId, out nodeName);
            }

            return new WfCcItemOutput
            {
                Id = c.Id,
                InstanceId = c.InstanceId,
                NodeId = c.NodeId,
                NodeName = nodeName,
                DefinitionId = defId,
                DefinitionName = defName,
                BusinessKey = inst?.BusinessKey,
                InstanceStatus = inst?.Status ?? 0,
                StarterUserId = inst?.StarterUserId ?? 0,
                IsRead = c.IsRead,
                ReadTime = c.ReadTime,
                CreateTime = c.CreateTime,
            };
        }).ToList();

        return new PagedList<WfCcItemOutput>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = items,
        };
    }

    private static PagedList<WfCcItemOutput> EmptyPage(WfCcPageInput input) => new()
    {
        Current = input.Current <= 0 ? 1 : input.Current,
        Size = input.Size <= 0 ? 20 : input.Size,
        Total = 0,
        Items = [],
    };
}
