using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>管理员干预共用的原因与目标校验。三条操作都要在历史里留下谁、做了什么、为什么。</summary>
internal static class AdminIntervention
{
    internal const int MaxReasonLength = 512;

    internal static string RequireReason(string? reason)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length == 0)
            throw Denied("reasonRequired");
        if (text.Length > MaxReasonLength)
            throw Denied("reasonTooLong");
        return text;
    }

    internal static async Task RequireEnabledUserAsync(
        WfExecutionContext ctx,
        long userId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw Denied("targetRequired");

        var enabled = await ctx.Db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == userId && u.Enabled == true)
            .AnyAsync(cancellationToken);
        if (!enabled)
            throw Denied("userUnavailable");
    }

    internal static AdminException Denied(string reason) =>
        WorkflowErrorCode.Exception(
            WorkflowErrorCode.AdminInterventionNotAllowed,
            new Dictionary<string, object?> { ["reason"] = reason });

    internal static WfNotifyContext Notify(WfExecutionContext ctx, WfNode node) => new()
    {
        InstanceId = ctx.Instance.Id,
        DefinitionVersionId = ctx.Instance.DefinitionVersionId,
        BusinessKey = ctx.Instance.BusinessKey,
        NodeId = node.Id,
        NodeName = node.Name,
        StarterUserId = ctx.Instance.StarterUserId,
        Status = ctx.Instance.Status,
    };

    internal static string NodeName(WfNode node) =>
        string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name;
}

/// <summary>
/// 管理员强制转办。管理员不必是当前办理人。
/// 已有待办:跳过 Pending/Waiting 审批人,换成目标用户,并通知两边。
/// 阻断节点没有待办:按指定用户补建一件或签待办,只通知新办理人。
/// </summary>
public class AdminForceTransferOp(long toUserId, string? reason) : IWfOperation
{
    private const int MaxTimeoutHours = 87_600;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = AdminIntervention.RequireReason(reason);
        await AdminIntervention.RequireEnabledUserAsync(ctx, toUserId, cancellationToken);

        var node = ctx.FindNode(ctx.Token.NodeId)
                   ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                       new Dictionary<string, object?> { ["nodeId"] = ctx.Token.NodeId });
        ctx.CurrentNode = node;

        // 已有待办时只抢任务版本,和同意/转办同一把锁,避免先锁 token 再锁任务与审批路径死锁。
        // 阻断节点没有待办,只能抢 token,占住后再复查一次,防止两个人各补一行。
        var task = await ctx.Db.Queryable<WfTask>()
            .Where(t => t.InstanceId == ctx.Instance.Id && t.TokenId == ctx.Token.Id)
            .FirstAsync(cancellationToken);
        if (task is null)
        {
            if (node.Type != WfNodeType.Approval)
                throw AdminIntervention.Denied("notApprovalNode");
            await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
            task = await ctx.Db.Queryable<WfTask>()
                .Where(t => t.InstanceId == ctx.Instance.Id && t.TokenId == ctx.Token.Id)
                .FirstAsync(cancellationToken);
            if (task is null)
            {
                await CreateBlockedTaskAsync(ctx, node, text, cancellationToken);
                return;
            }
        }

        await ReassignExistingTaskAsync(ctx, node, task, text, cancellationToken);
    }

    private async Task ReassignExistingTaskAsync(
        WfExecutionContext ctx,
        WfNode node,
        WfTask task,
        string text,
        CancellationToken cancellationToken)
    {
        var active = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id && a.ActorType == WfActorType.Approver
                        && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
            .ToListAsync(cancellationToken);
        if (active.Any(a => a.UserId == toUserId))
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.AdminInterventionNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "alreadyActor", ["toUserId"] = toUserId });
        }

        var claimed = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = task.Version + 1 })
            .Where(t => t.Id == task.Id && t.Version == task.Version)
            .ExecuteCommandAsync(cancellationToken);
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = task.Id });
        }

        var actorIds = active.Select(a => a.Id).ToList();
        if (actorIds.Count > 0)
        {
            await ctx.Db.Updateable<WfTaskActor>()
                .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
                .Where(a => actorIds.Contains(a.Id)
                            && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
                .ExecuteCommandAsync(cancellationToken);
        }

        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        await ctx.Db.Insertable(new WfTaskActor
        {
            TaskId = task.Id,
            UserId = toUserId,
            ActorType = WfActorType.Approver,
            Status = WfActorStatus.Pending,
            Sort = 0,
            ActivatedTime = now,
        }).ExecuteCommandAsync(cancellationToken);

        await WriteTransferRecordAsync(ctx, node, task.Id, task.NodeVisitId, text, cancellationToken);

        var displaced = active.Select(a => a.UserId).Distinct().ToList();
        if (displaced.Count > 0)
        {
            ctx.PendingTaskRecalledNotifications.Add((
                AdminIntervention.Notify(ctx, node),
                task.Id,
                displaced));
        }

        ctx.NewAssigneeUserIds.Add(toUserId);
        ctx.PendingTaskAssignedNotifications.Add((AdminIntervention.Notify(ctx, node), [toUserId]));
    }

    /// <summary>阻断节点没有 wf_task。按管理员指定的人补建,不再走空审批人策略。</summary>
    private async Task CreateBlockedTaskAsync(
        WfExecutionContext ctx,
        WfNode node,
        string text,
        CancellationToken cancellationToken)
    {
        var task = new WfTask
        {
            InstanceId = ctx.Instance.Id,
            NodeId = node.Id,
            TokenId = ctx.Token.Id,
            SignMode = WfSignMode.Any,
            Version = 0,
            DueTime = ResolveDueTime(ctx, node),
            NodeVisitId = ctx.Token.NodeVisitId,
        };
        await ctx.Db.Insertable(task).ExecuteCommandAsync(cancellationToken);

        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        await ctx.Db.Insertable(new WfTaskActor
        {
            TaskId = task.Id,
            UserId = toUserId,
            ActorType = WfActorType.Approver,
            Status = WfActorStatus.Pending,
            Sort = 0,
            ActivatedTime = now,
        }).ExecuteCommandAsync(cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCreated,
            node.Id,
            new { taskId = task.Id, signMode = WfSignMode.Any.ToString() },
            cancellationToken);

        await WriteTransferRecordAsync(ctx, node, task.Id, task.NodeVisitId, text, cancellationToken);
        ctx.CreatedTaskId = task.Id;
        ctx.NewAssigneeUserIds.Add(toUserId);
        ctx.PendingTaskAssignedNotifications.Add((AdminIntervention.Notify(ctx, node), [toUserId]));
    }

    private async Task WriteTransferRecordAsync(
        WfExecutionContext ctx,
        WfNode node,
        long taskId,
        long? nodeVisitId,
        string text,
        CancellationToken cancellationToken)
    {
        var nodeName = AdminIntervention.NodeName(node);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = ctx.Instance.Id,
            NodeId = node.Id,
            NodeName = nodeName,
            TaskId = taskId,
            TokenId = ctx.Token.Id,
            UserId = ctx.ActorUserId ?? 0,
            Action = WfTaskAction.AdminTransfer,
            Comment = text,
            DurationMs = 0,
            TransferToUserId = toUserId,
            NodeVisitId = nodeVisitId,
        }).ExecuteCommandAsync(cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.AdminForceTransferred,
            node.Id,
            new
            {
                nodeId = node.Id,
                nodeName,
                action = "forceTransfer",
                reason = text,
                comment = text,
                toUserId,
                adminUserId = ctx.ActorUserId,
            },
            cancellationToken);
    }

    private static DateTime? ResolveDueTime(WfExecutionContext ctx, WfNode node)
    {
        if (node.Props?.Timeout is not { } timeout || timeout.Hours <= 0)
            return null;
        var hours = Math.Min(timeout.Hours, MaxTimeoutHours);
        return ctx.TimeProvider.GetLocalNow().DateTime.AddHours(hours);
    }
}

/// <summary>
/// 管理员终止。运行中的实例都能停,包括已经有人同意过的(发起人撤销不允许那种情况)。
/// 状态是 Terminated,清掉活跃待办,通知发起人完结,并通知原待办人刷新。
/// </summary>
public class AdminTerminateOp(string? reason) : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = AdminIntervention.RequireReason(reason);

        await ctx.ClaimInstanceAsync(WfInstanceStatus.Running, cancellationToken);

        var tasks = await ctx.Db.Queryable<WfTask>()
            .Where(t => t.InstanceId == ctx.Instance.Id)
            .ToListAsync(cancellationToken);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var pending = taskIds.Count == 0
            ? []
            : await ctx.Db.Queryable<WfTaskActor>()
                .Where(a => taskIds.Contains(a.TaskId)
                            && a.ActorType == WfActorType.Approver
                            && (a.Status == WfActorStatus.Pending || a.Status == WfActorStatus.Waiting))
                .ToListAsync(cancellationToken);

        await ctx.WriteInstanceTerminalStatusAsync(WfInstanceStatus.Terminated, cancellationToken);
        await ParallelControlOps.CancelInstanceRuntimeAsync(ctx, "adminTerminated", cancellationToken);

        var node = ctx.FindNode(ctx.Token.NodeId);
        var nodeName = node is null ? ctx.Token.NodeId : AdminIntervention.NodeName(node);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = ctx.Instance.Id,
            NodeId = ctx.Token.NodeId,
            NodeName = nodeName,
            TokenId = ctx.Token.Id,
            UserId = ctx.ActorUserId ?? 0,
            Action = WfTaskAction.AdminTerminate,
            Comment = text,
            DurationMs = 0,
            NodeVisitId = ctx.Token.NodeVisitId,
        }).ExecuteCommandAsync(cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceCompleted,
            ctx.Token.NodeId,
            new
            {
                status = WfInstanceStatus.Terminated.ToString(),
                reason = text,
                comment = text,
                nodeId = ctx.Token.NodeId,
                nodeName,
                adminUserId = ctx.ActorUserId,
            },
            cancellationToken);

        await ctx.FormBinder.OnInstanceCompletedAsync(
            new WfFormBindContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                VariablesJson = ctx.Instance.VariablesJson,
                Status = WfInstanceStatus.Terminated,
                StarterUserId = ctx.Instance.StarterUserId,
            },
            cancellationToken);

        ctx.PendingInstanceCompletedNotification = new WfNotifyContext
        {
            InstanceId = ctx.Instance.Id,
            DefinitionVersionId = ctx.Instance.DefinitionVersionId,
            BusinessKey = ctx.Instance.BusinessKey,
            NodeId = ctx.Token.NodeId,
            NodeName = nodeName,
            StarterUserId = ctx.Instance.StarterUserId,
            Status = WfInstanceStatus.Terminated,
        };

        var recalled = new WfNotifyContext
        {
            InstanceId = ctx.Instance.Id,
            DefinitionVersionId = ctx.Instance.DefinitionVersionId,
            BusinessKey = ctx.Instance.BusinessKey,
            NodeId = ctx.Token.NodeId,
            NodeName = nodeName,
            StarterUserId = ctx.Instance.StarterUserId,
            Status = WfInstanceStatus.Terminated,
        };
        foreach (var group in pending.GroupBy(a => a.TaskId))
        {
            var users = group.Select(a => a.UserId).Distinct().ToList();
            if (users.Count == 0) continue;
            ctx.PendingTaskRecalledNotifications.Add((recalled, group.Key, users));
        }
    }
}

/// <summary>
/// 跳过阻断节点。只接受审批节点且当前 token 上没有待办;
/// 有待办说明还有人能批,不能由管理员直接往后推。
/// </summary>
public class AdminSkipNodeOp(string? reason) : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = AdminIntervention.RequireReason(reason);

        var node = ctx.FindNode(ctx.Token.NodeId)
                   ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                       new Dictionary<string, object?> { ["nodeId"] = ctx.Token.NodeId });
        if (node.Type != WfNodeType.Approval)
            throw AdminIntervention.Denied("notApprovalNode");

        var hasTask = await ctx.Db.Queryable<WfTask>()
            .Where(t => t.InstanceId == ctx.Instance.Id && t.TokenId == ctx.Token.Id)
            .AnyAsync(cancellationToken);
        if (hasTask)
            throw AdminIntervention.Denied("taskStillOpen");

        ctx.CurrentNode = node;
        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);

        var nodeName = AdminIntervention.NodeName(node);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = ctx.Instance.Id,
            NodeId = node.Id,
            NodeName = nodeName,
            TokenId = ctx.Token.Id,
            UserId = ctx.ActorUserId ?? 0,
            Action = WfTaskAction.AdminSkip,
            Comment = text,
            DurationMs = 0,
            NodeVisitId = ctx.Token.NodeVisitId,
        }).ExecuteCommandAsync(cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.AdminSkipped,
            node.Id,
            new
            {
                nodeId = node.Id,
                nodeName,
                action = "skip",
                reason = text,
                comment = text,
                adminUserId = ctx.ActorUserId,
            },
            cancellationToken);

        ctx.Agenda.Plan(new TakeTransitionOp(node, ctx.Token));
    }
}
