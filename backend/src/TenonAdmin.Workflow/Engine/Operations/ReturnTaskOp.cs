using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 主动退回(M2b):CAS 认领当前办理人 → 按节点 <see cref="WfReturnPolicy"/> 解析目标节点 → 关闭当前活跃任务
/// → token 回退到目标节点。<b>不</b> plan 任何后续 Op——Agenda 留空,等发起人重提
/// (<see cref="WorkflowEngine"/> 里的 <c>ResubmitInstanceCmd</c>)。校验已在
/// <see cref="WorkflowEngine.BeginReturnAsync"/> 做完(实例存在且 Running)。
/// </summary>
public class ReturnTaskOp(
    WfTask task,
    long userId,
    string? targetNodeId,
    string? comment) : IWfOperation
{
    protected WfTask Task { get; } = task;
    protected long UserId { get; } = userId;
    protected string? TargetNodeId { get; } = targetNodeId;
    protected string? Comment { get; } = comment;

    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        // 仅当前 Pending 办理人可退回(顺序会签的 Waiting 后手不可)。
        var claimed = await ctx.Db.Updateable<WfTaskActor>()
            .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
            .Where(a => a.TaskId == Task.Id && a.UserId == UserId && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
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

        var resolvedTargetId = await ResolveTargetNodeIdAsync(ctx, node, cancellationToken);
        var target = ctx.FindNode(resolvedTargetId)
                     ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                         new Dictionary<string, object?> { ["nodeId"] = resolvedTargetId });

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
            Action = WfTaskAction.Return,
            Comment = Comment,
            DurationMs = durationMs,
        }).ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskCompleted,
            Task.NodeId,
            new { taskId = Task.Id, userId = UserId, action = "Return", targetNodeId = target.Id },
            cancellationToken);

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TaskReturned,
            Task.NodeId,
            new { fromNodeId = Task.NodeId, targetNodeId = target.Id },
            cancellationToken);

        // 关闭当前活跃任务:全部 actor(不限 Pending——顺序会签的 Waiting 后手也要清)标 Skipped → 物理删
        // actor 行 → 物理删 task 行(转历史已写入 wf_his_task,同 CompleteTaskOp.CloseTaskAsync 的三步)。
        await ctx.Db.Updateable<WfTaskActor>()
            .SetColumns(a => new WfTaskActor { Status = WfActorStatus.Skipped })
            .Where(a => a.TaskId == Task.Id)
            .ExecuteCommandAsync();
        await ctx.Db.Deleteable<WfTaskActor>().Where(a => a.TaskId == Task.Id).ExecuteCommandAsync();
        await ctx.Db.Deleteable<WfTask>().In(Task.Id).ExecuteCommandAsync();

        // token 回退;Status 不变(仍 Active——退回后原实例保持 Running,不是完结),故领取的期望状态与
        // 目标状态都是 Active,双条件 CAS 只推进版本。顺序照旧「先抢任务、再动 token」:上面那段任务级
        // CAS 仍是本 Op 的第一个写操作。
        // ⚠「第一个写操作」只在**本 Op 内**成立,跨事务不成立(退回自己的 wf_his_task 与 wf_history 都
        // 写在本次 token 领取之前);安全性由整事务回滚兜底,不由语句顺序保证。
        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
        ctx.Token.NodeId = target.Id;
        await ctx.Db.Updateable(ctx.Token)
            .UpdateColumns(t => new { t.NodeId, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        // 不 plan 任何 Op——Agenda 留空,引擎自然停,等发起人重提。
    }

    /// <summary>
    /// 按当前节点 <see cref="WfNodeProps.ReturnPolicy"/> 解析目标节点 Id:
    /// <see cref="WfReturnPolicy.Node"/> 用节点固定配置;<see cref="WfReturnPolicy.Prev"/> 查本 token
    /// 最近一条 Approve 历史所在节点,无先例(链上第一个审批节点)退化到 <c>start</c>;
    /// <see cref="WfReturnPolicy.Any"/> 用调用方指定目标,须是本实例真正走过的节点。
    /// </summary>
    protected virtual async Task<string> ResolveTargetNodeIdAsync(
        WfExecutionContext ctx,
        WfNode node,
        CancellationToken cancellationToken)
    {
        switch (node.Props?.ReturnPolicy)
        {
            case WfReturnPolicy.Node:
                var configured = node.Props!.ReturnToNodeId;
                if (string.IsNullOrWhiteSpace(configured))
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.ReturnNotAllowed,
                        new Dictionary<string, object?> { ["reason"] = "targetNotConfigured" });
                }
                return configured;

            case WfReturnPolicy.Prev:
                // InstanceId 打头命中 idx_wf_his_task_instance(wf_his_task 无 TokenId 索引,只按 TokenId
                // 过滤是在永不清理的表上做无索引扫描);NodeId 排除当前节点自身——CompleteTaskOp 在计票前
                // 就插 WfHisTask,故会签/顺序会签下第一位 Approve 后任务仍开着,第二位调退回会命中自己。
                var lastApproved = await ctx.Db.Queryable<WfHisTask>()
                    .Where(h => h.InstanceId == ctx.Instance.Id && h.TokenId == Task.TokenId
                                && h.Action == WfTaskAction.Approve && h.NodeId != Task.NodeId)
                    .OrderBy(h => h.Id, OrderByType.Desc)
                    .FirstAsync();
                return lastApproved?.NodeId ?? ctx.Model.Root.Id;

            case WfReturnPolicy.Any:
                if (string.IsNullOrWhiteSpace(TargetNodeId) || ctx.FindNode(TargetNodeId) is null)
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.ReturnNotAllowed,
                        new Dictionary<string, object?> { ["reason"] = "invalidTarget" });
                }

                var walkedNodeIds = await ctx.Db.Queryable<WfHistory>()
                    .Where(h => h.InstanceId == ctx.Instance.Id && h.EventType == WfHistoryEventType.NodeEnter)
                    .Select(h => h.NodeId)
                    .ToListAsync();
                if (!walkedNodeIds.Contains(TargetNodeId))
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.ReturnNotAllowed,
                        new Dictionary<string, object?> { ["reason"] = "invalidTarget" });
                }
                return TargetNodeId;

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ReturnNotAllowed,
                    new Dictionary<string, object?> { ["reason"] = "policyNotConfigured" });
        }
    }
}
