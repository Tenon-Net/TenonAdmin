using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// token 进入节点:<c>start</c> 立即转移;<c>approval</c> 建待办后停顿;
/// <c>cc</c> 写抄送后继续;<c>next==null</c> 的完结由 <see cref="TakeTransitionOp"/> 处理。
/// </summary>
public class EnterNodeOp(WfNode node) : IWfOperation
{
    /// <summary>
    /// <see cref="ResolveDueTime"/> 允许的最大超时小时数(10 年)。<see cref="WfTimeout.Hours"/> 是
    /// <c>int</c>,设计器里填个 <c>int.MaxValue</c> 会让 <see cref="DateTime.AddHours"/> 溢出抛
    /// <see cref="ArgumentOutOfRangeException"/> → 发起流程 500。截到上限而不是报错:语义上
    /// 「10 年后到期」等于「基本不会到期」,正是填这种数的人想表达的意思。
    /// </summary>
    protected const int MaxTimeoutHours = 87_600;

    protected WfNode Node { get; } = node;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ctx.CurrentNode = Node;

        // token 级 CAS,必须留在**本方法**的第一个写操作位置:换节点就是状态推进,先抢锁再留痕。
        // ⚠「第一个写操作」只在方法内成立,**跨事务不成立**:approve 路径上任务级 CAS 在前、
        // wf_his_task 与 wf_history(TaskCompleted) 都写在本 Op 的 token 领取之前。安全性由整事务回滚
        // 兜底(一条 Cmd 一个事务),不由语句顺序保证。
        // 这是覆盖「审批 vs 撤销」**这个方向**的手段——一次会推进 token 的同意与一次并发撤销都要 CAS
        // 同一行,只有一个拿得到 1 行,输的整事务回滚。**不是唯一手段**:反方向另有一道既有防线——
        // 撤销要删掉那行活跃 wf_task,而同意已经把它删了,撤销的 FirstAsync() 找不到行;而未满票的
        // 同意这条路本 Op 压根不跑,由 CompleteTaskOp 的 !passed 分支自己领取 token 补上。
        // 一次事务里本 Op 可能跑多次(start → 汇合 → 审批节点),每次领取一次,助手把新版本写回
        // ctx.Token 故后续 CAS 对得上。
        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);

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

        // multiLeader(连续多级主管)的语义就是同一人跨级重现——不是意外重复,豁免去重。
        if (string.Equals(providerKey, ApproverProviderKeys.MultiLeader, StringComparison.Ordinal))
        {
            await CreateTaskAsync(ctx, users, signMode, cancellationToken);
            return;
        }

        await CreateTaskDedupedAsync(ctx, users, signMode, cancellationToken);
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
            // 按 (InstanceId, NodeId, UserId) 幂等:重提会从 start 把整条链(含 cc 节点)重走一遍,
            // wf_cc 没有唯一约束,无条件 Insertable 会给同一 (实例, 节点, 用户) 反复插行 → 抄送列表出现
            // 重复条目、标已读只标掉其中一行。先查后插(四库通用,不用某一库特有的 upsert 语法)。
            var existing = await ctx.Db.Queryable<WfCc>()
                .Where(c => c.InstanceId == ctx.Instance.Id && c.NodeId == Node.Id)
                .Select(c => c.UserId)
                .ToListAsync();
            var rows = users.Distinct()
                .Where(uid => !existing.Contains(uid))
                .Select(uid => new WfCc
                {
                    InstanceId = ctx.Instance.Id,
                    NodeId = Node.Id,
                    UserId = uid,
                    IsRead = false,
                })
                .ToList();
            if (rows.Count > 0)
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
            DueTime = ResolveDueTime(ctx),
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
        var pendingUserIds = signMode == WfSignMode.Sequential ? userIds.Take(1).ToList() : userIds;
        ctx.NewAssigneeUserIds.AddRange(pendingUserIds);

        ctx.PendingTaskAssignedNotifications.Add((
            new WfNotifyContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                NodeId = Node.Id,
                NodeName = Node.Name,
                StarterUserId = ctx.Instance.StarterUserId,
                Status = ctx.Instance.Status,
            },
            pendingUserIds));
        // 停顿等人——不再 plan TakeTransition。通知排队,事务提交后由 WorkflowEngine 统一派发。
    }

    /// <summary>
    /// 建任务时算 <see cref="WfTask.DueTime"/>:节点 <see cref="WfNodeProps.Timeout"/> 的
    /// <see cref="WfTimeout.Hours"/> 大于 0 才计时,否则 <c>null</c>(=不启用)。
    /// <para><c>Hours &lt;= 0</c> 必须等于「不启用」而不是抛错:<see cref="WfTimeout.Hours"/> 是非可空
    /// <c>int</c>,设计器上只点了 <c>action</c> 没填小时数就是 0——若把 0 当「立刻到期」,这类节点
    /// 建完任务当场就被超时策略处置;若抛错,存量定义整条流程发不起来。</para>
    /// <para>不做整秒截断:<see cref="WfTimeoutJob"/> 用的是不等式比较(<c>DueTime &lt;= now</c>),
    /// MySQL <c>datetime(0)</c> 的毫秒四舍五入最多让任务早/晚半秒到期,没有 CAS 失效风险。
    /// (内核的 <c>JobTime.Truncate</c> 是 <c>internal</c>,跨程序集取不到。)</para>
    /// <para>拆成 <c>virtual</c> 单步而非内联表达式:「按工作日算到期」这类需求正好覆写这一步。</para>
    /// </summary>
    protected virtual DateTime? ResolveDueTime(WfExecutionContext ctx)
    {
        if (Node.Props?.Timeout is not { } timeout || timeout.Hours <= 0)
            return null;

        var hours = Math.Min(timeout.Hours, MaxTimeoutHours);
        return ctx.TimeProvider.GetLocalNow().DateTime.AddHours(hours);
    }

    /// <summary>
    /// 同一人相邻节点去重:与最近一个已审批节点的完整审批人集合求交集,交集内的人不再重复建待办;
    /// 若去重后无人剩余,本节点直接自动通过(不建 wf_task);若有剩余,只给剩余人建待办。
    /// </summary>
    protected virtual async Task CreateTaskDedupedAsync(
        WfExecutionContext ctx,
        IReadOnlyList<long> users,
        WfSignMode signMode,
        CancellationToken cancellationToken)
    {
        var adjacentApproved = await ResolveAdjacentApprovedUserIdsAsync(ctx, cancellationToken);
        if (adjacentApproved.Count == 0 || !users.Any(adjacentApproved.Contains))
        {
            await CreateTaskAsync(ctx, users, signMode, cancellationToken);
            return;
        }

        var skipped = users.Where(adjacentApproved.Contains).ToList();
        var remaining = users.Where(u => !adjacentApproved.Contains(u)).ToList();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.DuplicateApproverSkipped,
            Node.Id,
            new { nodeId = Node.Id, userIds = skipped },
            cancellationToken);

        if (remaining.Count == 0)
        {
            ctx.Agenda.Plan(new TakeTransitionOp(Node));
            return;
        }

        await CreateTaskAsync(ctx, remaining, signMode, cancellationToken);
    }

    /// <summary>
    /// 取「本 token 最近一个已审批节点」的完整审批人集合(同节点可能多人各一行,如顺序/会签)。
    /// 按 <c>wf_his_task.Id</c> 倒序取第一条 Approve 行的 NodeId,再收集同 NodeId 的全部 UserId。
    /// 无任何 Approve 行(链上第一个审批节点)时返回空集合。
    /// <para><b>向后跳转重置基线</b>:token 只在单向前进时才有「紧邻的上一个已审批节点」。拒绝路由
    /// (<see cref="WfRejectAction.ToNode"/>)/ 主动退回(<see cref="ReturnTaskOp"/>)/ 退回重提都会让 token
    /// 向后跳,跳转目标往往正是最近一条 Approve 行所在的节点——若沿用它当基线,回退目标会被判成「已审过」
    /// 而整节点自动通过,拒绝路由退化成空操作、重提「从头重走」跳过已批节点。故本方法只认最近一次跳转
    /// <b>之后</b>的 Approve 行:跳转之前批过的节点在回退后必须重新审。跳转下界取同表的
    /// <see cref="WfTaskAction.Reject"/>/<see cref="WfTaskAction.Return"/> 行(两者都由跳转发起方在同一事务里
    /// 先写入 <c>wf_his_task</c>,重提则必然前置一次 Return),不跨表比较雪花 Id。</para>
    /// </summary>
    protected virtual async Task<IReadOnlySet<long>> ResolveAdjacentApprovedUserIdsAsync(
        WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await ctx.Db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == ctx.Instance.Id && h.TokenId == ctx.Token.Id
                        && (h.Action == WfTaskAction.Approve
                            || h.Action == WfTaskAction.Reject
                            || h.Action == WfTaskAction.Return))
            .OrderBy(h => h.Id, OrderByType.Desc)
            .ToListAsync();

        // 倒序遇到的第一条 Reject/Return 行就是最近一次向后跳转,它及更早的行一律不参与基线。
        var sinceLastJump = rows.TakeWhile(h => h.Action == WfTaskAction.Approve).ToList();
        if (sinceLastJump.Count == 0)
            return new HashSet<long>();

        return sinceLastJump
            .TakeWhile(h => h.NodeId == sinceLastJump[0].NodeId)
            .Select(h => h.UserId)
            .ToHashSet();
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
