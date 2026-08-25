using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 任务级转办(M1):CAS 跳过当前办理人 → 写历史 → 挂上目标 Pending actor。
/// 不推进 token、不删待办;Agenda 自然空停。
/// <para>M2b 的委托(<see cref="DelegateTaskOp"/>)机制与转办同构,只换 <see cref="HistoryAction"/>
/// 与 <see cref="TargetInvalidErrorCode"/> 两个语义钩子,故继承本类而不复制整段动作序列。</para>
/// </summary>
public class TransferTaskOp(
    WfTask task,
    long userId,
    long toUserId,
    string? comment) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected long ToUserId { get; } = toUserId;
    protected string? Comment { get; } = comment;

    /// <summary>落进 <c>wf_his_task.Action</c> 与事件流 payload 的动作标签。</summary>
    protected virtual WfTaskAction HistoryAction => WfTaskAction.Transfer;

    /// <summary>目标用户不可用 / 已是办理人 / 是自己时抛的业务码。</summary>
    protected virtual int TargetInvalidErrorCode => WorkflowErrorCode.TransferTargetInvalid;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ToUserId <= 0 || ToUserId == UserId)
        {
            throw WorkflowErrorCode.Exception(TargetInvalidErrorCode,
                new Dictionary<string, object?> { ["toUserId"] = ToUserId });
        }

        var targetEnabled = await ctx.Db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == ToUserId && u.Enabled)
            .AnyAsync();
        if (!targetEnabled)
        {
            throw WorkflowErrorCode.Exception(TargetInvalidErrorCode,
                new Dictionary<string, object?> { ["toUserId"] = ToUserId, ["reason"] = "userUnavailable" });
        }

        // 目标已是本待办任一 Approver → 拒绝(避免重复 Pending / 搅乱顺序签)。
        var targetExists = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == ToUserId && a.ActorType == WfActorType.Approver)
            .AnyAsync();
        if (targetExists)
        {
            throw WorkflowErrorCode.Exception(TargetInvalidErrorCode,
                new Dictionary<string, object?> { ["toUserId"] = ToUserId, ["reason"] = "alreadyActor" });
        }

        // CAS:仅 Pending 办理人可转出;两副本同点只有一个成功。
        var fromActor = await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == Task.Id && a.UserId == UserId && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
            .FirstAsync();
        if (fromActor is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = Task.Id });
        }

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

        var claimed = await ctx.Db.Updateable<WfTaskActor>()
            .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
            .Where(a => a.Id == fromActor.Id && a.Status == WfActorStatus.Pending)
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

        var durationMs = Math.Max(
            0,
            (long)(ctx.TimeProvider.GetLocalNow().DateTime - Task.CreateTime).TotalMilliseconds);
        await ctx.Db.Insertable(new WfHisTask
        {
            InstanceId = Task.InstanceId,
            NodeId = Task.NodeId,
            NodeName = node.Name,
            TaskId = Task.Id,
            TokenId = Task.TokenId,
            UserId = UserId,
            Action = HistoryAction,
            Comment = Comment,
            DurationMs = durationMs,
            TransferToUserId = ToUserId,
        }).ExecuteCommandAsync();

        await ctx.Db.Insertable(new WfTaskActor
        {
            TaskId = Task.Id,
            UserId = ToUserId,
            ActorType = WfActorType.Approver,
            Status = WfActorStatus.Pending,
            Sort = fromActor.Sort,
        }).ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCompleted,
            Task.NodeId,
            new
            {
                taskId = Task.Id,
                userId = UserId,
                action = HistoryAction.ToString(),
                toUserId = ToUserId,
            },
            cancellationToken);

        // 通知用:新办理人(待办本身未换 Id)。
        ctx.NewAssigneeUserIds.Add(ToUserId);

        // 通知排队,事务提交后由 WorkflowEngine 统一派发。
        ctx.PendingTaskAssignedNotifications.Add((
            new WfNotifyContext
            {
                InstanceId = ctx.Instance.Id,
                DefinitionVersionId = ctx.Instance.DefinitionVersionId,
                BusinessKey = ctx.Instance.BusinessKey,
                NodeId = Task.NodeId,
                NodeName = node.Name,
                StarterUserId = ctx.Instance.StarterUserId,
                Status = ctx.Instance.Status,
            },
            [ToUserId]));
    }
}
