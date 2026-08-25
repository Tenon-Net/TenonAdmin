using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置流程定义服务:草稿落 <see cref="DraftVersion"/>(=0),发布即不可变快照。
/// </summary>
public class WfDefinitionService(
    IRepository<WfDefinition> definitions,
    IRepository<WfDefinitionVersion> versions,
    IEnumerable<IApproverProvider> approverProviders,
    TimeProvider timeProvider,
    ICurrentUser? currentUser = null) : IWfDefinitionService
{
    /// <summary>未发布工作副本版本号;已发布快照从 1 起递增。</summary>
    public const int DraftVersion = 0;

    /// <inheritdoc />
    public virtual async Task<PagedList<WfDefinition>> PageAsync(
        WfDefinitionPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await definitions.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(input.Name), d => d.Name.Contains(input.Name!))
            .WhereIF(!string.IsNullOrWhiteSpace(input.GroupName), d => d.GroupName == input.GroupName)
            .WhereIF(input.Status.HasValue, d => d.Status == input.Status!.Value)
            .ToPagedListAsync(input, q => q.OrderBy(d => d.Id, OrderByType.Desc));
    }

    /// <inheritdoc />
    public virtual async Task<WfDefinitionDetailOutput> GetAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        var draft = await GetOrCreateDraftAsync(def.Id, cancellationToken);
        return MapDetail(def, draft);
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(
        WfDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = NormalizeName(input.Name);
        ValidateMetadata(input.Icon, input.GroupName);
        var modelJson = SerializeModel(input.Model ?? CreateDefaultModel());

        var db = definitions.Db;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = new WfDefinition
            {
                Name = name,
                Icon = input.Icon,
                GroupName = input.GroupName,
                Status = WfDefinitionStatus.Draft,
                CurrentVersion = 0,
            };
            await db.Insertable(def).ExecuteCommandAsync();

            var draft = new WfDefinitionVersion
            {
                DefinitionId = def.Id,
                Version = DraftVersion,
                ModelJson = modelJson,
                FormSchema = null,
                PublishTime = null,
                PublishUserId = null,
            };
            await db.Insertable(draft).ExecuteCommandAsync();
            return def.Id;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        return tran.Data;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(
        WfDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

        var name = NormalizeName(input.Name);
        ValidateMetadata(input.Icon, input.GroupName);
        var modelJson = SerializeModel(input.Model ?? CreateDefaultModel());

        var db = definitions.Db;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = await db.Queryable<WfDefinition>().InSingleAsync(input.Id)
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

            def.Name = name;
            def.Icon = input.Icon;
            def.GroupName = input.GroupName;
            await db.Updateable(def)
                .UpdateColumns(d => new { d.Name, d.Icon, d.GroupName, d.UpdateTime, d.UpdateUserId })
                .ExecuteCommandAsync();

            var draft = await db.Queryable<WfDefinitionVersion>()
                .Where(v => v.DefinitionId == def.Id && v.Version == DraftVersion)
                .FirstAsync();
            if (draft is null)
            {
                draft = new WfDefinitionVersion
                {
                    DefinitionId = def.Id,
                    Version = DraftVersion,
                    ModelJson = modelJson,
                };
                await db.Insertable(draft).ExecuteCommandAsync();
            }
            else
            {
                draft.ModelJson = modelJson;
                await db.Updateable(draft)
                    .UpdateColumns(v => new { v.ModelJson, v.UpdateTime, v.UpdateUserId })
                    .ExecuteCommandAsync();
            }
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
    }

    /// <inheritdoc />
    public virtual async Task<int> PublishAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

        var db = definitions.Db;
        var publishUserId = currentUser?.UserId;
        var now = timeProvider.GetLocalNow().DateTime;

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = await db.Queryable<WfDefinition>().InSingleAsync(id)
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

            var draft = await db.Queryable<WfDefinitionVersion>()
                .Where(v => v.DefinitionId == def.Id && v.Version == DraftVersion)
                .FirstAsync();
            if (draft is null || string.IsNullOrWhiteSpace(draft.ModelJson))
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?> { ["reason"] = "draftEmpty" });

            var model = WfModelJson.Deserialize(draft.ModelJson)
                        ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);
            ValidateModelForPublish(model);

            var nextVersion = def.CurrentVersion + 1;
            var snapshot = new WfDefinitionVersion
            {
                DefinitionId = def.Id,
                Version = nextVersion,
                ModelJson = WfModelJson.Serialize(model),
                FormSchema = model.FormSchema is null ? null : System.Text.Json.JsonSerializer.Serialize(model.FormSchema, WfModelJson.Options),
                PublishTime = now,
                PublishUserId = publishUserId,
            };
            await db.Insertable(snapshot).ExecuteCommandAsync();

            def.CurrentVersion = nextVersion;
            def.Status = WfDefinitionStatus.Published;
            await db.Updateable(def)
                .UpdateColumns(d => new { d.CurrentVersion, d.Status, d.UpdateTime, d.UpdateUserId })
                .ExecuteCommandAsync();

            return nextVersion;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        return tran.Data;
    }

    /// <inheritdoc />
    public virtual async Task DisableAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        if (def.Status == WfDefinitionStatus.Disabled)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionStatusConflict,
                new Dictionary<string, object?> { ["status"] = "disabled" });

        def.Status = WfDefinitionStatus.Disabled;
        await definitions.UpdateAsync(def);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        var db = definitions.Db;

        var versionIds = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.DefinitionId == def.Id)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count > 0)
        {
            // 跨机构看在途单:本机构看不见的运行中单据,也不能把定义抽掉。
            var hasRunning = await db.Queryable<WfInstance>()
                .ClearFilter<IOrgScoped>()
                .Where(i => versionIds.Contains(i.DefinitionVersionId) && i.Status == WfInstanceStatus.Running)
                .AnyAsync();
            if (hasRunning)
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionHasRunningInstances);
        }

        await definitions.DeleteAsync(def.Id);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<WfDefinitionVersionOutput>> ListVersionsAsync(
        long definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RequireDefinitionAsync(definitionId);

        var list = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId && v.Version >= 1)
            .OrderBy(v => v.Version, OrderByType.Desc)
            .ToListAsync();

        return list.Select(v => new WfDefinitionVersionOutput
        {
            Id = v.Id,
            DefinitionId = v.DefinitionId,
            Version = v.Version,
            ModelJson = v.ModelJson,
            FormSchema = v.FormSchema,
            PublishTime = v.PublishTime,
            PublishUserId = v.PublishUserId,
        }).ToList();
    }

    /// <summary>
    /// 校验可发布模型(树语义,M2a):根为 start;节点类型限 start|approval|cc|branch
    /// (Parallel/Webhook 仍被拒,M3 开放);节点 Id 跨整棵树(含分支臂内)非空且唯一;
    /// branch 节点的臂配置合法(见 <see cref="ValidateBranch"/>);跳转目标引用完整
    /// (见 <see cref="ValidateNodeReferences"/>)。
    /// </summary>
    protected virtual void ValidateModelForPublish(WfModel model)
    {
        if (model.Root.Type != WfNodeType.Start)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "rootNotStart" });
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var providerKeys = approverProviders.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        ValidateLength(model.FormComponent, 256, "formComponent");
        ValidateChain(model.Root, seen, providerKeys);
        ValidateNodeReferences(model);
    }

    /// <summary>
    /// 跳转目标与超时目标的引用完整性(M2b):<c>onReject=toNode</c> ⇒ <see cref="WfNodeProps.RejectToNodeId"/>
    /// 非空且指向全树存在的节点;<c>returnPolicy=node</c> ⇒ <see cref="WfNodeProps.ReturnToNodeId"/> 同理;
    /// <c>timeout.action=transfer</c>(且 <c>hours &gt; 0</c>)⇒ <see cref="WfTimeout.TransferUserId"/> 为正。
    /// <para>必须独立于 <see cref="ValidateChain"/> 单独走一趟:跳转目标可能在当前遍历位置<b>之后</b>、
    /// 或在另一条分支臂上,只查已 <c>seen</c> 的集合会把合法的前向/跨臂引用误判成非法。这里复用
    /// <see cref="WfModelIndex"/> 的整树索引,不再手写第三次遍历。</para>
    /// <para>不校验会漏出「拒绝动作永久不可用」的定义:运行到该节点点拒绝才抛
    /// <see cref="WorkflowErrorCode.ModelInvalid"/> 并回滚,而那个码的语义(根非 start / 缺节点)
    /// 完全看不出是配置问题。</para>
    /// </summary>
    protected virtual void ValidateNodeReferences(WfModel model)
    {
        var index = WfModelIndex.Build(model);
        foreach (var node in index.Nodes)
        {
            if (node.Props?.OnReject == WfRejectAction.ToNode)
            {
                RequireNodeReference(index, node, node.Props.RejectToNodeId, "rejectToNodeId");
            }

            if (node.Props?.ReturnPolicy == WfReturnPolicy.Node)
            {
                RequireNodeReference(index, node, node.Props.ReturnToNodeId, "returnToNodeId");
            }

            // 超时自动转办缺目标是「永久失败」形态:待办到期后每一拍都失败一次,直到有人手工办掉。
            // 运行期只能计数 + 日志,发布期拒了才是根治。复用 48002 + reason,零新增错误码。
            if (node.Props?.Timeout is { Hours: > 0, Action: WfTimeoutAction.Transfer } timeout
                && timeout.TransferUserId is not > 0)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "timeoutTransferUserIdInvalid",
                        ["nodeId"] = node.Id,
                    });
            }
        }
    }

    /// <summary>目标节点 Id 非空且能在整树索引里解析;否则抛 <c>&lt;field&gt;Invalid</c>。</summary>
    protected virtual void RequireNodeReference(
        WfModelIndex index,
        WfNode node,
        string? targetNodeId,
        string field)
    {
        ValidateLength(targetNodeId, 64, field);
        if (string.IsNullOrWhiteSpace(targetNodeId) || index.Find(targetNodeId) is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?>
                {
                    ["reason"] = $"{field}Invalid",
                    ["nodeId"] = node.Id,
                    ["targetNodeId"] = targetNodeId,
                });
        }
    }

    /// <summary>沿 <c>.Next</c> 走一条链,逐节点校验;<paramref name="seen"/> 跨整棵树共享,
    /// 遇到 branch 节点额外递归校验它的每条臂。</summary>
    protected virtual void ValidateChain(WfNode? node, HashSet<string> seen, HashSet<string> providerKeys)
    {
        for (var n = node; n is not null; n = n.Next)
        {
            ValidateNode(n, seen, providerKeys);

            if (n.Type == WfNodeType.Branch)
            {
                ValidateBranch(n, seen, providerKeys);
            }
        }
    }

    /// <summary>
    /// 单节点公共校验:类型白名单、非 branch 节点不得携带 <see cref="WfNode.Conditions"/>、
    /// Id 非空唯一、长度、审批人 Provider 已注册。
    /// </summary>
    protected virtual void ValidateNode(WfNode node, HashSet<string> seen, HashSet<string> providerKeys)
    {
        if (node.Type is not (WfNodeType.Start or WfNodeType.Approval or WfNodeType.Cc or WfNodeType.Branch))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.NodeTypeUnsupported,
                new Dictionary<string, object?> { ["type"] = node.Type.ToString() });
        }

        if (node.Type != WfNodeType.Branch && node.Conditions is { Count: > 0 })
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "conditionsOnNonBranch", ["nodeId"] = node.Id });
        }

        if (string.IsNullOrWhiteSpace(node.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "emptyNodeId" });
        }

        ValidateLength(node.Id, 64, "nodeId");
        ValidateLength(node.Name, 128, "nodeName");

        if (!seen.Add(node.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "duplicateNodeId", ["nodeId"] = node.Id });
        }

        var provider = node.Props?.Assignee?.Provider;
        if (node.Type is WfNodeType.Approval or WfNodeType.Cc)
        {
            if (!string.IsNullOrWhiteSpace(provider))
            {
                ValidateLength(provider, 64, "provider");
                if (!providerKeys.Contains(provider))
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.ProviderNotRegistered,
                        new Dictionary<string, object?> { ["provider"] = provider, ["nodeId"] = node.Id });
                }
            }
        }
    }

    /// <summary>
    /// branch 专属校验:臂非空(<c>branchNoArms</c>)、臂 Id 非空(<c>emptyArmId</c>)且本 branch 内唯一
    /// (<c>duplicateArmId</c>)、非默认臂须有 <see cref="WfBranchArm.Expr"/>(<c>branchArmWithoutExpr</c>)、
    /// 恰好一条默认臂(<c>branchDefaultArmCount</c>);再递归校验每条臂的子链。
    /// </summary>
    protected virtual void ValidateBranch(WfNode branch, HashSet<string> seen, HashSet<string> providerKeys)
    {
        if (branch.Conditions is not { Count: > 0 } arms)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "branchNoArms", ["nodeId"] = branch.Id });
        }

        var armIds = new HashSet<string>(StringComparer.Ordinal);
        var defaultCount = 0;
        foreach (var arm in arms)
        {
            if (string.IsNullOrWhiteSpace(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?> { ["reason"] = "emptyArmId", ["nodeId"] = branch.Id });
            }

            ValidateLength(arm.Id, 64, "armId");
            ValidateLength(arm.Name, 128, "armName");

            if (!armIds.Add(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "duplicateArmId", ["nodeId"] = branch.Id, ["armId"] = arm.Id,
                    });
            }

            if (arm.IsDefault)
            {
                defaultCount++;
            }
            else if (arm.Expr is null)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "branchArmWithoutExpr", ["nodeId"] = branch.Id, ["armId"] = arm.Id,
                    });
            }

            ValidateChain(arm.Next, seen, providerKeys);
        }

        if (defaultCount != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "branchDefaultArmCount", ["nodeId"] = branch.Id });
        }
    }

    /// <summary>按 Id 取定义;不存在抛 <see cref="WorkflowErrorCode.DefinitionNotFound"/>。</summary>
    protected virtual async Task<WfDefinition> RequireDefinitionAsync(long id)
    {
        if (id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        var def = await definitions.GetByIdAsync(id);
        if (def is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        return def;
    }

    /// <summary>取草稿版本 0;缺失则插入默认模型草稿。</summary>
    protected virtual async Task<WfDefinitionVersion> GetOrCreateDraftAsync(
        long definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId && v.Version == DraftVersion)
            .FirstAsync();
        if (draft is not null)
            return draft;

        draft = new WfDefinitionVersion
        {
            DefinitionId = definitionId,
            Version = DraftVersion,
            ModelJson = SerializeModel(CreateDefaultModel()),
        };
        await versions.InsertAsync(draft);
        return draft;
    }

    protected virtual string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed))
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNameInvalid);
        ValidateLength(trimmed, 128, "definitionName");
        return trimmed;
    }

    protected virtual void ValidateMetadata(string? icon, string? groupName)
    {
        ValidateLength(icon, 64, "icon");
        ValidateLength(groupName, 64, "groupName");
    }

    protected virtual void ValidateLength(string? value, int maxLength, string field)
    {
        if (value?.Length > maxLength)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelFieldTooLong,
                new Dictionary<string, object?>
                {
                    ["field"] = field,
                    ["maxLength"] = maxLength,
                });
        }
    }

    protected virtual string SerializeModel(WfModel model) => WfModelJson.Serialize(model);

    protected virtual WfModel CreateDefaultModel() => new()
    {
        Version = WfModelJson.CurrentVersion,
        Root = new WfNode { Id = "start", Type = WfNodeType.Start, Name = "" },
    };

    protected virtual WfDefinitionDetailOutput MapDetail(WfDefinition def, WfDefinitionVersion draft)
    {
        var model = WfModelJson.Deserialize(draft.ModelJson) ?? CreateDefaultModel();
        return new WfDefinitionDetailOutput
        {
            Id = def.Id,
            Name = def.Name,
            Icon = def.Icon,
            GroupName = def.GroupName,
            Status = def.Status,
            CurrentVersion = def.CurrentVersion,
            CreateTime = def.CreateTime,
            CreateUserId = def.CreateUserId,
            UpdateTime = def.UpdateTime,
            UpdateUserId = def.UpdateUserId,
            Model = model,
        };
    }
}
