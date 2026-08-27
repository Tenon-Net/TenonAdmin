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

        // ⚠ 这段任务级 CAS 是转办与委托**全部**并发安全性的唯一锚点,不是冗余,删不得也放松不得。
        // 它在任何 actor 行被改动之前抢到任务级独占,后面的「原 actor 翻 Skipped」「插新 actor」
        // 「插 wf_his_task」才不会两副本各做一遍。
        // Task 9 给实例与 token 各加了 Version 并把状态推进收口到「期望状态 + 版本」双条件 CAS,但那一层
        // 对本路径**不构成任何保护**:改派压根不改实例状态、不改 token(节点没变、状态没变),两个并发
        // 委托同一件待办时实例与 token 一字不动,新 CAS 拦不住,后果是两行 Pending actor + 两条 Delegate
        // 历史。反过来也不能给改派加实例级 CAS —— 那会让同实例上两件**不同**待办的并发委托互相冲突,
        // 属过度加锁。两个方向都由 WfVersionCasTests.Reassign_claims_task_version_only_and_leaves_
        // instance_and_token_untouched 钉住:删掉本段 → 任务版本不前进;加实例级 CAS → 实例版本前进。
        //
        // ⚠ 那么「实例已 Cancelled 但改派仍成功」今天靠什么挡住?**不是**本段任务级 CAS,而是一条此前
        // 没有任何地方写下来的隐式不变量:**每一个把实例改成终态的动作都会物理删掉那一行活跃 wf_task**
        // (CancelInstanceOp 撤销、CompleteTaskOp.CloseTaskAsync 同意/拒绝、ReturnTaskOp 退回)。撤销先
        // 提交时,本段 CAS 打在一行**已被删除**的记录上 → 影响 0 行 → TaskConflict → 整事务回滚。
        // 换句话说跨层一致性来自「终态动作必删待办」,不来自任何版本列。**M3 一旦出现「不删待办的实例级
        // 动作」(挂起 / 终止 / 并行网关下只收某一分支),这条不变量静默失效**,届时改派必须自己加一道
        // 实例状态复验(在事务内,不能只靠 BeginXxxAsync 的那次读),否则会出现「实例已挂起、待办还能
        // 被转手」。新增任何实例级动作时先回头看这段。
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
