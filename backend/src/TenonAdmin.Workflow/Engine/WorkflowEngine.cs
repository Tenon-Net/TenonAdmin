using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置引擎:一条 Cmd → 一个 DB 事务 → Agenda 循环直至空。
/// 方法拆成 <c>virtual</c> 小步,消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
/// <remarks>
/// M2a 有意的源码级破坏性变更:主构造函数新增 <paramref name="conditionEvaluator"/> 参数(分支求值 SPI,
/// 供 <see cref="EnterNodeOp"/> 选臂用)。M2b 同理追加 <paramref name="notifier"/> 参数(通知 SPI,
/// 供各 Op 建任务 / 实例完结 / 转办后调用)。前置 <c>TryAdd</c> 整体替换 <see cref="IWorkflowEngine"/>
/// 的消费者不受影响(<see cref="IWorkflowEngine"/> 契约本身没动);<b>继承</b> <see cref="WorkflowEngine"/>
/// 的消费者需要在自己的 <c>base(...)</c> 调用里补上这些参数。不为兼容加 <c>[Obsolete]</c> 双构造函数。
/// </remarks>
public class WorkflowEngine(
    IRepository<WfInstance> instances,
    IApproverResolver approverResolver,
    IWorkflowFormBinder formBinder,
    WorkflowOptions options,
    TimeProvider timeProvider,
    IWfConditionEvaluator conditionEvaluator,
    IWorkflowNotifier notifier) : IWorkflowEngine
{
    /// <inheritdoc />
    public virtual async Task<WfEngineResult> ExecuteAsync(
        IWfCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var db = instances.Db;
        WfExecutionContext? ctx = null;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            ctx = command switch
            {
                StartInstanceCmd start => await BeginStartAsync(db, start, cancellationToken),
                CompleteTaskCmd complete => await BeginCompleteAsync(db, complete, cancellationToken),
                TransferTaskCmd transfer => await BeginTransferAsync(db, transfer, cancellationToken),
                CancelInstanceCmd cancel => await BeginCancelAsync(db, cancel, cancellationToken),
                ReturnTaskCmd ret => await BeginReturnAsync(db, ret, cancellationToken),
                ResubmitInstanceCmd resubmit => await BeginResubmitAsync(db, resubmit, cancellationToken),
                _ => throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["command"] = command.GetType().Name }),
            };

            await RunAgendaAsync(ctx, cancellationToken);
            return ctx.ToResult();
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);

        // 事务已提交:此时才派发排队的通知(不能在事务内发——提交失败时不该已经推过,
        // 且真实 SignalR 网关会让客户端在提交落盘前就收到推送去查询,读到脏数据)。
        if (ctx is not null)
            await DispatchPendingNotificationsAsync(ctx, cancellationToken);

        return tran.Data;
    }

    /// <summary>
    /// 事务提交成功后统一派发 <see cref="WfExecutionContext.PendingTaskAssignedNotifications"/> /
    /// <see cref="WfExecutionContext.PendingInstanceCompletedNotification"/>。通知失败仍要 try/catch——
    /// 此时事务已提交,不会回滚,但不能让通知异常炸掉这次 HTTP 响应。
    /// </summary>
    protected virtual async Task DispatchPendingNotificationsAsync(
        WfExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        foreach (var (notifyCtx, userIds) in ctx.PendingTaskAssignedNotifications)
        {
            try
            {
                await ctx.Notifier.TaskAssignedAsync(notifyCtx, userIds, cancellationToken);
            }
            catch (Exception)
            {
                // 通知失败不得影响已提交事务的响应;静默吞掉。
            }
        }

        if (ctx.PendingInstanceCompletedNotification is { } completedCtx)
        {
            try
            {
                await ctx.Notifier.InstanceCompletedAsync(completedCtx, cancellationToken);
            }
            catch (Exception)
            {
                // 同上。
            }
        }
    }

    /// <summary>Agenda 出队循环;某 Op 不再 plan 则自然停(等人审批)。</summary>
    protected virtual async Task RunAgendaAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        while (ctx.Agenda.TryTake(out var op))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await op.ExecuteAsync(ctx, cancellationToken);
        }
    }

    /// <summary>发起:校验挂载点 → 插 instance/token → 历史 → 入队 EnterNode(start)。</summary>
    protected virtual async Task<WfExecutionContext> BeginStartAsync(
        ISqlSugarClient db,
        StartInstanceCmd cmd,
        CancellationToken cancellationToken)
    {
        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == cmd.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);
        if (model.Root.Type != WfNodeType.Start)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "rootNotStart" });
        if (string.IsNullOrWhiteSpace(model.Root.Id))
            model.Root.Id = "start";

        await formBinder.ValidateOnStartAsync(
            new WfFormBindContext
            {
                InstanceId = 0,
                DefinitionVersionId = cmd.DefinitionVersionId,
                BusinessKey = cmd.BusinessKey,
                VariablesJson = cmd.VariablesJson,
                Status = WfInstanceStatus.Running,
                StarterUserId = cmd.StarterUserId,
            },
            cancellationToken);

        var leaderLevels = ResolveLeaderLevels(model);
        var leaderChainByLevel = await SnapshotLeaderChainsAsync(
            cmd.StarterUserId, cmd.StarterOrgId, leaderLevels, cancellationToken);

        var instance = new WfInstance
        {
            DefinitionVersionId = cmd.DefinitionVersionId,
            BusinessKey = cmd.BusinessKey,
            StarterUserId = cmd.StarterUserId,
            Status = WfInstanceStatus.Running,
            VariablesJson = cmd.VariablesJson,
            SelectedUserIdsJson = cmd.SelectedUserIdsByNode is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(cmd.SelectedUserIdsByNode, WfModelJson.Options),
            LeaderChainJson = leaderChainByLevel is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(leaderChainByLevel, WfModelJson.Options),
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = model.Root.Id,
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = cmd.SelectedUserIdsByNode
                                    ?? new Dictionary<string, List<long>>(StringComparer.Ordinal),
            StarterOrgId = cmd.StarterOrgId,
            LeaderChainByLevel = leaderChainByLevel,
        };

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceStarted,
            model.Root.Id,
            new { starterUserId = cmd.StarterUserId, businessKey = cmd.BusinessKey },
            cancellationToken);

        agenda.Plan(new EnterNodeOp(model.Root));
        return ctx;
    }

    /// <summary>完成待办:加载运行态 → 入队 CompleteTaskOp。</summary>
    protected virtual async Task<WfExecutionContext> BeginCompleteAsync(
        ISqlSugarClient db,
        CompleteTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new CompleteTaskOp(task, cmd.UserId, cmd.Action, cmd.Comment));
        return ctx;
    }

    /// <summary>转办:加载运行态 → 入队 TransferTaskOp(不推进 token)。</summary>
    protected virtual async Task<WfExecutionContext> BeginTransferAsync(
        ISqlSugarClient db,
        TransferTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new TransferTaskOp(task, cmd.UserId, cmd.ToUserId, cmd.Comment));
        return ctx;
    }

    /// <summary>撤销:仅发起人、仅无人已批的 Running 实例 → 入队 CancelInstanceOp。</summary>
    protected virtual async Task<WfExecutionContext> BeginCancelAsync(
        ISqlSugarClient db, CancelInstanceCmd cmd, CancellationToken cancellationToken)
    {
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == cmd.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);
        if (instance.StarterUserId != cmd.CallerUserId)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CancelNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notStarter" });
        }

        if (await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.InstanceId == instance.Id && h.Action == WfTaskAction.Approve))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CancelNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "alreadyApproved" });
        }

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instance.Id && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new CancelInstanceOp());
        return ctx;
    }

    /// <summary>退回:加载运行态 → 入队 ReturnTaskOp(关闭当前待办、token 回退,Agenda 留空等重提)。</summary>
    protected virtual async Task<WfExecutionContext> BeginReturnAsync(
        ISqlSugarClient db,
        ReturnTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new ReturnTaskOp(task, cmd.UserId, cmd.TargetNodeId, cmd.Comment));
        return ctx;
    }

    protected virtual IReadOnlyDictionary<string, List<long>> DeserializeSelectedUsers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, List<long>>(StringComparer.Ordinal);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<long>>>(
                       json, WfModelJson.Options)
                   ?? new Dictionary<string, List<long>>(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "selectedUsersInvalid" });
        }
    }

    /// <summary>
    /// 反序列化 <see cref="WfInstance.LeaderChainJson"/>。<c>null</c>/空白 json(老实例或无 multiLeader 节点)
    /// 保持返回 <c>null</c>(=没有快照);非空 json 读取为按 level 键控的链快照。
    /// 与 <see cref="DeserializeSelectedUsers"/> 不同——那里 null 会折叠成空字典,这里保留老实例语义。
    /// </summary>
    protected virtual IReadOnlyDictionary<int, IReadOnlyList<long>>? DeserializeLeaderChainsByLevel(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var chains = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, List<long>>>(
                json, WfModelJson.Options);
            return chains?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<long>)pair.Value);
        }
        catch (System.Text.Json.JsonException)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "leaderChainInvalid" });
        }
    }

    /// <summary>
    /// 扫描整棵模型树(含分支臂内,经 <see cref="WfModelIndex"/>)找出所有 <c>multiLeader</c> 节点,
    /// 将 <c>level</c> 归一化到至少 1 后去重,并保留该 level 首个节点的真实参数包。
    /// 内置 provider 只读取 level;消费者自定义 provider 若让同 level 节点的其它参数不同,
    /// 当前按 level 键控的快照由首个节点参数胜出。若需逐节点快照,应另行扩展 SPI 传入 NodeId。
    /// </summary>
    protected virtual IReadOnlyDictionary<int, Dictionary<string, System.Text.Json.JsonElement>?> ResolveLeaderLevels(
        WfModel model)
    {
        var levels = new Dictionary<int, Dictionary<string, System.Text.Json.JsonElement>?>();
        foreach (var node in WfModelIndex.Build(model).Nodes)
        {
            var assignee = node.Props?.Assignee;
            if (assignee is null ||
                !string.Equals(assignee.Provider, ApproverProviderKeys.MultiLeader, StringComparison.Ordinal))
                continue;

            var level = Math.Max(1, ApproverParamReader.GetInt(assignee.Params, "level", 1));
            levels.TryAdd(level, assignee.Params);
        }

        return levels;
    }

    /// <summary>
    /// 发起时按每个 distinct level 分别算快照;模型无 multiLeader 节点则返回 <c>null</c>(不存)。
    /// 每次保留节点真实参数并覆盖归一化后的 level;<c>LeaderChainByLevel = null</c> 避免自引用,
    /// provider 自然走实时上溯。各级结果原样存(启用过滤后,运行期不再二次过滤)。
    /// </summary>
    protected virtual async Task<IReadOnlyDictionary<int, IReadOnlyList<long>>?> SnapshotLeaderChainsAsync(
        long starterUserId,
        long? starterOrgId,
        IReadOnlyDictionary<int, Dictionary<string, System.Text.Json.JsonElement>?> leaderLevels,
        CancellationToken cancellationToken)
    {
        if (leaderLevels.Count == 0)
            return null;

        var chains = new Dictionary<int, IReadOnlyList<long>>();
        foreach (var (level, nodeParams) in leaderLevels.OrderBy(pair => pair.Key))
        {
            var levelParams = nodeParams is null
                ? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, System.Text.Json.JsonElement>(nodeParams, StringComparer.Ordinal);
            levelParams["level"] = System.Text.Json.JsonSerializer.SerializeToElement(level);

            chains[level] = await approverResolver.ResolveAsync(
                ApproverProviderKeys.MultiLeader,
                new ApproverResolveContext
                {
                    InitiatorUserId = starterUserId,
                    InitiatorOrgId = starterOrgId,
                    Params = levelParams,
                    LeaderChainByLevel = null,
                },
                cancellationToken);
        }

        return chains;
    }

    /// <summary>
    /// 重提:仅发起人、仅退回后无活跃待办的 Running 实例可重提 → 重算 multiLeader 快照 → token/实例回到
    /// start → 入队 EnterNode(start),从头重走一遍(连已批过的节点也重新审)。复用同一实例行,不新建实例。
    /// </summary>
    protected virtual async Task<WfExecutionContext> BeginResubmitAsync(
        ISqlSugarClient db,
        ResubmitInstanceCmd cmd,
        CancellationToken cancellationToken)
    {
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == cmd.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);
        if (instance.StarterUserId != cmd.CallerUserId)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ResubmitNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notStarter" });
        }

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instance.Id && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        if (await db.Queryable<WfTask>().AnyAsync(t => t.TokenId == token.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ResubmitNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "hasActiveTask" });
        }

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        await formBinder.ValidateOnStartAsync(
            new WfFormBindContext
            {
                InstanceId = instance.Id,
                DefinitionVersionId = instance.DefinitionVersionId,
                BusinessKey = instance.BusinessKey,
                VariablesJson = cmd.VariablesJson ?? instance.VariablesJson,
                Status = WfInstanceStatus.Running,
                StarterUserId = instance.StarterUserId,
            },
            cancellationToken);

        if (cmd.VariablesJson is not null)
            instance.VariablesJson = cmd.VariablesJson;
        if (cmd.SelectedUserIdsByNode is not null)
        {
            instance.SelectedUserIdsJson = System.Text.Json.JsonSerializer.Serialize(
                cmd.SelectedUserIdsByNode, WfModelJson.Options);
        }

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var leaderLevels = ResolveLeaderLevels(model);
        var leaderChainByLevel = await SnapshotLeaderChainsAsync(
            instance.StarterUserId, starterOrgId, leaderLevels, cancellationToken);
        instance.LeaderChainJson = leaderChainByLevel is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(leaderChainByLevel, WfModelJson.Options);

        await db.Updateable(instance)
            .UpdateColumns(i => new { i.VariablesJson, i.LeaderChainJson, i.SelectedUserIdsJson, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

        token.NodeId = model.Root.Id;
        await db.Updateable(token)
            .UpdateColumns(t => new { t.NodeId, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = cmd.SelectedUserIdsByNode
                                    ?? DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = leaderChainByLevel,
        };

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceResubmitted,
            model.Root.Id,
            new { starterUserId = cmd.CallerUserId },
            cancellationToken);

        agenda.Plan(new EnterNodeOp(model.Root));
        return ctx;
    }
}
