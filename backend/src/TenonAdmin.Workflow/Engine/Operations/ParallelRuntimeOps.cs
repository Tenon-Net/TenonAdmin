using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>并行节点一次访问的 fork：停泊父 token，并为非空臂创建子 token。</summary>
internal class ForkParallelOp(WfNode parallel, WfToken parentToken) : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ctx.Token = parentToken;

        var arms = parallel.ParallelArms;
        if (arms is not { Count: >= 2 } || parentToken.NodeVisitId is not { } parentNodeVisitId)
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "parallelRuntimeInvalid", ["nodeId"] = parallel.Id });
        }

        var forkId = ctx.IdGenerator.NextId();
        var activeArmCount = arms.Count(arm => arm.Next is not null);

        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
        parentToken.Status = WfTokenStatus.WaitingJoin;
        parentToken.ForkId = forkId;
        parentToken.PendingArmCount = activeArmCount;
        await ctx.Db.Updateable(parentToken)
            .UpdateColumns(t => new { t.Status, t.ForkId, t.PendingArmCount, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        var rows = new List<WfParallelArm>(arms.Count);
        var entries = new List<(WfNode Node, long NodeVisitId, WfToken Token)>();
        foreach (var arm in arms)
        {
            WfToken? child = null;
            long? childEntryNodeVisitId = null;
            if (arm.Next is not null)
            {
                childEntryNodeVisitId = ctx.IdGenerator.NextId();
                child = new WfToken
                {
                    InstanceId = ctx.Instance.Id,
                    NodeId = arm.Next.Id,
                    Status = WfTokenStatus.Active,
                    ParentTokenId = parentToken.Id,
                    ForkId = forkId,
                };
                await ctx.Db.Insertable(child).ExecuteCommandAsync();
                entries.Add((arm.Next, childEntryNodeVisitId.Value, child));
            }

            rows.Add(new WfParallelArm
            {
                ForkId = forkId,
                ArmId = arm.Id,
                ParentTokenId = parentToken.Id,
                ChildTokenId = child?.Id,
                Status = child is null ? WfParallelArmStatus.Completed : WfParallelArmStatus.Active,
                ParentNodeVisitId = parentNodeVisitId,
                ChildEntryNodeVisitId = childEntryNodeVisitId,
                Reason = child is null ? "empty" : null,
            });
        }

        await ctx.Db.Insertable(rows).ExecuteCommandAsync();
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.ParallelFork,
            parallel.Id,
            new
            {
                eventName = "fork",
                forkId,
                armId = (string?)null,
                parentTokenId = parentToken.Id,
                childTokenId = (long?)null,
                parentNodeVisitId,
                childEntryNodeVisitId = (long?)null,
                status = WfTokenStatus.WaitingJoin.ToString(),
                reason = (string?)null,
                arms = rows.Select(row => new
                {
                    row.ArmId,
                    row.ChildTokenId,
                    row.ChildEntryNodeVisitId,
                    status = row.Status.ToString(),
                    row.Reason,
                }),
            },
            cancellationToken);

        foreach (var row in rows.Where(row => row.Status == WfParallelArmStatus.Completed))
        {
            await ctx.AppendHistoryAsync(
                WfHistoryEventType.ParallelArmCompleted,
                parallel.Id,
                new
                {
                    eventName = "armComplete",
                    forkId,
                    row.ArmId,
                    parentTokenId = parentToken.Id,
                    childTokenId = (long?)null,
                    parentNodeVisitId,
                    childEntryNodeVisitId = (long?)null,
                    status = row.Status.ToString(),
                    row.Reason,
                },
                cancellationToken);
        }

        if (activeArmCount == 0)
        {
            await JoinAsync(ctx, parentToken, parallel, forkId, parentNodeVisitId, cancellationToken);
            return;
        }

        foreach (var entry in entries)
            ctx.Agenda.Plan(new EnterNodeOp(entry.Node, entry.NodeVisitId, entry.Token));
    }

    internal static async Task JoinAsync(
        WfExecutionContext ctx,
        WfToken parent,
        WfNode parallel,
        long forkId,
        long parentNodeVisitId,
        CancellationToken cancellationToken)
    {
        var currentVersion = parent.Version;
        var nextVersion = currentVersion + 1;
        var updated = await ctx.Db.Updateable<WfToken>()
            .SetColumns(t => new WfToken
            {
                Status = WfTokenStatus.Active,
                ForkId = null,
                PendingArmCount = 0,
                Version = nextVersion,
                UpdateTime = ctx.TimeProvider.GetLocalNow().DateTime,
                UpdateUserId = ctx.ActorUserId,
            })
            .Where(t => t.Id == parent.Id
                        && t.Status == WfTokenStatus.WaitingJoin
                        && t.ForkId == forkId
                        && t.PendingArmCount == 0
                        && t.Version == currentVersion)
            .ExecuteCommandAsync();
        if (updated != 1)
            ThrowConflict("parallelJoinConflict", parent.Id);

        parent.Status = WfTokenStatus.Active;
        parent.ForkId = null;
        parent.PendingArmCount = 0;
        parent.Version = nextVersion;
        ctx.Token = parent;
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.ParallelJoined,
            parallel.Id,
            new
            {
                eventName = "join",
                forkId,
                armId = (string?)null,
                parentTokenId = parent.Id,
                childTokenId = (long?)null,
                parentNodeVisitId,
                childEntryNodeVisitId = (long?)null,
                status = WfTokenStatus.Active.ToString(),
                reason = (string?)null,
            },
            cancellationToken);
        ctx.Agenda.Plan(new TakeTransitionOp(parallel, parent));
    }

    internal static void ThrowConflict(string reason, long tokenId) =>
        throw WorkflowErrorCode.Exception(
            WorkflowErrorCode.InstanceStatusConflict,
            new Dictionary<string, object?> { ["reason"] = reason, ["tokenId"] = tokenId });
}

/// <summary>并行臂到达末端：依次 CAS 子 token、arm 和父 token，最后一臂负责 join。</summary>
internal class CompleteParallelArmOp(
    WfNode parallel,
    WfParallelArmDefinition armDefinition,
    WfToken childToken) : IWfOperation
{
    public virtual async Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ctx.Token = childToken;
        var forkId = childToken.ForkId
                     ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);
        var parentTokenId = childToken.ParentTokenId
                            ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var arm = await ctx.Db.Queryable<WfParallelArm>()
            .Where(row => row.ForkId == forkId
                          && row.ArmId == armDefinition.Id
                          && row.ChildTokenId == childToken.Id)
            .FirstAsync();
        if (arm is null)
            ForkParallelOp.ThrowConflict("parallelArmNotFound", childToken.Id);

        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);
        childToken.Status = WfTokenStatus.Completed;
        await ctx.Db.Updateable(childToken)
            .UpdateColumns(t => new { t.Status, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        var armVersion = arm!.Version;
        var armUpdated = await ctx.Db.Updateable<WfParallelArm>()
            .SetColumns(row => new WfParallelArm
            {
                Status = WfParallelArmStatus.Completed,
                Version = armVersion + 1,
                Reason = "completed",
            })
            .Where(row => row.ForkId == forkId
                          && row.ArmId == armDefinition.Id
                          && row.ChildTokenId == childToken.Id
                          && row.Status == WfParallelArmStatus.Active
                          && row.Version == armVersion)
            .ExecuteCommandAsync();
        if (armUpdated != 1)
            ForkParallelOp.ThrowConflict("parallelArmVersionConflict", childToken.Id);

        var parent = await ctx.Db.Queryable<WfToken>()
            .Where(token => token.Id == parentTokenId)
            .FirstAsync();
        if (parent?.PendingArmCount is not > 0)
            ForkParallelOp.ThrowConflict("parallelParentNotWaiting", parentTokenId);

        var pendingBefore = parent!.PendingArmCount.GetValueOrDefault();
        var pendingAfter = pendingBefore - 1;
        var parentVersion = parent.Version;
        var parentUpdated = await ctx.Db.Updateable<WfToken>()
            .SetColumns(token => new WfToken
            {
                Status = pendingAfter == 0 ? WfTokenStatus.Active : WfTokenStatus.WaitingJoin,
                ForkId = pendingAfter == 0 ? null : forkId,
                PendingArmCount = pendingAfter,
                Version = parentVersion + 1,
                UpdateTime = ctx.TimeProvider.GetLocalNow().DateTime,
                UpdateUserId = ctx.ActorUserId,
            })
            .Where(token => token.Id == parentTokenId
                            && token.Status == WfTokenStatus.WaitingJoin
                            && token.ForkId == forkId
                            && token.PendingArmCount == pendingBefore
                            && token.Version == parentVersion)
            .ExecuteCommandAsync();
        if (parentUpdated != 1)
            ForkParallelOp.ThrowConflict("parallelParentVersionConflict", parentTokenId);

        parent.Status = pendingAfter == 0 ? WfTokenStatus.Active : WfTokenStatus.WaitingJoin;
        parent.ForkId = pendingAfter == 0 ? null : forkId;
        parent.PendingArmCount = pendingAfter;
        parent.Version = parentVersion + 1;
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.ParallelArmCompleted,
            parallel.Id,
            new
            {
                eventName = "armComplete",
                forkId,
                armId = armDefinition.Id,
                parentTokenId,
                childTokenId = childToken.Id,
                arm.ParentNodeVisitId,
                arm.ChildEntryNodeVisitId,
                status = WfParallelArmStatus.Completed.ToString(),
                reason = "completed",
            },
            cancellationToken);

        if (pendingAfter != 0)
            return;

        ctx.Token = parent;
        await ctx.AppendHistoryAsync(
            WfHistoryEventType.ParallelJoined,
            parallel.Id,
            new
            {
                eventName = "join",
                forkId,
                armId = armDefinition.Id,
                parentTokenId,
                childTokenId = childToken.Id,
                arm.ParentNodeVisitId,
                arm.ChildEntryNodeVisitId,
                status = WfTokenStatus.Active.ToString(),
                reason = "lastArm",
            },
            cancellationToken);
        ctx.Agenda.Plan(new TakeTransitionOp(parallel, parent));
    }
}

internal static class ParallelControlOps
{
    internal static async Task CancelInstanceRuntimeAsync(
        WfExecutionContext ctx,
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokens = await ctx.Db.Queryable<WfToken>()
            .Where(token => token.InstanceId == ctx.Instance.Id
                            && (token.Status == WfTokenStatus.Active
                                || token.Status == WfTokenStatus.WaitingJoin))
            .ToListAsync(cancellationToken);
        var parentById = tokens.ToDictionary(token => token.Id);
        var arms = tokens.Count == 0
            ? []
            : await ctx.Db.Queryable<WfParallelArm>()
                .Where(arm => tokens.Select(token => token.Id).Contains(arm.ParentTokenId)
                              && arm.Status == WfParallelArmStatus.Active)
                .ToListAsync(cancellationToken);

        var executionNowUtc = ctx.TimeProvider.GetUtcNow().UtcDateTime;
        var localNow = ctx.TimeProvider.GetLocalNow().DateTime;
        await ctx.Db.Updateable<WfNodeExecution>()
            .SetColumns(execution => new WfNodeExecution
            {
                Status = WfNodeExecutionStatus.Cancelled,
                Fence = execution.Fence + 1,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                NextRetryAtUtc = null,
                CompletedTimeUtc = executionNowUtc,
            })
            .Where(execution => execution.InstanceId == ctx.Instance.Id
                                && (execution.Status == WfNodeExecutionStatus.Pending
                                    || execution.Status == WfNodeExecutionStatus.RetryScheduled
                                    || execution.Status == WfNodeExecutionStatus.Running))
            .ExecuteCommandAsync(cancellationToken);

        foreach (var arm in arms)
        {
            var updated = await ctx.Db.Updateable<WfParallelArm>()
                .SetColumns(row => new WfParallelArm
                {
                    Status = WfParallelArmStatus.Cancelled,
                    Version = row.Version + 1,
                    Reason = reason,
                })
                .Where(row => row.ForkId == arm.ForkId
                              && row.ArmId == arm.ArmId
                              && row.Status == WfParallelArmStatus.Active
                              && row.Version == arm.Version)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1)
                ForkParallelOp.ThrowConflict("parallelArmVersionConflict", arm.ParentTokenId);

            if (parentById.TryGetValue(arm.ParentTokenId, out var parent))
            {
                ctx.Token = parent;
                await ctx.AppendHistoryAsync(
                    WfHistoryEventType.ParallelCancelled,
                    parent.NodeId,
                    new
                    {
                        eventName = "cancel",
                        arm.ForkId,
                        arm.ArmId,
                        parentTokenId = arm.ParentTokenId,
                        childTokenId = arm.ChildTokenId,
                        arm.ParentNodeVisitId,
                        arm.ChildEntryNodeVisitId,
                        status = WfParallelArmStatus.Cancelled.ToString(),
                        reason,
                    },
                    cancellationToken);
            }
        }

        var taskIds = await ctx.Db.Queryable<WfTask>()
            .Where(task => task.InstanceId == ctx.Instance.Id)
            .Select(task => task.Id)
            .ToListAsync(cancellationToken);
        if (taskIds.Count > 0)
        {
            var actorIds = await ctx.Db.Queryable<WfTaskActor>()
                .Where(actor => taskIds.Contains(actor.TaskId)
                                && (actor.Status == WfActorStatus.Pending
                                    || actor.Status == WfActorStatus.Waiting))
                .Select(actor => actor.Id)
                .ToListAsync(cancellationToken);
            if (actorIds.Count > 0)
            {
                await ctx.Db.Updateable<WfTaskActor>()
                    .SetColumns(actor => new WfTaskActor { Status = WfActorStatus.Skipped })
                    .Where(actor => actorIds.Contains(actor.Id)
                                    && (actor.Status == WfActorStatus.Pending
                                        || actor.Status == WfActorStatus.Waiting))
                    .ExecuteCommandAsync(cancellationToken);
            }
            await ctx.Db.Deleteable<WfTask>().In(taskIds).ExecuteCommandAsync();
        }

        foreach (var token in tokens)
        {
            var updated = await ctx.Db.Updateable<WfToken>()
                .SetColumns(row => new WfToken
                {
                    Status = WfTokenStatus.Cancelled,
                    Version = row.Version + 1,
                    UpdateTime = localNow,
                    UpdateUserId = ctx.ActorUserId,
                })
                .Where(row => row.Id == token.Id
                              && row.Status == token.Status
                              && row.Version == token.Version)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1)
                ForkParallelOp.ThrowConflict("parallelTokenVersionConflict", token.Id);

            token.Status = WfTokenStatus.Cancelled;
            token.Version++;
        }

        if (tokens.Count > 0)
            ctx.Token = tokens.FirstOrDefault(token => token.ParentTokenId is null) ?? tokens[0];
    }

    internal static async Task ReturnForkAsync(
        WfExecutionContext ctx,
        string returnTargetNodeId,
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var child = ctx.Token;
        var forkId = child.ForkId
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);
        var parentId = child.ParentTokenId
                       ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);
        var parent = await ctx.Db.Queryable<WfToken>()
            .Where(token => token.Id == parentId
                            && token.Status == WfTokenStatus.WaitingJoin
                            && token.ForkId == forkId)
            .FirstAsync(cancellationToken);
        if (parent is null)
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.InstanceStatusConflict,
                new Dictionary<string, object?> { ["reason"] = "parallelParentNotWaiting", ["tokenId"] = parentId });
        }

        var forkTokens = await ctx.Db.Queryable<WfToken>()
            .Where(token => token.InstanceId == ctx.Instance.Id
                            && (token.Id == parentId || token.ForkId == forkId))
            .ToListAsync(cancellationToken);
        var childTokens = forkTokens.Where(token => token.Id != parentId).ToList();
        var childTokenIds = childTokens.Select(token => token.Id).ToList();
        var arms = await ctx.Db.Queryable<WfParallelArm>()
            .Where(arm => arm.ForkId == forkId && arm.Status == WfParallelArmStatus.Active)
            .ToListAsync(cancellationToken);

        var nowUtc = ctx.TimeProvider.GetUtcNow().UtcDateTime;
        var localNow = ctx.TimeProvider.GetLocalNow().DateTime;
        if (childTokenIds.Count > 0)
        {
            await ctx.Db.Updateable<WfNodeExecution>()
                .SetColumns(execution => new WfNodeExecution
                {
                    Status = WfNodeExecutionStatus.Cancelled,
                    Fence = execution.Fence + 1,
                    LeaseOwner = null,
                    LeaseExpiresAtUtc = null,
                    NextRetryAtUtc = null,
                    CompletedTimeUtc = nowUtc,
                })
                .Where(execution => childTokenIds.Contains(execution.TokenId)
                                    && (execution.Status == WfNodeExecutionStatus.Pending
                                        || execution.Status == WfNodeExecutionStatus.RetryScheduled
                                        || execution.Status == WfNodeExecutionStatus.Running))
                .ExecuteCommandAsync(cancellationToken);

            var taskIds = await ctx.Db.Queryable<WfTask>()
                .Where(task => childTokenIds.Contains(task.TokenId))
                .Select(task => task.Id)
                .ToListAsync(cancellationToken);
            if (taskIds.Count > 0)
            {
                var actorIds = await ctx.Db.Queryable<WfTaskActor>()
                    .Where(actor => taskIds.Contains(actor.TaskId)
                                    && (actor.Status == WfActorStatus.Pending
                                        || actor.Status == WfActorStatus.Waiting))
                    .Select(actor => actor.Id)
                    .ToListAsync(cancellationToken);
                if (actorIds.Count > 0)
                {
                    await ctx.Db.Updateable<WfTaskActor>()
                        .SetColumns(actor => new WfTaskActor { Status = WfActorStatus.Skipped })
                        .Where(actor => actorIds.Contains(actor.Id)
                                        && (actor.Status == WfActorStatus.Pending
                                            || actor.Status == WfActorStatus.Waiting))
                        .ExecuteCommandAsync(cancellationToken);
                }
                await ctx.Db.Deleteable<WfTask>().In(taskIds).ExecuteCommandAsync();
            }
        }

        foreach (var arm in arms)
        {
            var updated = await ctx.Db.Updateable<WfParallelArm>()
                .SetColumns(row => new WfParallelArm
                {
                    Status = WfParallelArmStatus.Cancelled,
                    Version = row.Version + 1,
                    Reason = reason,
                })
                .Where(row => row.ForkId == arm.ForkId
                              && row.ArmId == arm.ArmId
                              && row.Status == WfParallelArmStatus.Active
                              && row.Version == arm.Version)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1)
                ForkParallelOp.ThrowConflict("parallelArmVersionConflict", child.Id);

            ctx.Token = parent;
            await ctx.AppendHistoryAsync(
                WfHistoryEventType.ParallelCancelled,
                parent.NodeId,
                new
                {
                    eventName = "cancel",
                    arm.ForkId,
                    arm.ArmId,
                    parentTokenId = parent.Id,
                    arm.ChildTokenId,
                    arm.ParentNodeVisitId,
                    arm.ChildEntryNodeVisitId,
                    status = WfParallelArmStatus.Cancelled.ToString(),
                    reason,
                },
                cancellationToken);
        }

        foreach (var token in childTokens)
        {
            if (token.Status is not (WfTokenStatus.Active or WfTokenStatus.WaitingJoin))
                continue;
            var updated = await ctx.Db.Updateable<WfToken>()
                .SetColumns(row => new WfToken
                {
                    Status = WfTokenStatus.Cancelled,
                    Version = row.Version + 1,
                    UpdateTime = localNow,
                    UpdateUserId = ctx.ActorUserId,
                })
                .Where(row => row.Id == token.Id
                              && row.Status == token.Status
                              && row.Version == token.Version)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1)
                ForkParallelOp.ThrowConflict("parallelTokenVersionConflict", token.Id);
            token.Status = WfTokenStatus.Cancelled;
            token.Version++;
        }

        var parentUpdated = await ctx.Db.Updateable<WfToken>()
            .SetColumns(row => new WfToken
            {
                Status = WfTokenStatus.Active,
                NodeId = returnTargetNodeId,
                ForkId = null,
                PendingArmCount = null,
                Version = row.Version + 1,
                UpdateTime = localNow,
                UpdateUserId = ctx.ActorUserId,
            })
            .Where(row => row.Id == parent.Id
                          && row.Status == WfTokenStatus.WaitingJoin
                          && row.ForkId == forkId
                          && row.Version == parent.Version)
            .ExecuteCommandAsync(cancellationToken);
        if (parentUpdated != 1)
            ForkParallelOp.ThrowConflict("parallelParentVersionConflict", parent.Id);

        parent.Status = WfTokenStatus.Active;
        parent.NodeId = returnTargetNodeId;
        parent.ForkId = null;
        parent.PendingArmCount = null;
        parent.Version++;
        ctx.Token = parent;
    }
}
