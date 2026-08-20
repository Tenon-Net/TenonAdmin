using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// token 进入节点:<c>start</c> 立即转移;<c>approval</c> 建待办后停顿;
/// <c>cc</c> 写抄送后继续;<c>next==null</c> 的完结由 <see cref="TakeTransitionOp"/> 处理。
/// </summary>
public class EnterNodeOp(WfNode node) : IWfOperation
{
    protected WfNode Node { get; } = node;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ctx.CurrentNode = Node;

        ctx.Token.NodeId = Node.Id;
        await ctx.Db.Updateable(ctx.Token)
            .UpdateColumns(t => new { t.NodeId, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(WfHistoryEventType.NodeEnter, Node.Id, cancellationToken: cancellationToken);

        switch (Node.Type)
        {
            case WfNodeType.Start:
                // 发起人节点不等人,立刻走向后继。
                ctx.Agenda.Plan(new TakeTransitionOp(Node));
                break;

            case WfNodeType.Approval:
                await EnterApprovalAsync(ctx, cancellationToken);
                break;

            case WfNodeType.Cc:
                await EnterCcAsync(ctx, cancellationToken);
                break;

            case WfNodeType.Branch:
                await EnterBranchAsync(ctx, cancellationToken);
                break;

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.NodeTypeUnsupported,
                    new Dictionary<string, object?> { ["type"] = Node.Type.ToString() });
        }
    }

    /// <summary>解析审批人 → 建 wf_task + actors;空人按三级 nobody 策略。</summary>
    protected virtual async Task EnterApprovalAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        var assignee = Node.Props?.Assignee;
        var providerKey = assignee?.Provider;
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            await ApplyNobodyAsync(ctx, cancellationToken);
            return;
        }

        var users = await ctx.ApproverResolver.ResolveAsync(
            providerKey,
            new ApproverResolveContext
            {
                InitiatorUserId = ctx.Instance.StarterUserId,
                InitiatorOrgId = ctx.StarterOrgId,
                Params = assignee?.Params,
                SelectedUserIds = ctx.GetSelectedUserIds(Node.Id),
                LeaderChainByLevel = ctx.LeaderChainByLevel,
            },
            cancellationToken);

        if (users.Count == 0)
        {
            await ApplyNobodyAsync(ctx, cancellationToken);
            return;
        }

        var signMode = MapSignMode(Node.Props?.Mode, providerKey);
        await CreateTaskAsync(ctx, users, signMode, cancellationToken);
    }

    /// <summary>抄送:写 wf_cc 后立刻转移(抄送≠待办)。</summary>
    protected virtual async Task EnterCcAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        var assignee = Node.Props?.Assignee;
        var providerKey = assignee?.Provider;
        IReadOnlyList<long> users = [];
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            users = await ctx.ApproverResolver.ResolveAsync(
                providerKey,
                new ApproverResolveContext
                {
                    InitiatorUserId = ctx.Instance.StarterUserId,
                    InitiatorOrgId = ctx.StarterOrgId,
                    Params = assignee?.Params,
                    SelectedUserIds = ctx.GetSelectedUserIds(Node.Id),
                    LeaderChainByLevel = ctx.LeaderChainByLevel,
                },
                cancellationToken);
        }

        if (users.Count > 0)
        {
            var rows = users.Select(uid => new WfCc
            {
                InstanceId = ctx.Instance.Id,
                NodeId = Node.Id,
                UserId = uid,
                IsRead = false,
            }).ToList();
            await ctx.Db.Insertable(rows).ExecuteCommandAsync();
            ctx.NewCcUserIds.AddRange(users);
            await ctx.AppendHistoryAsync(
                WfHistoryEventType.CcSent,
                Node.Id,
                new { userIds = users },
                cancellationToken);
        }

        ctx.Agenda.Plan(new TakeTransitionOp(Node));
    }

    /// <summary>
    /// 分支执行:选臂 → 写 <see cref="WfHistoryEventType.GatewayTaken"/> →
    /// 臂无子链(<c>arm.Next is null</c>,直接汇合到 <c>Node.Next</c>)时交给 <see cref="TakeTransitionOp"/>
    /// 写 <see cref="WfHistoryEventType.NodeLeave"/> 并求汇合;否则本方法先写 NodeLeave 再进臂子链入口。
    /// </summary>
    protected virtual async Task EnterBranchAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        var arm = Node.Conditions is { Count: > 0 } arms
            ? SelectArm(arms, ctx.ConditionEvaluator, ctx.Instance.VariablesJson)
            : null;
        if (arm is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "branchNoArmMatched", ["nodeId"] = Node.Id });
        }

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.GatewayTaken,
            Node.Id,
            new { armId = arm.Id, armName = arm.Name, isDefault = arm.IsDefault },
            cancellationToken);

        if (arm.Next is null)
        {
            ctx.Agenda.Plan(new TakeTransitionOp(Node));
            return;
        }

        await ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, Node.Id, cancellationToken: cancellationToken);
        ctx.Agenda.Plan(new EnterNodeOp(arm.Next));
    }

    /// <summary>
    /// 选臂:按 <paramref name="arms"/> 数组顺序取第一条非默认且条件求值为 true 的臂;都不中则取默认臂;
    /// 都没有返回 <c>null</c>。入参全是普通值(不吃 ctx),便于单测。求值器本身失败安全(不抛异常),
    /// 这里不再加一层 try/catch 或类型判断。
    /// </summary>
    protected virtual WfBranchArm? SelectArm(
        IReadOnlyList<WfBranchArm> arms,
        IWfConditionEvaluator evaluator,
        string? variablesJson)
    {
        WfBranchArm? defaultArm = null;
        foreach (var arm in arms)
        {
            if (arm.IsDefault)
            {
                defaultArm ??= arm;
                continue;
            }

            if (evaluator.Evaluate(arm.Expr, variablesJson))
                return arm;
        }

        return defaultArm;
    }

    /// <summary>空审批人:节点 &gt; 流程 &gt; 全局。</summary>
    protected virtual async Task ApplyNobodyAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        var action = ResolveNobody(ctx);
        switch (action)
        {
            case WfNobodyAction.AutoPass:
                ctx.Agenda.Plan(new TakeTransitionOp(Node));
                return;

            case WfNobodyAction.Transfer:
                var uid = Node.Props?.NobodyTransferUserId
                          ?? ctx.Model.NobodyTransferUserId
                          ?? 0;
                if (uid <= 0)
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.NobodyBlocked,
                        new Dictionary<string, object?> { ["nodeId"] = Node.Id, ["reason"] = "transferWithoutUser" });
                }
                await CreateTaskAsync(ctx, [uid], WfSignMode.Any, cancellationToken);
                return;

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.NobodyBlocked,
                    new Dictionary<string, object?> { ["nodeId"] = Node.Id });
        }
    }

    protected virtual async Task CreateTaskAsync(
        WfExecutionContext ctx,
        IReadOnlyList<long> userIds,
        WfSignMode signMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = new WfTask
        {
            InstanceId = ctx.Instance.Id,
            NodeId = Node.Id,
            TokenId = ctx.Token.Id,
            SignMode = signMode,
            Version = 0,
            DueTime = null,
        };
        await ctx.Db.Insertable(task).ExecuteCommandAsync();

        var actors = new List<WfTaskActor>(userIds.Count);
        for (var i = 0; i < userIds.Count; i++)
        {
            actors.Add(new WfTaskActor
            {
                TaskId = task.Id,
                UserId = userIds[i],
                ActorType = WfActorType.Approver,
                Status = signMode == WfSignMode.Sequential && i > 0
                    ? WfActorStatus.Waiting
                    : WfActorStatus.Pending,
                Sort = signMode == WfSignMode.Sequential ? i + 1 : 0,
            });
        }
        await ctx.Db.Insertable(actors).ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCreated,
            Node.Id,
            new { taskId = task.Id, userIds, signMode = signMode.ToString() },
            cancellationToken);

        ctx.CreatedTaskId = task.Id;
        ctx.NewAssigneeUserIds.AddRange(
            signMode == WfSignMode.Sequential ? userIds.Take(1) : userIds);
        // 停顿等人——不再 plan TakeTransition。
    }

    protected virtual WfNobodyAction ResolveNobody(WfExecutionContext ctx)
    {
        if (Node.Props?.Nobody is { } nodeLevel)
            return nodeLevel;
        if (ctx.Model.Nobody is { } modelLevel)
            return modelLevel;
        return ParseNobody(ctx.Options.Nobody);
    }

    protected static WfNobodyAction ParseNobody(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "transfer" => WfNobodyAction.Transfer,
            "block" => WfNobodyAction.Block,
            _ => WfNobodyAction.AutoPass,
        };

    /// <summary>schema mode → 实体枚举;multiLeader 强制顺序(CONTEXT 连续多级主管)。</summary>
    protected static WfSignMode MapSignMode(WfApprovalMode? mode, string providerKey)
    {
        if (string.Equals(providerKey, ApproverProviderKeys.MultiLeader, StringComparison.Ordinal))
            return WfSignMode.Sequential;

        return mode switch
        {
            WfApprovalMode.All => WfSignMode.All,
            WfApprovalMode.Seq => WfSignMode.Sequential,
            _ => WfSignMode.Any,
        };
    }
}
