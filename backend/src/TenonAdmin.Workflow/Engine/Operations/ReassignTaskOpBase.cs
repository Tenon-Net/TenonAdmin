using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 任务级「改派」的公共动作序列:校验目标 → CAS 认领当前 Pending 办理人 → 写 <c>wf_his_task</c> →
/// 挂上目标 Pending actor → 写 <c>wf_history</c> → 排队通知。把当前这一件待办从 X 挪到 Y,
/// **不推进 token、不删待办**;Agenda 自然空停。
/// <para><b>两个子类是兄弟、语义平级,不是父子</b>:<see cref="TransferTaskOp"/> = 转办(把活儿交出去,
/// 责任转移);<see cref="DelegateTaskOp"/> = 委托(请人代办,委托人在 <c>wf_his_task</c> 里留一行,
/// 与被委托人后续的办理动作各自成行)。两者走独立端点是有意的——本仓权限码即路由,合并端点等于让
/// 「可转办」与「可委托」永远只能一起授权。差异全部收敛到 <see cref="HistoryAction"/> 与
/// <see cref="TargetInvalidErrorCode"/> 两个 <c>abstract</c> 钩子:基类对「自己是哪个动词」不持意见,
/// 将来第三个兄弟忘了声明是编译失败,而不是静默记成转办。</para>
/// <para><b>不是向后跳转</b>:改派不改 token 所在节点,故 <see cref="WfTaskAction.Transfer"/> 与
/// <see cref="WfTaskAction.Delegate"/> 都**不进** <c>EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync</c>
/// 的跳转下界白名单——加进去会让改派误重置「同一人相邻节点去重」的基线。</para>
/// </summary>
public abstract class ReassignTaskOpBase(
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
    protected abstract WfTaskAction HistoryAction { get; }

    /// <summary>目标用户不可用 / 已是办理人 / 是自己时抛的业务码。</summary>
    protected abstract int TargetInvalidErrorCode { get; }

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
