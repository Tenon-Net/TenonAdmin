using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T19 fork/join 纵切：父子 token、空臂、自动 execution 与人工待办共用既有推进链。</summary>
public class WfParallelRuntimeTests
{
    [Fact]
    public async Task Concurrent_duplicate_start_reuses_one_parallel_fork()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var setup = factory.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var version = await InsertVersionAsync(db, EmptyModel());
        var command = new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
            RequestId = "parallel-duplicate-start",
        };

        using var rightFactory = new WorkflowAppFactory { DbPath = factory.DbPath, ResetDatabase = false, WorkerId = 1 };
        _ = rightFactory.CreateClient();
        using var left = factory.Services.CreateScope();
        using var right = rightFactory.Services.CreateScope();
        var outcomes = await Task.WhenAll(
            RunIsolatedAsync(() => left.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(command)),
            RunIsolatedAsync(() => right.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(command)));
        foreach (var outcome in outcomes.Where(outcome => outcome.Error is not null))
            Assert.IsType<AdminException>(outcome.Error);

        var instances = await db.Queryable<WfInstance>().ToListAsync();
        Assert.Single(instances);
        var parent = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == instances[0].Id && token.ParentTokenId == null)
            .FirstAsync();
        Assert.NotNull(parent);
        Assert.Single(await db.Queryable<WfHistory>()
            .Where(history => history.InstanceId == instances[0].Id
                              && history.EventType == WfHistoryEventType.ParallelFork)
            .ToListAsync());
        Assert.Equal(2, await db.Queryable<WfParallelArm>()
            .Where(arm => arm.ParentTokenId == parent!.Id)
            .CountAsync());
    }

    [Fact]
    public async Task Instance_detail_replays_each_arm_and_keeps_task_token_targets_distinct()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var service = scope.ServiceProvider.GetRequiredService<IWfInstanceService>();
        var version = await InsertVersionAsync(db, TwoApprovalModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        var waiting = await service.GetAsync(started.InstanceId, 1);
        var fork = Assert.Single(waiting.ParallelForks);
        Assert.Equal(WfParallelForkStatus.Waiting, fork.Status);
        Assert.Equal(2, fork.PendingArmCount);
        Assert.Equal(2, fork.Arms.Count);
        Assert.All(fork.Arms, arm =>
        {
            Assert.Equal(WfParallelArmStatus.Active, arm.Status);
            Assert.NotNull(arm.ChildTokenId);
            Assert.NotNull(arm.CurrentNodeId);
        });
        Assert.Equal(2, waiting.CurrentTasks.Count);
        Assert.Equal(
            waiting.CurrentTasks.Select(task => task.TokenId).Distinct().Count(),
            waiting.CurrentTasks.Count);
        Assert.All(waiting.CurrentTasks, task =>
            Assert.Contains(task.TokenId, fork.Arms.Select(arm => arm.ChildTokenId)));

        var history = await service.ListHistoryAsync(started.InstanceId, 1);
        Assert.NotEmpty(history);
        Assert.Equal(history.Select(item => item.Sequence).OrderBy(sequence => sequence), history.Select(item => item.Sequence));
        Assert.Contains(history, item => item.EventType == WfHistoryEventType.ParallelFork && item.TokenId == fork.ParentTokenId);

        var tasks = await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId)
            .OrderBy(task => task.Id)
            .ToListAsync();
        await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = tasks[0].Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        });

        var oneDone = Assert.Single((await service.GetAsync(started.InstanceId, 1)).ParallelForks);
        Assert.Equal(WfParallelForkStatus.Waiting, oneDone.Status);
        Assert.Equal(1, oneDone.PendingArmCount);
        Assert.Contains(oneDone.Arms, arm => arm.Status == WfParallelArmStatus.Completed);
        Assert.Contains(oneDone.Arms, arm => arm.Status == WfParallelArmStatus.Active);

        var remainingTask = Assert.Single(await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId)
            .ToListAsync());
        await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = remainingTask.Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        });

        var joined = Assert.Single((await service.GetAsync(started.InstanceId, 1)).ParallelForks);
        Assert.Equal(WfParallelForkStatus.Joined, joined.Status);
        Assert.All(joined.Arms, arm => Assert.Equal(WfParallelArmStatus.Completed, arm.Status));
        Assert.Null((await db.Queryable<WfToken>().Where(token => token.Id == joined.ParentTokenId).FirstAsync())!.ForkId);
        Assert.Contains((await service.GetAsync(started.InstanceId, 1)).CurrentTasks,
            task => task.NodeId == "after" && task.TokenId == joined.ParentTokenId);
    }

    [Fact]
    public async Task Instance_detail_marks_cancelled_parallel_fork_and_clears_targets()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var service = scope.ServiceProvider.GetRequiredService<IWfInstanceService>();
        var version = await InsertVersionAsync(db, ControlModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        await engine.ExecuteAsync(new CancelInstanceCmd
        {
            InstanceId = started.InstanceId,
            CallerUserId = 1,
        });

        var detail = await service.GetAsync(started.InstanceId, 1);
        var fork = Assert.Single(detail.ParallelForks);
        Assert.Equal(WfParallelForkStatus.Cancelled, fork.Status);
        Assert.All(fork.Arms, arm => Assert.Equal(WfParallelArmStatus.Cancelled, arm.Status));
        Assert.Empty(detail.CurrentTasks);
        Assert.Contains(await service.ListHistoryAsync(started.InstanceId, 1),
            item => item.EventType == WfHistoryEventType.ParallelCancelled);
    }

    [Fact]
    public async Task Concurrent_arm_completion_retries_the_loser_and_joins_once()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var setup = factory.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var version = await InsertVersionAsync(db, TwoApprovalModel());
        var started = await setup.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(
            new StartInstanceCmd
            {
                DefinitionVersionId = version.Id,
                StarterUserId = 1,
                StarterOrgId = 1,
            });
        var forkId = (await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId && token.ParentTokenId == null)
            .FirstAsync())!.ForkId;
        Assert.NotNull(forkId);

        var branchTasks = await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId != "after")
            .OrderBy(task => task.Id)
            .ToListAsync();
        Assert.Equal(2, branchTasks.Count);

        var commands = branchTasks.Select(task => new CompleteTaskCmd
        {
            TaskId = task.Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        }).ToArray();
        using var rightFactory = new WorkflowAppFactory { DbPath = factory.DbPath, ResetDatabase = false, WorkerId = 1 };
        _ = rightFactory.CreateClient();
        using var left = factory.Services.CreateScope();
        using var right = rightFactory.Services.CreateScope();
        var outcomes = await Task.WhenAll(
            RunIsolatedAsync(() => left.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(commands[0])),
            RunIsolatedAsync(() => right.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(commands[1])));

        Assert.InRange(outcomes.Count(outcome => outcome.Error is null), 1, 2);
        foreach (var outcome in outcomes.Where(outcome => outcome.Error is not null))
        {
            Assert.IsType<AdminException>(outcome.Error);
            Assert.Equal(
                WorkflowErrorCode.InstanceStatusConflict,
                (int)((AdminException)outcome.Error!).Code);
        }

        foreach (var (outcome, command) in outcomes.Zip(commands))
        {
            if (outcome.Error is not null)
                await left.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(command);
        }

        var parent = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId && token.ParentTokenId == null)
            .FirstAsync();
        Assert.NotNull(parent);
        Assert.Equal(WfTokenStatus.Active, parent.Status);
        Assert.Equal(0, parent.PendingArmCount);
        Assert.Equal("after", parent.NodeId);
        Assert.Equal(2, await db.Queryable<WfParallelArm>()
            .Where(arm => arm.ForkId == forkId && arm.Status == WfParallelArmStatus.Completed)
            .CountAsync());
        Assert.Single(await db.Queryable<WfHistory>()
            .Where(history => history.InstanceId == started.InstanceId
                              && history.EventType == WfHistoryEventType.ParallelJoined)
            .ToListAsync());
        Assert.Single(await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "after")
            .ToListAsync());
    }

    [Fact]
    public async Task Cancellation_cancels_every_parallel_token_arm_task_and_execution()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, ControlModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        await engine.ExecuteAsync(new CancelInstanceCmd
        {
            InstanceId = started.InstanceId,
            CallerUserId = 1,
        });

        Assert.Equal(WfInstanceStatus.Cancelled,
            (await db.Queryable<WfInstance>().Where(instance => instance.Id == started.InstanceId).FirstAsync())!.Status);
        var tokens = await db.Queryable<WfToken>().Where(token => token.InstanceId == started.InstanceId).ToListAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.Equal(WfTokenStatus.Cancelled, token.Status));
        var arms = await db.Queryable<WfParallelArm>().Where(arm => arm.ParentTokenId == tokens[0].Id ||
                                                                    arm.ParentTokenId == tokens[1].Id ||
                                                                    arm.ParentTokenId == tokens[2].Id).ToListAsync();
        Assert.Equal(2, arms.Count);
        Assert.All(arms, arm => Assert.Equal(WfParallelArmStatus.Cancelled, arm.Status));
        Assert.Empty(await db.Queryable<WfTask>().Where(task => task.InstanceId == started.InstanceId).ToListAsync());
        Assert.All(
            await db.Queryable<WfNodeExecution>().Where(execution => execution.InstanceId == started.InstanceId).ToListAsync(),
            execution => Assert.Equal(WfNodeExecutionStatus.Cancelled, execution.Status));
        Assert.Equal(2, await db.Queryable<WfHistory>()
            .Where(history => history.InstanceId == started.InstanceId
                              && history.EventType == WfHistoryEventType.ParallelCancelled)
            .CountAsync());
    }

    [Fact]
    public async Task Rejecting_one_parallel_arm_cancels_the_other_arm()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, ControlModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });
        var approvalTask = await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "approval")
            .FirstAsync();
        Assert.NotNull(approvalTask);

        await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = approvalTask!.Id,
            UserId = 1,
            Action = WfTaskAction.Reject,
        });

        Assert.Equal(WfInstanceStatus.Rejected,
            (await db.Queryable<WfInstance>().Where(instance => instance.Id == started.InstanceId).FirstAsync())!.Status);
        Assert.All(
            await db.Queryable<WfToken>().Where(token => token.InstanceId == started.InstanceId).ToListAsync(),
            token => Assert.Equal(WfTokenStatus.Cancelled, token.Status));
        Assert.All(
            await db.Queryable<WfParallelArm>().Where(arm => arm.ParentTokenId != 0).ToListAsync(),
            arm => Assert.Equal(WfParallelArmStatus.Cancelled, arm.Status));
        Assert.All(
            await db.Queryable<WfNodeExecution>().Where(execution => execution.InstanceId == started.InstanceId).ToListAsync(),
            execution => Assert.Equal(WfNodeExecutionStatus.Cancelled, execution.Status));
        Assert.Empty(await db.Queryable<WfTask>().Where(task => task.InstanceId == started.InstanceId).ToListAsync());
    }

    [Fact]
    public async Task Returning_from_a_parallel_arm_restores_the_parent_and_resubmit_forks_again()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, ReturnModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });
        var parent = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId && token.ParentTokenId == null)
            .FirstAsync();
        Assert.NotNull(parent);
        var oldForkId = parent!.ForkId;
        var approvalTask = await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "approval")
            .FirstAsync();
        Assert.NotNull(approvalTask);

        await engine.ExecuteAsync(new ReturnTaskCmd
        {
            TaskId = approvalTask!.Id,
            UserId = 1,
        });

        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Active, parent!.Status);
        Assert.Equal("start", parent.NodeId);
        Assert.Null(parent.ForkId);
        Assert.Null(parent.PendingArmCount);
        Assert.All(
            await db.Queryable<WfToken>().Where(token => token.InstanceId == started.InstanceId && token.Id != parent.Id).ToListAsync(),
            token => Assert.Equal(WfTokenStatus.Cancelled, token.Status));
        Assert.All(
            await db.Queryable<WfParallelArm>().Where(arm => arm.ForkId == oldForkId).ToListAsync(),
            arm => Assert.Equal(WfParallelArmStatus.Cancelled, arm.Status));
        Assert.Empty(await db.Queryable<WfTask>().Where(task => task.InstanceId == started.InstanceId).ToListAsync());

        await engine.ExecuteAsync(new ResubmitInstanceCmd
        {
            InstanceId = started.InstanceId,
            CallerUserId = 1,
        });

        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.WaitingJoin, parent!.Status);
        Assert.NotNull(parent.ForkId);
        Assert.NotEqual(oldForkId, parent.ForkId);
        Assert.NotEmpty(await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "approval")
            .ToListAsync());

        var detail = await scope.ServiceProvider.GetRequiredService<IWfInstanceService>()
            .GetAsync(started.InstanceId, 1);
        Assert.Equal(WfParallelForkStatus.Cancelled,
            Assert.Single(detail.ParallelForks, fork => fork.ForkId == oldForkId).Status);
        Assert.Equal(WfParallelForkStatus.Waiting,
            Assert.Single(detail.ParallelForks, fork => fork.ForkId == parent.ForkId).Status);
    }

    [Fact]
    public async Task A_late_parallel_webhook_owner_cannot_advance_a_cancelled_fork()
    {
        var handler = new BlockingNodeHandler();
        using var factory = new WorkflowAppFactory
        {
            Overrides = services => services.Insert(
                0,
                ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => handler)),
        };
        _ = factory.CreateClient();
        using var setup = factory.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await setup.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(
            new StartInstanceCmd
            {
                DefinitionVersionId = (await InsertVersionAsync(db, ControlModel())).Id,
                StarterUserId = 1,
                StarterOrgId = 1,
            });
        var execution = await db.Queryable<WfNodeExecution>()
            .Where(row => row.InstanceId == started.InstanceId)
            .FirstAsync();
        Assert.NotNull(execution);

        using var worker = factory.Services.CreateScope();
        using var canceller = factory.Services.CreateScope();
        var dispatcher = new WfNodeExecutionDispatcher(
            worker.ServiceProvider.GetRequiredService<ISqlSugarClient>(),
            [handler],
            worker.ServiceProvider.GetRequiredService<IWorkflowEngine>(),
            TimeProvider.System);
        var lateOwner = dispatcher.RunAsync(
            execution!.Id,
            "parallel-old-owner",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        await handler.WaitUntilStartedAsync();

        await canceller.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(new CancelInstanceCmd
        {
            InstanceId = started.InstanceId,
            CallerUserId = 1,
        });
        handler.Succeed();

        var error = await Record.ExceptionAsync(() => lateOwner);
        Assert.IsType<AdminException>(error);
        Assert.Equal(WfNodeExecutionStatus.Cancelled,
            (await db.Queryable<WfNodeExecution>().Where(row => row.Id == execution.Id).FirstAsync())!.Status);
        Assert.Equal(WfTokenStatus.Cancelled,
            (await db.Queryable<WfToken>().Where(token => token.Id == execution.TokenId).FirstAsync())!.Status);
    }

    [Fact]
    public async Task A_parallel_webhook_tx2_crash_is_reclaimed_without_a_second_arm_completion()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, ControlModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });
        var execution = await db.Queryable<WfNodeExecution>()
            .Where(row => row.InstanceId == started.InstanceId)
            .FirstAsync();
        Assert.NotNull(execution);
        var handler = new SequencedNodeHandler(
            WfNodeType.Webhook,
            WfNodeExecutionResult.Succeeded(summary: "first-attempt"),
            WfNodeExecutionResult.Succeeded(summary: "recovered"));
        var crashing = new WfNodeExecutionDispatcher(
            db,
            [handler],
            new CrashBeforeCommitEngine(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => crashing.RunAsync(
            execution!.Id,
            "parallel-crashed-owner",
            TimeSpan.FromMinutes(1),
            CancellationToken.None));
        var afterCrash = await db.Queryable<WfNodeExecution>().Where(row => row.Id == execution.Id).FirstAsync();
        Assert.Equal(WfNodeExecutionStatus.Running, afterCrash!.Status);
        Assert.Equal(1, afterCrash.AttemptCount);
        Assert.Empty(await db.Queryable<WfNodeExecutionAttempt>()
            .Where(attempt => attempt.ExecutionId == execution.Id)
            .ToListAsync());

        await db.Updateable<WfNodeExecution>()
            .SetColumns(row => new WfNodeExecution { LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) })
            .Where(row => row.Id == execution.Id)
            .ExecuteCommandAsync();
        var recovering = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);
        Assert.Equal(WfNodeExecutionStatus.Succeeded,
            await recovering.RunAsync(execution.Id, "parallel-recovering-owner", TimeSpan.FromMinutes(1), CancellationToken.None));

        var parent = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId && token.ParentTokenId == null)
            .FirstAsync();
        Assert.Equal(WfTokenStatus.WaitingJoin, parent!.Status);
        Assert.Equal(1, parent.PendingArmCount);
        Assert.Single(await db.Queryable<WfNodeExecutionAttempt>()
            .Where(attempt => attempt.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Parallel_webhook_retry_and_ai_manual_fallback_wait_before_joining()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, AutomaticModel());
        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });
        var executions = await db.Queryable<WfNodeExecution>()
            .Where(execution => execution.InstanceId == started.InstanceId)
            .ToListAsync();
        var webhook = Assert.Single(executions, execution => execution.NodeType == WfNodeType.Webhook);
        var ai = Assert.Single(executions, execution => execution.NodeType == WfNodeType.AiDecision);
        var webhookHandler = new SequencedNodeHandler(
            WfNodeType.Webhook,
            WfNodeExecutionResult.RetryableFailure(retryAfter: TimeSpan.Zero),
            WfNodeExecutionResult.Succeeded(summary: "retry-ok"));
        var aiHandler = new SequencedNodeHandler(WfNodeType.AiDecision, WfNodeExecutionResult.ManualFallback());
        var dispatcher = new WfNodeExecutionDispatcher(
            db,
            [webhookHandler, aiHandler],
            engine,
            TimeProvider.System);

        Assert.Equal(WfNodeExecutionStatus.RetryScheduled,
            await dispatcher.RunAsync(webhook.Id, "parallel-retry", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal(WfNodeExecutionStatus.ManualFallback,
            await dispatcher.RunAsync(ai.Id, "parallel-ai", TimeSpan.FromMinutes(1), CancellationToken.None));
        var parent = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId && token.ParentTokenId == null)
            .FirstAsync();
        Assert.Equal(WfTokenStatus.WaitingJoin, parent!.Status);
        Assert.Equal(2, parent.PendingArmCount);
        Assert.Single(await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "ai")
            .ToListAsync());

        await db.Updateable<WfNodeExecution>()
            .SetColumns(execution => new WfNodeExecution { NextRetryAtUtc = DateTime.UtcNow.AddMinutes(-1) })
            .Where(execution => execution.Id == webhook.Id)
            .ExecuteCommandAsync();
        Assert.Equal(WfNodeExecutionStatus.Succeeded,
            await dispatcher.RunAsync(webhook.Id, "parallel-retry-2", TimeSpan.FromMinutes(1), CancellationToken.None));
        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.WaitingJoin, parent!.Status);
        Assert.Equal(1, parent.PendingArmCount);

        var aiTask = Assert.Single(await db.Queryable<WfTask>()
            .Where(task => task.InstanceId == started.InstanceId && task.NodeId == "ai")
            .ToListAsync());
        await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = aiTask.Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        });
        Assert.Equal(WfInstanceStatus.Approved,
            (await db.Queryable<WfInstance>().Where(instance => instance.Id == started.InstanceId).FirstAsync())!.Status);
        Assert.Single(await db.Queryable<WfHistory>()
            .Where(history => history.InstanceId == started.InstanceId
                              && history.EventType == WfHistoryEventType.ParallelJoined)
            .ToListAsync());
    }

    [Fact]
    public async Task Webhook_and_manual_arms_join_only_after_both_complete()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, MixedModel());

        var started = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        var tokens = await db.Queryable<WfToken>()
            .Where(token => token.InstanceId == started.InstanceId)
            .ToListAsync();
        var parent = Assert.Single(tokens, token => token.ParentTokenId is null);
        Assert.Equal(WfTokenStatus.WaitingJoin, parent.Status);
        Assert.Equal(2, parent.PendingArmCount);
        Assert.NotNull(parent.ForkId);

        var arms = await db.Queryable<WfParallelArm>()
            .Where(arm => arm.ForkId == parent.ForkId)
            .OrderBy(arm => arm.ArmId)
            .ToListAsync();
        Assert.Equal(3, arms.Count);
        var webhookArm = Assert.Single(arms, arm => arm.ArmId == "webhook-arm");
        var approvalArm = Assert.Single(arms, arm => arm.ArmId == "approval-arm");
        var emptyArm = Assert.Single(arms, arm => arm.ArmId == "empty-arm");
        Assert.Equal(WfParallelArmStatus.Active, webhookArm.Status);
        Assert.Equal(WfParallelArmStatus.Active, approvalArm.Status);
        Assert.Equal(WfParallelArmStatus.Completed, emptyArm.Status);
        Assert.Null(emptyArm.ChildTokenId);
        Assert.Null(emptyArm.ChildEntryNodeVisitId);

        var webhookToken = Assert.Single(tokens, token => token.Id == webhookArm.ChildTokenId);
        var approvalToken = Assert.Single(tokens, token => token.Id == approvalArm.ChildTokenId);
        Assert.Equal(webhookArm.ChildEntryNodeVisitId, webhookToken.NodeVisitId);
        Assert.Equal(approvalArm.ChildEntryNodeVisitId, approvalToken.NodeVisitId);
        Assert.Equal(parent.Id, webhookToken.ParentTokenId);
        Assert.Equal(parent.ForkId, webhookToken.ForkId);

        var execution = await db.Queryable<WfNodeExecution>()
            .Where(row => row.TokenId == webhookToken.Id)
            .FirstAsync();
        Assert.NotNull(execution);
        var task = await db.Queryable<WfTask>()
            .Where(row => row.TokenId == approvalToken.Id)
            .FirstAsync();
        Assert.NotNull(task);

        var dispatcher = new WfNodeExecutionDispatcher(
            db,
            [new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook)],
            engine,
            TimeProvider.System);
        Assert.Equal(
            WfNodeExecutionStatus.Succeeded,
            await dispatcher.RunAsync(execution!.Id, "t19-worker", TimeSpan.FromMinutes(1), CancellationToken.None));

        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        webhookArm = await db.Queryable<WfParallelArm>()
            .Where(arm => arm.ForkId == parent!.ForkId && arm.ArmId == "webhook-arm")
            .FirstAsync();
        Assert.Equal(WfTokenStatus.WaitingJoin, parent!.Status);
        Assert.Equal(1, parent.PendingArmCount);
        Assert.Equal(WfParallelArmStatus.Completed, webhookArm!.Status);

        var completed = await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = task!.Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        });
        Assert.Equal(WfInstanceStatus.Running, completed.InstanceStatus);

        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Active, parent!.Status);
        Assert.Equal(0, parent.PendingArmCount);
        Assert.Equal("after", parent.NodeId);
        var afterTask = await db.Queryable<WfTask>()
            .Where(row => row.TokenId == parent.Id && row.NodeId == "after")
            .FirstAsync();
        Assert.NotNull(afterTask);
        var finished = await engine.ExecuteAsync(new CompleteTaskCmd
        {
            TaskId = afterTask!.Id,
            UserId = 1,
            Action = WfTaskAction.Approve,
        });
        Assert.Equal(WfInstanceStatus.Approved, finished.InstanceStatus);
        parent = await db.Queryable<WfToken>().Where(token => token.Id == parent.Id).FirstAsync();
        Assert.Equal(WfTokenStatus.Completed, parent!.Status);
        Assert.All(
            await db.Queryable<WfParallelArm>().Where(arm => arm.ForkId == parent.ForkId).ToListAsync(),
            arm => Assert.Equal(WfParallelArmStatus.Completed, arm.Status));

        var history = await db.Queryable<WfHistory>()
            .Where(row => row.InstanceId == started.InstanceId
                          && (row.EventType == WfHistoryEventType.ParallelFork
                              || row.EventType == WfHistoryEventType.ParallelArmCompleted
                              || row.EventType == WfHistoryEventType.ParallelJoined))
            .ToListAsync();
        Assert.Single(history, row => row.EventType == WfHistoryEventType.ParallelFork);
        Assert.Equal(3, history.Count(row => row.EventType == WfHistoryEventType.ParallelArmCompleted));
        Assert.Single(history, row => row.EventType == WfHistoryEventType.ParallelJoined);
        Assert.Contains(history, row => row.PayloadJson?.Contains("childEntryNodeVisitId", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task All_empty_arms_join_in_the_fork_transaction_without_child_tokens()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var version = await InsertVersionAsync(db, EmptyModel());

        var result = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = 1,
            StarterOrgId = 1,
        });

        Assert.Equal(WfInstanceStatus.Approved, result.InstanceStatus);
        var tokens = await db.Queryable<WfToken>().Where(token => token.InstanceId == result.InstanceId).ToListAsync();
        var parent = Assert.Single(tokens);
        Assert.Equal(WfTokenStatus.Completed, parent.Status);
        Assert.Null(parent.ForkId);
        Assert.Equal(0, parent.PendingArmCount);
        var arms = await db.Queryable<WfParallelArm>().Where(arm => arm.ParentTokenId == parent.Id).ToListAsync();
        Assert.Equal(2, arms.Count);
        Assert.All(arms, arm =>
        {
            Assert.Equal(WfParallelArmStatus.Completed, arm.Status);
            Assert.Null(arm.ChildTokenId);
            Assert.Null(arm.ChildEntryNodeVisitId);
        });
    }

    private static async Task<WfDefinitionVersion> InsertVersionAsync(ISqlSugarClient db, WfModel model)
    {
        var version = new WfDefinitionVersion
        {
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = WfModelJson.Serialize(model),
        };
        await db.Insertable(version).ExecuteCommandAsync();
        return version;
    }

    private static WfModel MixedModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    new WfParallelArmDefinition
                    {
                        Id = "webhook-arm",
                        Next = new WfNode
                        {
                            Id = "webhook",
                            Type = WfNodeType.Webhook,
                            Props = new WfNodeProps { WebhookUrl = "http://127.0.0.1/webhook" },
                        },
                    },
                    new WfParallelArmDefinition
                    {
                        Id = "approval-arm",
                        Next = new WfNode
                        {
                            Id = "approval",
                            Type = WfNodeType.Approval,
                            Props = new WfNodeProps
                            {
                                Assignee = new WfAssignee { Provider = ApproverProviderKeys.Initiator },
                            },
                        },
                    },
                    new WfParallelArmDefinition { Id = "empty-arm" },
                ],
                Next = new WfNode
                {
                    Id = "after",
                    Type = WfNodeType.Approval,
                    Props = new WfNodeProps
                    {
                        Assignee = new WfAssignee { Provider = ApproverProviderKeys.Initiator },
                    },
                },
            },
        },
    };

    private static WfModel EmptyModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    new WfParallelArmDefinition { Id = "a" },
                    new WfParallelArmDefinition { Id = "b" },
                ],
            },
        },
    };

    private static WfModel TwoApprovalModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    ApprovalArm("a"),
                    ApprovalArm("b"),
                ],
                Next = ApprovalNode("after"),
            },
        },
    };

    private static WfModel ControlModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    new WfParallelArmDefinition { Id = "approval-arm", Next = ApprovalNode("approval") },
                    new WfParallelArmDefinition
                    {
                        Id = "webhook-arm",
                        Next = new WfNode
                        {
                            Id = "webhook",
                            Type = WfNodeType.Webhook,
                            Props = new WfNodeProps { WebhookUrl = "http://127.0.0.1/webhook" },
                        },
                    },
                ],
            },
        },
    };

    private static WfModel ReturnModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    new WfParallelArmDefinition { Id = "approval-arm", Next = ApprovalNode("approval", WfReturnPolicy.Prev) },
                    new WfParallelArmDefinition
                    {
                        Id = "webhook-arm",
                        Next = new WfNode
                        {
                            Id = "webhook",
                            Type = WfNodeType.Webhook,
                            Props = new WfNodeProps { WebhookUrl = "http://127.0.0.1/webhook" },
                        },
                    },
                ],
            },
        },
    };

    private static WfModel AutomaticModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "parallel",
                Type = WfNodeType.Parallel,
                ParallelArms =
                [
                    new WfParallelArmDefinition
                    {
                        Id = "webhook-arm",
                        Next = new WfNode
                        {
                            Id = "webhook",
                            Type = WfNodeType.Webhook,
                            Props = new WfNodeProps { WebhookUrl = "http://127.0.0.1/webhook" },
                        },
                    },
                    new WfParallelArmDefinition
                    {
                        Id = "ai-arm",
                        Next = new WfNode
                        {
                            Id = "ai",
                            Type = WfNodeType.AiDecision,
                            Props = new WfNodeProps
                            {
                                AiInstructions = "Review the case.",
                                AiInputFields = ["caseId"],
                                Assignee = new WfAssignee { Provider = ApproverProviderKeys.Initiator },
                            },
                        },
                    },
                ],
            },
        },
    };

    private static WfParallelArmDefinition ApprovalArm(string id) =>
        new() { Id = id, Next = ApprovalNode($"approval-{id}") };

    private static WfNode ApprovalNode(string id, WfReturnPolicy? returnPolicy = null) => new()
    {
        Id = id,
        Type = WfNodeType.Approval,
        Props = new WfNodeProps
        {
            Assignee = new WfAssignee { Provider = ApproverProviderKeys.Initiator },
            ReturnPolicy = returnPolicy,
        },
    };

    private static async Task<CommandOutcome> CaptureAsync(Func<Task<WfEngineResult>> execute)
    {
        try
        {
            return new CommandOutcome(await execute(), null);
        }
        catch (Exception exception)
        {
            return new CommandOutcome(null, exception);
        }
    }

    /// <summary>让两个并发请求各自从干净的 SqlSugar AsyncLocal 上下文开始。</summary>
    private static Task<CommandOutcome> RunIsolatedAsync(Func<Task<WfEngineResult>> execute)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(() => CaptureAsync(execute));
    }

    private sealed record CommandOutcome(WfEngineResult? Result, Exception? Error);

    private sealed class BlockingNodeHandler : IWorkflowNodeHandler
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<WfNodeExecutionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WfNodeType NodeType => WfNodeType.Webhook;

        public Task<WfNodeExecutionResult> ExecuteAsync(
            WfNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            started.TrySetResult();
            return completion.Task;
        }

        public Task WaitUntilStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Succeed() => completion.TrySetResult(WfNodeExecutionResult.Succeeded(summary: "late-ok"));
    }

    private sealed class SequencedNodeHandler(
        WfNodeType nodeType,
        params WfNodeExecutionResult[] results) : IWorkflowNodeHandler
    {
        private readonly Queue<WfNodeExecutionResult> results = new(results);

        public WfNodeType NodeType => nodeType;

        public int CallCount { get; private set; }

        public Task<WfNodeExecutionResult> ExecuteAsync(
            WfNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class CrashBeforeCommitEngine : IWorkflowEngine
    {
        public Task<WfEngineResult> ExecuteAsync(
            IWfCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WfEngineResult>(
                new InvalidOperationException("parallel tx2 crash"));
    }
}
