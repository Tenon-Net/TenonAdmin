using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 完成待办计票:处理或签、顺序审批和会签。
/// 通过则继续流转;拒绝按节点配置终止实例或回退到指定节点。
/// </summary>
public class CompleteTaskOp(
    WfTask task,
    long userId,
    WfTaskAction action,
    string? comment,
    string? submittedVariablesJson = null,
    bool allowAnyFileOwner = false) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected WfTaskAction Action { get; } = action;
    protected string? Comment { get; } = comment;
    protected string? SubmittedVariablesJson { get; } = submittedVariablesJson;
    protected bool AllowAnyFileOwner { get; } = allowAnyFileOwner;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Action is not (WfTaskAction.Approve or WfTaskAction.Reject))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["action"] = Action.ToString() });
        }

        // 任务级 CAS:同一任务的并发动作只有一个事务能继续推进。
        var taskClaimed = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = Task.Version + 1 })
            .Where(t => t.Id == Task.Id && t.Version == Task.Version)
            .ExecuteCommandAsync();
        if (taskClaimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        }
        Task.Version++;

        // 关闭任务会把剩余 Pending/Waiting actor 翻为 Skipped。先取主键快照,再写 actor/HistorySeq;
        // SQL Server 上不能在持有 actor 行写锁后反向扫描 actor 表,否则并行臂会形成锁环。
        var remainingActorIds = await FindRemainingActorIdsAsync(ctx, Task.Id, cancellationToken);

        // 读 ActivatedTime 在先(数据库评审 §4.3):不改变下面 CAS 的判定条件与并发语义,只是额外取一次
        // 快照,给 DurationMs/StartedTime 用。读不到不代表并发失败——下面的 CAS 才是唯一裁判。
        var actor = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == UserId && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
            .FirstAsync();

        // 仅当前 Pending 办理人可翻 Done;顺序审批的后级仍是 Waiting。
        var claimed = actor is null
            ? 0
            : await ctx.Db.Updateable<WfTaskActor>()
                .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Done })
                .Where(a => a.Id == actor.Id && a.Status == WfActorStatus.Pending)
                .ExecuteCommandAsync();
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        }

        var node = ctx.FindNode(Task.NodeId)
                   ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                       new Dictionary<string, object?> { ["nodeId"] = Task.NodeId });
        ctx.CurrentNode = node;

        await ApplyFormValuesAsync(ctx, node, cancellationToken);

        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        var durationMs = Math.Max(0, (long)(now - (actor?.ActivatedTime ?? Task.CreateTime)).TotalMilliseconds);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = Task.InstanceId,
            NodeId = Task.NodeId,
            NodeName = node.Name,
            TaskId = Task.Id,
            TokenId = Task.TokenId,
            UserId = UserId,
            OriginalUserId = actor?.OriginalUserId,
            DelegationRuleId = actor?.DelegationRuleId,
            DelegationScopeOrgId = actor?.DelegationScopeOrgId,
            Action = Action,
            Comment = Comment,
            DurationMs = durationMs,
            StartedTime = actor?.ActivatedTime,
            NodeVisitId = Task.NodeVisitId,
        }).ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCompleted,
            Task.NodeId,
            new { taskId = Task.Id, userId = UserId, action = Action.ToString() },
            cancellationToken);

        if (Action == WfTaskAction.Reject && Task.SignMode != WfSignMode.All)
        {
            await CloseTaskAsync(ctx, Task, skipRemaining: true, cancellationToken, remainingActorIds);
            await RejectInstanceAsync(ctx, node, cancellationToken);
            return;
        }

        if (Action == WfTaskAction.Reject)
        {
            var (_, impossible) = await EvaluateAllAsync(ctx, Task, node, cancellationToken);
            if (impossible)
            {
                await CloseTaskAsync(ctx, Task, skipRemaining: true, cancellationToken, remainingActorIds);
                await RejectInstanceAsync(ctx, node, cancellationToken);
                return;
            }

            await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
            return;
        }

        // Approve 计票
        var passed = await TryPassAsync(ctx, cancellationToken);
        if (!passed)
        {
            // 未满票的同意**也是一次审批**,必须领取 token —— 否则 mode=all / mode=seq(以及被
            // MapSignMode 强制成 Sequential 的 multiLeader)的**非末位投票**这条路上实例与 token 一字
            // 不动,本轮的两级 CAS 一个都不触发:并发撤销的 ClaimInstanceAsync(Running) 与
            // ClaimTokenAsync(Active) 两个条件全都满足,于是「会签第一票同意」与「发起人撤销」两边都
            // 成功,落成 Status = Cancelled 与一条 Approve 行共存(BeginCancelAsync 的「无任何 Approve
            // 行」准入只是一次读、提交前不复验),违背语义契约「仅当没有任何 Approve 记录才允许撤销」。
            // 语义站得住:这个 token 上的签核进度前进了一步,与「进节点也算状态推进」是同一条论证;
            // 锚的是本 token,故不引入过度加锁(M3 并行网关下不同 token 各走各的行)。
            await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
            return; // 顺序/会签未满票:待办仍在,Agenda 空 → 等人
        }

        await CloseTaskAsync(ctx, Task, skipRemaining: true, cancellationToken, remainingActorIds);
        ctx.Agenda.Plan(new TakeTransitionOp(node));
    }

    /// <summary>在办理事务内按发布快照合并内置表单值;旧表单路径保持原变量语义。</summary>
    protected virtual async Task ApplyFormValuesAsync(
        WfExecutionContext ctx,
        WfNode node,
        CancellationToken cancellationToken)
    {
        if (ctx.Model.FormSchema is null || !string.IsNullOrWhiteSpace(ctx.Model.FormComponent)) return;

        WfInstance source = ctx.Instance;
        if (SubmittedVariablesJson is not null)
        {
            // 会签不同 actor 可以同时提交;实例版本 CAS 让变量合并基于最新快照,避免后写请求覆盖先写请求。
            await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
            source = await ctx.Db.Queryable<WfInstance>()
                .ClearFilter<IOrgScoped>()
                .Where(i => i.Id == ctx.Instance.Id)
                .FirstAsync()
                ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        }

        var next = await WfFormRuntime.ApplyAsync(
            ctx.Db,
            enabled: true,
            schema: ctx.Model.FormSchema,
            currentJson: source.VariablesJson,
            submittedJson: SubmittedVariablesJson,
            permissions: node.Type == WfNodeType.Approval ? node.Props?.FormPerms : null,
            cancellationToken: cancellationToken,
            actorUserId: UserId,
            allowAnyFileOwner: AllowAnyFileOwner);
        if (SubmittedVariablesJson is null) return;

        ctx.Instance.VariablesJson = next;
        await ctx.Db.Updateable(ctx.Instance)
            .UpdateColumns(i => new { i.VariablesJson, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();
    }

    /// <summary>或签立刻通过;顺序晋级下一位;会签看是否还有 Pending。</summary>
    protected virtual async Task<bool> TryPassAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (Task.SignMode)
        {
            case WfSignMode.Any:
                return true;

            case WfSignMode.Sequential:
                var next = await ctx.Db.Queryable<WfTaskActor>()
                    .Where(a => a.TaskId == Task.Id
                                && a.Status == WfActorStatus.Waiting
                                && a.ActorType == WfActorType.Approver)
                    .OrderBy(a => a.Sort, OrderByType.Asc)
                    .FirstAsync();
                if (next is null)
                    return true;

                var promotedAt = ctx.TimeProvider.GetLocalNow().DateTime;
                var promoted = await ctx.Db.Updateable<WfTaskActor>()
                    .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Pending, ActivatedTime = promotedAt })
                    .Where(a => a.Id == next.Id && a.Status == WfActorStatus.Waiting)
                    .ExecuteCommandAsync();
                if (promoted != 1)
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                        new Dictionary<string, object?> { ["taskId"] = Task.Id });
                }
                ctx.NewAssigneeUserIds.Add(next.UserId);
                ctx.PendingTaskAssignedNotifications.Add((
                    new WfNotifyContext
                    {
                        InstanceId = ctx.Instance.Id,
                        DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                        BusinessKey = ctx.Instance.BusinessKey,
                        NodeId = Task.NodeId,
                        NodeName = ctx.CurrentNode?.Name,
                        StarterUserId = ctx.Instance.StarterUserId,
                        Status = ctx.Instance.Status,
                    },
                    [next.UserId]));
                return false;

            case WfSignMode.All:
            default:
                var (passed, _) = await EvaluateAllAsync(ctx, Task, ctx.CurrentNode!, cancellationToken);
                return passed;
        }
    }

    /// <summary>按当前节点访问的有效审批人重算会签门槛与剩余可达票数。</summary>
    internal static async Task<(bool Passed, bool Impossible)> EvaluateAllAsync(
        WfExecutionContext ctx,
        WfTask task,
        WfNode node,
        CancellationToken cancellationToken)
    {
        var statuses = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id && a.ActorType == WfActorType.Approver
                        && a.Status != WfActorStatus.Skipped)
            .Select(a => a.Status)
            .ToListAsync();
        var approvals = await ctx.Db.Queryable<WfHisTask>()
            .Where(h => h.TaskId == task.Id && h.NodeVisitId == task.NodeVisitId
                        && h.Action == WfTaskAction.Approve)
            .CountAsync();
        var remaining = statuses.Count(status => status is WfActorStatus.Pending or WfActorStatus.Waiting);
        var ratio = node.Props?.AllPassRatio ?? 100;
        var required = Math.Max(1, (int)(((long)statuses.Count * ratio + 99) / 100));
        return (approvals >= required, approvals + remaining < required);
    }

    internal static Task<List<long>> FindRemainingActorIdsAsync(
        WfExecutionContext ctx,
        long taskId,
        CancellationToken cancellationToken) =>
        ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == taskId
                        && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

    internal static async Task CloseTaskAsync(
        WfExecutionContext ctx,
        WfTask task,
        bool skipRemaining,
        CancellationToken cancellationToken,
        List<long>? remainingActorIds = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (skipRemaining)
        {
            // Pending(或签/会签未行动的候选人)与 Waiting(顺序会签尚未轮到的候选人)都要给一个终态,
            // 否则 Waiting 行会永远卡在「尚未轮到」——数据库评审 §4.4 要求分配历史必须完整、不能有
            // 「查不出最终去向」的行。
            remainingActorIds ??= await FindRemainingActorIdsAsync(ctx, task.Id, cancellationToken);
            if (remainingActorIds.Count > 0)
            {
                await ctx.Db.Updateable<WfTaskActor>()
                    .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
                    .Where(a => remainingActorIds.Contains(a.Id)
                                && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
                    .ExecuteCommandAsync(cancellationToken);
            }
        }

        // 办理人分配历史(数据库评审 §4.4)不再物理删 —— 二选一里选了「保留 wf_task_actor,关闭只翻状态」:
        // 现有的「我的待办」等全部读路径已经逐一确认过都显式过滤 Status == Pending(见 WfTaskService/
        // WfInstanceService/WorkflowEngine.ResolvePendingActorsAsync/WfTimeoutJob),没有任何地方靠「这行
        // 还在不在」判活跃,keep 下来零风险;换新表反而要多一套实体/仓储/可替换性面,对已有信息纯属复制。
        // wf_task 本身仍然物理删——它承担的是另一个职责:改派/超时等路径的隐式不变量「终态动作必删活跃
        // wf_task」(见 ReassignTaskOpBase 的详细注释),与本表的历史留存无关,不能一并保留。
        await ctx.Db.Deleteable<WfTask>().In(task.Id).ExecuteCommandAsync();
    }

    /// <summary>
    /// 拒绝:节点未配置 <see cref="WfRejectAction"/> 或配为 <see cref="WfRejectAction.Terminate"/> 时终止整单;
    /// 配为 <see cref="WfRejectAction.ToNode"/> 时不终止,回退到 <see cref="WfNodeProps.RejectToNodeId"/> 指向的
    /// 节点重新进入(M2)——实例仍在正常审批流程中,后续一切交给 <see cref="EnterNodeOp"/> 处理。
    /// </summary>
    protected virtual async Task RejectInstanceAsync(
        WfExecutionContext ctx,
        WfNode node,
        CancellationToken cancellationToken)
    {
        if (node.Props?.OnReject == WfRejectAction.ToNode)
        {
            var target = ctx.FindNode(node.Props!.RejectToNodeId!)
                         ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                             new Dictionary<string, object?> { ["reason"] = "rejectTargetInvalid" });

            await ctx.AppendHistoryAsync(
                WfHistoryEventType.RejectRouted,
                node.Id,
                new { fromNodeId = node.Id, targetNodeId = target.Id },
                cancellationToken);
            await ctx.AppendHistoryAsync(WfHistoryEventType.NodeLeave, node.Id, cancellationToken: cancellationToken);
            ctx.Agenda.Plan(new EnterNodeOp(target));
            return;
        }

        // 终态写入前先领取实例与 token(数据库评审 §4.1)。只在**终止**分支做:上面的 ToNode 分支压根
        // 不写实例/token 状态,它 plan 的 EnterNodeOp 会自己领取 token。
        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);
        await ctx.WriteInstanceTerminalStatusAsync(WfInstanceStatus.Rejected, cancellationToken);

        await ParallelControlOps.CancelInstanceRuntimeAsync(ctx, "rejected", cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceCompleted,
            payload: new { status = WfInstanceStatus.Rejected.ToString() },
            cancellationToken: cancellationToken);

        await ctx.FormBinder.OnInstanceCompletedAsync(
            new WfFormBindContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                VariablesJson = ctx.Instance.VariablesJson,
                Status = WfInstanceStatus.Rejected,
                StarterUserId = ctx.Instance.StarterUserId,
            },
            cancellationToken);

        // 通知排队,事务提交后由 WorkflowEngine 统一派发。
        ctx.PendingInstanceCompletedNotification = new WfNotifyContext
        {
            InstanceId = ctx.Instance.Id,
            DefinitionVersionId = ctx.Instance.DefinitionVersionId,
            BusinessKey = ctx.Instance.BusinessKey,
            StarterUserId = ctx.Instance.StarterUserId,
            Status = WfInstanceStatus.Rejected,
        };
    }
}
