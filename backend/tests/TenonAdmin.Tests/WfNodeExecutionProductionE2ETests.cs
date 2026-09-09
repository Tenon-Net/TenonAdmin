using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// Task 8b T11 完整 Webhook E2E：从定义发布、流程发起、入口建 execution、后台 worker 领取，
/// 到真实 Webhook handler 外呼和 dispatcher 回写，覆盖成功、重试、终态失败、人工兜底与恢复。
/// </summary>
public class WfNodeExecutionProductionE2ETests
{
    [Theory]
    [MemberData(nameof(AiDecisionProviderNegativeScenarios))]
    public async Task AiDecision_provider_negative_scenarios_are_audited_and_stay_manual(
        FakeAiDecisionScenario scenario,
        AiDecisionProviderResultType providerResultType,
        AiDecisionFallbackReason fallbackReason,
        AiDecisionPolicyClassification? policyClassification,
        bool? schemaValid)
    {
        var fake = new FakeAiDecisionProvider(scenario);
        using var f = NewAiFactory(fake, new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions
        {
            HighRiskFlags = [FakeAiDecisionProvider.HighRiskFlag],
        }));
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await StartPublishedAiAsync(scope.ServiceProvider);
        var execution = await SingleExecutionAsync(db, started.InstanceId);

        await RunWorkerAsync(scope.ServiceProvider);

        var completed = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, completed.Status);
        Assert.Equal(1, fake.CallCount);
        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(1, attempt.AttemptNo);
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, attempt.ResultType);

        var audit = Assert.Single(await db.Queryable<WfAiDecision>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(attempt.AttemptNo, audit.AttemptNo);
        Assert.Equal(providerResultType, audit.ProviderResultType);
        Assert.Equal(schemaValid, audit.SchemaValid);
        Assert.Equal(policyClassification, audit.PolicyClassification);
        Assert.Equal(fallbackReason, audit.FallbackReason);
        Assert.True(audit.ShadowMode);

        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        var task = Assert.Single(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal("ai", task.NodeId);
        Assert.Single(await db.Queryable<WfTaskActor>().Where(a => a.TaskId == task.Id).ToListAsync());
        Assert.Contains(await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId)
            .ToListAsync(), h => h.EventType == WfHistoryEventType.TaskCreated && h.NodeId == "ai");
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    public static IEnumerable<object?[]> AiDecisionProviderNegativeScenarios()
    {
        yield return [FakeAiDecisionScenario.MalformedProposal, AiDecisionProviderResultType.Proposal,
            AiDecisionFallbackReason.MalformedProposal, null, false];
        yield return [FakeAiDecisionScenario.LowConfidence, AiDecisionProviderResultType.Proposal,
            AiDecisionFallbackReason.LowConfidence, AiDecisionPolicyClassification.LowConfidence, true];
        yield return [FakeAiDecisionScenario.HighRisk, AiDecisionProviderResultType.Proposal,
            AiDecisionFallbackReason.HighRisk, AiDecisionPolicyClassification.HighRisk, true];
        yield return [FakeAiDecisionScenario.TimedOut, AiDecisionProviderResultType.TimedOut,
            AiDecisionFallbackReason.ProviderTimeout, null, null];
        yield return [FakeAiDecisionScenario.Failed, AiDecisionProviderResultType.Failed,
            AiDecisionFallbackReason.ProviderFailure, null, null];
        yield return [FakeAiDecisionScenario.Throws, AiDecisionProviderResultType.Failed,
            AiDecisionFallbackReason.ProviderFailure, null, null];
    }

    [Fact]
    public async Task AiDecision_external_cancellation_leaves_only_the_claim_and_no_side_effects()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new CancellingAiDecisionProvider(cancellation);
        using var f = NewAiFactory(provider);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await StartPublishedAiAsync(scope.ServiceProvider);
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        var historyCount = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId)
            .CountAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ResolveWorker(scope.ServiceProvider).ExecuteAsync(JobContext(), cancellation.Token));

        var reloaded = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal(1, provider.CallCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Empty(await db.Queryable<WfAiDecision>().Where(a => a.ExecutionId == execution.Id).ToListAsync());
        Assert.Empty(await ReadOutboxesAsync(db, execution.Id));
        Assert.Empty(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal(0, await db.Queryable<WfTaskActor>().CountAsync());
        Assert.Equal(historyCount, await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId)
            .CountAsync());
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task AiDecision_crash_before_tx2_is_reclaimed_once_with_append_only_history()
    {
        var fake = new FakeAiDecisionProvider();
        using var f = NewAiFactory(fake);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await StartPublishedAiAsync(scope.ServiceProvider);
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        var handler = new AiDecisionNodeHandler(
            fake,
            new AiDecisionProposalParser(),
            new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions()));
        var crashingDispatcher = new WfNodeExecutionDispatcher(
            db,
            [handler],
            new CrashBeforeCommitEngine(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => crashingDispatcher.RunAsync(
            execution.Id, "worker-ai-crashed", TimeSpan.FromMinutes(5), CancellationToken.None));

        var afterCrash = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, afterCrash.Status);
        Assert.Equal(1, afterCrash.AttemptCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Empty(await db.Queryable<WfAiDecision>().Where(a => a.ExecutionId == execution.Id).ToListAsync());
        Assert.Empty(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(1, fake.CallCount);

        await ExpireLeaseAsync(db, execution.Id);
        await RunWorkerAsync(scope.ServiceProvider);

        var recovered = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(2, attempt.AttemptNo);
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, attempt.ResultType);
        var audit = Assert.Single(await db.Queryable<WfAiDecision>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(2, audit.AttemptNo);
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Single(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal(2, fake.CallCount);
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task AiDecision_duplicate_worker_scan_does_not_append_any_side_effect()
    {
        var fake = new FakeAiDecisionProvider();
        using var f = NewAiFactory(fake);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var started = await StartPublishedAiAsync(scope.ServiceProvider);
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        await RunWorkerAsync(scope.ServiceProvider);

        var attemptCount = await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync();
        var auditCount = await db.Queryable<WfAiDecision>().Where(a => a.ExecutionId == execution.Id).CountAsync();
        var outboxCount = await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync();
        var taskCount = await ReadTasksAsync(db, execution.InstanceId);
        var actorCount = await db.Queryable<WfTaskActor>().CountAsync();
        var historyCount = await db.Queryable<WfHistory>().Where(h => h.InstanceId == execution.InstanceId).CountAsync();

        await RunWorkerAsync(scope.ServiceProvider);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(attemptCount, await db.Queryable<WfNodeExecutionAttempt>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(auditCount, await db.Queryable<WfAiDecision>().Where(a => a.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(outboxCount, await db.Queryable<WfOutbox>().Where(o => o.ExecutionId == execution.Id).CountAsync());
        Assert.Equal(taskCount.Count, (await ReadTasksAsync(db, execution.InstanceId)).Count);
        Assert.Equal(actorCount, await db.Queryable<WfTaskActor>().CountAsync());
        Assert.Equal(historyCount, await db.Queryable<WfHistory>().Where(h => h.InstanceId == execution.InstanceId).CountAsync());
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, (await ReadExecutionAsync(db, execution.Id)).Status);
    }

    [Fact]
    public async Task AiDecision_stale_owner_cannot_write_attempt_audit_outbox_or_task()
    {
        var fake = new FakeAiDecisionProvider();
        using var f = NewAiFactory(fake);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var started = await StartPublishedAiAsync(scope.ServiceProvider);
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        var now = DateTime.UtcNow;
        var oldOwner = await ClaimAsync(db, execution.Id, "worker-ai-old", now);
        Assert.NotNull(oldOwner);
        await ExpireLeaseAsync(db, execution.Id);
        var newOwner = await ClaimAsync(db, execution.Id, "worker-ai-new", now.AddMinutes(1));
        Assert.NotNull(newOwner);

        var historyCount = await db.Queryable<WfHistory>().Where(h => h.InstanceId == execution.InstanceId).CountAsync();
        var typedResult = await new AiDecisionNodeHandler(
            fake,
            new AiDecisionProposalParser(),
            new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions()))
            .ExecuteAsync(new WfNodeExecutionContext
            {
                ExecutionKey = execution.ExecutionKey,
                InstanceId = execution.InstanceId,
                TokenId = execution.TokenId,
                NodeVisitId = execution.NodeVisitId,
                NodeId = execution.NodeId,
                NodeType = WfNodeType.AiDecision,
                DefinitionVersionId = execution.DefinitionVersionId,
                StarterUserId = 1,
                NodeProps = AiDecisionModel().Root.Next!.Props,
                VariablesJson = "{\"amount\":12}",
                Attempt = 1,
                DeadlineAtUtc = new DateTimeOffset(now.AddMinutes(5), TimeSpan.Zero),
            }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(
            new NodeExecutionCompletedCmd
            {
                ExecutionId = execution.Id,
                Fence = oldOwner!.Fence,
                Result = typedResult,
                HandlerType = typeof(AiDecisionNodeHandler).FullName,
                StartedAtUtc = now,
                EndedAtUtc = now.AddSeconds(1),
            }));

        Assert.Equal(48004, (int)exception.Code);
        var reloaded = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, reloaded.Status);
        Assert.Equal(newOwner!.Fence, reloaded.Fence);
        Assert.Equal("worker-ai-new", reloaded.LeaseOwner);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Empty(await db.Queryable<WfAiDecision>().Where(a => a.ExecutionId == execution.Id).ToListAsync());
        Assert.Empty(await ReadOutboxesAsync(db, execution.Id));
        Assert.Empty(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal(0, await db.Queryable<WfTaskActor>().CountAsync());
        Assert.Equal(historyCount, await db.Queryable<WfHistory>().Where(h => h.InstanceId == execution.InstanceId).CountAsync());
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task A_published_ai_decision_runs_through_scheduler_worker_and_creates_shadow_audit_and_manual_todo()
    {
        var fake = new FakeAiDecisionProvider();
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.AddScoped<IAiDecisionProvider>(_ => fake),
        };
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var definitions = scope.ServiceProvider.GetRequiredService<IWfDefinitionService>();
        var definitionId = await definitions.AddAsync(new WfDefinitionInput
        {
            Name = $"AI Decision E2E {Guid.NewGuid():N}",
            Model = AiDecisionModel(),
        });

        Assert.Equal(1, await definitions.PublishAsync(definitionId));
        var started = await scope.ServiceProvider.GetRequiredService<IWfInstanceService>().StartAsync(
            new WfStartInput
            {
                DefinitionId = definitionId,
                BusinessKey = $"ai-e2e-{Guid.NewGuid():N}",
                VariablesJson = "{\"amount\":12}",
            },
            starterUserId: 1,
            starterOrgId: 1,
            CancellationToken.None);

        var execution = await SingleExecutionAsync(db, started.InstanceId);
        Assert.Equal(WfInstanceStatus.Running, started.InstanceStatus);
        Assert.Equal(WfNodeExecutionStatus.Pending, execution.Status);
        Assert.Equal(WfNodeType.AiDecision, execution.NodeType);

        var job = await db.Queryable<SysJob>()
            .Where(j => j.Code == "wf-node-execution-scan")
            .FirstAsync();
        Assert.NotNull(job);
        await db.Updateable<SysJob>()
            .SetColumns(j => new SysJob { NextRunTime = DateTime.Now.AddSeconds(-10) })
            .Where(j => j.Id == job!.Id)
            .ExecuteCommandAsync();

        await scope.ServiceProvider.GetRequiredService<JobSchedulerService>()
            .TickAsync(CancellationToken.None);

        var deadline = Environment.TickCount64 + 5_000;
        WfNodeExecution completed;
        List<SysJobLog> logs;
        do
        {
            completed = await ReadExecutionAsync(db, execution.Id);
            logs = await db.Queryable<SysJobLog>()
                .Where(l => l.JobId == job!.Id)
                .ToListAsync();
            if (completed.Status == WfNodeExecutionStatus.ManualFallback
                && logs.Any(l => l.EndTime is not null))
                break;
            await Task.Delay(50);
        } while (Environment.TickCount64 < deadline);

        Assert.Contains(logs, log => log.EndTime is not null && log.RunStatus == JobRunStatus.Success);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, completed.Status);
        Assert.Equal(1, fake.CallCount);

        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, attempt.ResultType);
        var audit = Assert.Single(await db.Queryable<WfAiDecision>()
            .Where(a => a.ExecutionId == execution.Id)
            .ToListAsync());
        Assert.Equal(attempt.AttemptNo, audit.AttemptNo);
        Assert.Equal(AiDecisionProviderResultType.Proposal, audit.ProviderResultType);
        Assert.True(audit.SchemaValid);
        Assert.Equal(AiDecisionPolicyClassification.ShadowCandidate, audit.PolicyClassification);
        Assert.Equal(AiDecisionFallbackReason.ShadowOnly, audit.FallbackReason);
        Assert.True(audit.ShadowMode);

        var outbox = Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(WfOutboxStatus.Pending, outbox.Status);
        Assert.Contains("manualFallback", outbox.PayloadJson ?? string.Empty, StringComparison.Ordinal);

        var task = Assert.Single(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal("ai", task.NodeId);
        Assert.Equal(execution.TokenId, task.TokenId);
        Assert.Equal(execution.NodeVisitId, task.NodeVisitId);
        var actor = Assert.Single(await db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id)
            .ToListAsync());
        Assert.Equal(1, actor.UserId);
        Assert.Equal(WfActorStatus.Pending, actor.Status);
        Assert.Contains(await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId)
            .ToListAsync(), h => h.EventType == WfHistoryEventType.TaskCreated && h.NodeId == "ai");
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task A_published_webhook_runs_through_the_worker_and_advances_once()
    {
        var transport = new SequenceTransport(_ => Ok("accepted"));
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var sawTransaction = true;
        var sawTransactionObject = true;
        transport.OnSend = () =>
        {
            sawTransaction = db.Ado.IsAnyTran();
            sawTransactionObject = db.Ado.Transaction is not null;
        };

        var started = await StartPublishedWebhookAsync(scope.ServiceProvider, WebhookModel());
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        Assert.Equal(WfNodeExecutionStatus.Pending, execution.Status);

        await RunWorkerAsync(scope.ServiceProvider);

        Assert.False(sawTransaction);
        Assert.False(sawTransactionObject);
        Assert.Equal(1, transport.SendCount);

        var completed = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, completed.Status);
        Assert.Equal(1, completed.AttemptCount);
        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(WfNodeExecutionResultType.Succeeded, attempt.ResultType);
        Assert.Equal("accepted", attempt.OutputSummary);
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(WfTokenStatus.Completed, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Approved, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task A_retryable_webhook_failure_is_retried_and_then_succeeds()
    {
        var transport = new SequenceTransport(call => call == 1
            ? Response(HttpStatusCode.ServiceUnavailable, "temporary")
            : Ok("recovered"));
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var started = await StartPublishedWebhookAsync(scope.ServiceProvider, WebhookModel());
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        var worker = ResolveWorker(scope.ServiceProvider);

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var scheduled = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.RetryScheduled, scheduled.Status);
        Assert.Equal(1, scheduled.AttemptCount);
        Assert.NotNull(scheduled.NextRetryAtUtc);
        Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Empty(await ReadOutboxesAsync(db, execution.Id));

        await db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { NextRetryAtUtc = DateTime.UtcNow.AddSeconds(-1) })
            .Where(e => e.Id == execution.Id)
            .ExecuteCommandAsync();

        await worker.ExecuteAsync(JobContext(), CancellationToken.None);

        var recovered = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal(2, transport.SendCount);
        var attempts = await ReadAttemptsAsync(db, execution.Id);
        Assert.Collection(
            attempts,
            first => Assert.Equal(WfNodeExecutionResultType.RetryableFailure, first.ResultType),
            second =>
            {
                Assert.Equal(WfNodeExecutionResultType.Succeeded, second.ResultType);
                Assert.Equal("recovered", second.OutputSummary);
            });
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(WfTokenStatus.Completed, await ReadTokenStatusAsync(db, execution.TokenId));
    }

    [Fact]
    public async Task A_terminal_webhook_failure_stops_at_failed_without_creating_a_task()
    {
        var transport = new SequenceTransport(_ => Response(HttpStatusCode.NotFound, "missing"));
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var started = await StartPublishedWebhookAsync(scope.ServiceProvider, WebhookModel());
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        await RunWorkerAsync(scope.ServiceProvider);

        var failed = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Failed, failed.Status);
        Assert.Equal(48029, failed.ErrorCode);
        Assert.Equal(1, failed.AttemptCount);
        var attempt = Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, attempt.ResultType);
        Assert.Empty(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
    }

    [Fact]
    public async Task A_manual_webhook_failure_creates_a_task_at_the_same_node()
    {
        var transport = new SequenceTransport(_ => Response(HttpStatusCode.NotFound, "manual"));
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var model = WebhookModel(new WfNodeProps
        {
            WebhookUrl = "http://example.com/hook",
            WebhookOnFailure = WfWebhookFailureAction.Manual,
            Assignee = new WfAssignee
            {
                Provider = "user",
                Params = new Dictionary<string, JsonElement>
                {
                    ["userIds"] = JsonSerializer.SerializeToElement(new[] { 1L }),
                },
            },
        });

        var started = await StartPublishedWebhookAsync(scope.ServiceProvider, model);
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        await RunWorkerAsync(scope.ServiceProvider);

        var fallback = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.ManualFallback, fallback.Status);
        var task = Assert.Single(await ReadTasksAsync(db, execution.InstanceId));
        Assert.Equal("webhook", task.NodeId);
        Assert.Equal(execution.TokenId, task.TokenId);
        Assert.Equal(execution.NodeVisitId, task.NodeVisitId);
        Assert.Equal(WfTokenStatus.Active, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Running, await ReadInstanceStatusAsync(db, execution.InstanceId));
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
    }

    [Fact]
    public async Task A_result_commit_crash_after_webhook_call_recovers_with_one_local_advance()
    {
        var transport = new SequenceTransport(_ => Ok("accepted"));
        using var f = NewFactory(transport);
        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var realEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        var started = await StartPublishedWebhookAsync(scope.ServiceProvider, WebhookModel());
        var execution = await SingleExecutionAsync(db, started.InstanceId);
        var handler = new WebhookNodeHandler(
            new HttpClient(transport),
            new AdminJobsOptions(),
            TimeProvider.System);
        var crashingDispatcher = new WfNodeExecutionDispatcher(
            db,
            [handler],
            new CrashBeforeCommitEngine(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => crashingDispatcher.RunAsync(
            execution.Id,
            "worker-a",
            TimeSpan.FromMinutes(5),
            CancellationToken.None));

        var afterCrash = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Running, afterCrash.Status);
        Assert.Equal(1, afterCrash.AttemptCount);
        Assert.Empty(await ReadAttemptsAsync(db, execution.Id));
        Assert.Equal(1, transport.SendCount);

        await ExpireLeaseAsync(db, execution.Id);
        await RunWorkerAsync(scope.ServiceProvider);

        var recovered = await ReadExecutionAsync(db, execution.Id);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal(2, transport.SendCount); // 外部副作用是 at-least-once
        Assert.Single(await ReadAttemptsAsync(db, execution.Id));
        Assert.Single(await ReadOutboxesAsync(db, execution.Id));
        Assert.Equal(WfTokenStatus.Completed, await ReadTokenStatusAsync(db, execution.TokenId));
        Assert.Equal(WfInstanceStatus.Approved, await ReadInstanceStatusAsync(db, execution.InstanceId));
        Assert.Single(await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == execution.InstanceId && h.EventType == WfHistoryEventType.InstanceCompleted)
            .ToListAsync());
    }

    /// <summary>
    /// 重提复用同一个 token，却必须使此前 visit 上正在事务外执行的节点 execution 失效。
    /// 否则旧 handler 的迟到成功会拿着仍然有效的 execution fence，把新 traversal 的 token
    /// 按旧节点的 next 推走。变异：删掉重提时的 active-execution invalidation → 此用例在旧行
    /// 仍为 Running 处失败；删掉 <c>Status == Running</c> 回写条件 → 旧结果会推进 replacement visit。
    /// </summary>
    [Fact]
    public async Task A_resubmit_invalidates_the_old_webhook_execution_before_its_late_result_can_advance_the_new_visit()
    {
        var handler = new BlockingNodeHandler();
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.Insert(
                0,
                ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => handler)),
        };
        _ = f.CreateClient();
        using var workerScope = f.Services.CreateScope();
        using var resubmitScope = f.Services.CreateScope();
        var db = workerScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var started = await StartPublishedWebhookAsync(workerScope.ServiceProvider, WebhookModel());
        var oldExecution = await SingleExecutionAsync(db, started.InstanceId);
        var workerTask = ResolveWorker(workerScope.ServiceProvider)
            .ExecuteAsync(JobContext(), CancellationToken.None);
        await handler.WaitUntilStartedAsync();
        var stalePending = await InsertSupersededExecutionAsync(
            db,
            oldExecution,
            oldExecution.NodeVisitId!.Value - 2,
            WfNodeExecutionStatus.Pending,
            fence: 0,
            nextRetryAtUtc: null);
        var staleRetry = await InsertSupersededExecutionAsync(
            db,
            oldExecution,
            oldExecution.NodeVisitId!.Value - 3,
            WfNodeExecutionStatus.RetryScheduled,
            fence: 7,
            nextRetryAtUtc: DateTime.UtcNow.AddHours(1));

        try
        {
            var resubmitted = await resubmitScope.ServiceProvider.GetRequiredService<IWfInstanceService>()
                .ResubmitAsync(started.InstanceId, 1, null, null, cancellationToken: CancellationToken.None);
            Assert.Equal(WfInstanceStatus.Running, resubmitted.InstanceStatus);

            var oldAfterResubmit = await ReadExecutionAsync(db, oldExecution.Id);
            Assert.Equal(WfNodeExecutionStatus.Cancelled, oldAfterResubmit.Status);
            Assert.True(oldAfterResubmit.Fence > oldExecution.Fence);
            Assert.Empty(await ReadAttemptsAsync(db, oldExecution.Id));
            Assert.Empty(await ReadOutboxesAsync(db, oldExecution.Id));
            await AssertSupersededByResubmitAsync(db, stalePending.Id, expectedFence: 1);
            await AssertSupersededByResubmitAsync(db, staleRetry.Id, expectedFence: 8);

            var replacement = Assert.Single(
                await db.Queryable<WfNodeExecution>()
                    .Where(e => e.InstanceId == started.InstanceId && e.Id != oldExecution.Id)
                    .ToListAsync(),
                e => e.Status == WfNodeExecutionStatus.Pending);
            Assert.NotEqual(oldExecution.NodeVisitId, replacement.NodeVisitId);

            var tokenAfterResubmit = await db.Queryable<WfToken>()
                .Where(t => t.Id == oldExecution.TokenId)
                .FirstAsync();
            Assert.Equal(WfTokenStatus.Active, tokenAfterResubmit.Status);
            Assert.Equal("webhook", tokenAfterResubmit.NodeId);
            Assert.Equal(replacement.NodeVisitId, tokenAfterResubmit.NodeVisitId);

            handler.Succeed();
            await workerTask;

            var tokenAfterLateResult = await db.Queryable<WfToken>()
                .Where(t => t.Id == oldExecution.TokenId)
                .FirstAsync();
            Assert.Equal(tokenAfterResubmit.NodeId, tokenAfterLateResult.NodeId);
            Assert.Equal(tokenAfterResubmit.NodeVisitId, tokenAfterLateResult.NodeVisitId);
            Assert.Equal(tokenAfterResubmit.Version, tokenAfterLateResult.Version);
            Assert.Empty(await ReadAttemptsAsync(db, oldExecution.Id));
            Assert.Empty(await ReadOutboxesAsync(db, oldExecution.Id));

            await RunWorkerAsync(workerScope.ServiceProvider);
            Assert.Equal(2, handler.CallCount);
            var replacementAfterRun = await ReadExecutionAsync(db, replacement.Id);
            Assert.Equal(WfNodeExecutionStatus.Succeeded, replacementAfterRun.Status);
            Assert.Equal(WfTokenStatus.Completed, await ReadTokenStatusAsync(db, oldExecution.TokenId));
        }
        finally
        {
            handler.Succeed();
            if (!workerTask.IsCompleted)
                await workerTask;
        }
    }

    private static WorkflowAppFactory NewFactory(SequenceTransport transport) => new()
    {
        Overrides = services => services.Insert(
            0,
            ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => new WebhookNodeHandler(
                new HttpClient(transport),
                new AdminJobsOptions(),
                TimeProvider.System))),
    };

    private static WorkflowAppFactory NewAiFactory(
        IAiDecisionProvider provider,
        IAiDecisionPolicyEvaluator? policy = null) => new()
    {
        Overrides = services =>
        {
            services.AddScoped<IAiDecisionProvider>(_ => provider);
            if (policy is not null)
                services.AddSingleton<IAiDecisionPolicyEvaluator>(_ => policy);
        },
    };

    private static WfModel WebhookModel(WfNodeProps? props = null) => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Name = "",
            Next = new WfNode
            {
                Id = "webhook",
                Type = WfNodeType.Webhook,
                Name = "webhook",
                Props = props ?? new WfNodeProps { WebhookUrl = "http://example.com/hook" },
            },
        },
    };

    private static WfModel AiDecisionModel() => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "ai",
                Type = WfNodeType.AiDecision,
                Name = "AI review",
                Props = new WfNodeProps
                {
                    Assignee = new WfAssignee
                    {
                        Provider = ApproverProviderKeys.User,
                        Params = new Dictionary<string, JsonElement>
                        {
                            ["userIds"] = JsonSerializer.SerializeToElement(new[] { 1L }),
                        },
                    },
                    AiInstructions = "Review the selected case.",
                    AiInputFields = ["amount"],
                    MaxAttempts = 2,
                },
            },
        },
    };

    private static async Task<WfEngineResult> StartPublishedWebhookAsync(
        IServiceProvider services,
        WfModel model)
    {
        var definitions = services.GetRequiredService<IWfDefinitionService>();
        var definitionId = await definitions.AddAsync(new WfDefinitionInput
        {
            Name = $"Webhook E2E {Guid.NewGuid():N}",
            Model = model,
        });
        await definitions.PublishAsync(definitionId);

        return await services.GetRequiredService<IWfInstanceService>().StartAsync(
            new WfStartInput
            {
                DefinitionId = definitionId,
                BusinessKey = $"e2e-{Guid.NewGuid():N}",
            },
            starterUserId: 1,
            starterOrgId: 1,
            CancellationToken.None);
    }

    private static async Task<WfEngineResult> StartPublishedAiAsync(IServiceProvider services)
    {
        var definitions = services.GetRequiredService<IWfDefinitionService>();
        var definitionId = await definitions.AddAsync(new WfDefinitionInput
        {
            Name = $"AI Decision negative E2E {Guid.NewGuid():N}",
            Model = AiDecisionModel(),
        });
        Assert.Equal(1, await definitions.PublishAsync(definitionId));

        return await services.GetRequiredService<IWfInstanceService>().StartAsync(
            new WfStartInput
            {
                DefinitionId = definitionId,
                BusinessKey = $"ai-negative-{Guid.NewGuid():N}",
                VariablesJson = "{\"amount\":12}",
            },
            starterUserId: 1,
            starterOrgId: 1,
            CancellationToken.None);
    }

    private static async Task<WfNodeExecution?> ClaimAsync(
        ISqlSugarClient db,
        long executionId,
        string owner,
        DateTime nowUtc)
    {
        var transaction = await db.Ado.UseTranAsync(() => WfNodeExecutionStore.ClaimAsync(
            db,
            executionId,
            owner,
            nowUtc,
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        Assert.True(transaction.IsSuccess);
        return transaction.Data;
    }

    private static Task<WfNodeExecutionStatus> RunWorkerAsync(IServiceProvider services) =>
        RunWorkerCoreAsync(services);

    private static async Task<WfNodeExecutionStatus> RunWorkerCoreAsync(IServiceProvider services)
    {
        var worker = ResolveWorker(services);
        await worker.ExecuteAsync(JobContext(), CancellationToken.None);
        return WfNodeExecutionStatus.Succeeded;
    }

    private static WfNodeExecutionJob ResolveWorker(IServiceProvider services) =>
        services.GetServices<IAdminJob>().OfType<WfNodeExecutionJob>().Single();

    private static JobExecutionContext JobContext() => new()
    {
        JobId = 1,
        JobCode = "wf-node-execution-scan",
        JobName = "工作流节点执行扫描",
        FireInstanceId = 1,
        ScheduledTime = DateTime.Now,
        FireTime = DateTime.Now,
    };

    private static async Task<WfNodeExecution> SingleExecutionAsync(ISqlSugarClient db, long instanceId)
    {
        var execution = await db.Queryable<WfNodeExecution>()
            .Where(e => e.InstanceId == instanceId)
            .FirstAsync();
        Assert.NotNull(execution);
        return execution!;
    }

    private static async Task<WfNodeExecution> InsertSupersededExecutionAsync(
        ISqlSugarClient db,
        WfNodeExecution source,
        long nodeVisitId,
        WfNodeExecutionStatus status,
        long fence,
        DateTime? nextRetryAtUtc)
    {
        var row = new WfNodeExecution
        {
            ScopeKey = source.ScopeKey,
            ExecutionKey = WfExecutionKey.Compute(
                source.ScopeKey,
                source.InstanceId,
                source.TokenId,
                nodeVisitId,
                source.NodeId,
                source.DefinitionVersionId),
            InstanceId = source.InstanceId,
            TokenId = source.TokenId,
            NodeVisitId = nodeVisitId,
            NodeId = source.NodeId,
            NodeType = source.NodeType,
            DefinitionVersionId = source.DefinitionVersionId,
            Status = status,
            Fence = fence,
            MaxAttempts = source.MaxAttempts,
            NextRetryAtUtc = nextRetryAtUtc,
        };
        await db.Insertable(row).ExecuteCommandAsync();
        return row;
    }

    private static async Task AssertSupersededByResubmitAsync(
        ISqlSugarClient db,
        long executionId,
        long expectedFence)
    {
        var execution = await ReadExecutionAsync(db, executionId);
        Assert.Equal(WfNodeExecutionStatus.Cancelled, execution.Status);
        Assert.Equal(expectedFence, execution.Fence);
        Assert.Null(execution.LeaseOwner);
        Assert.Null(execution.LeaseExpiresAtUtc);
        Assert.Null(execution.NextRetryAtUtc);
        Assert.NotNull(execution.CompletedTimeUtc);
        Assert.Empty(await ReadAttemptsAsync(db, executionId));
        Assert.Empty(await ReadOutboxesAsync(db, executionId));
    }

    private static Task<WfNodeExecution> ReadExecutionAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfNodeExecution>().Where(e => e.Id == id).FirstAsync();

    private static Task<List<WfNodeExecutionAttempt>> ReadAttemptsAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfNodeExecutionAttempt>()
            .Where(a => a.ExecutionId == id)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();

    private static Task<List<WfOutbox>> ReadOutboxesAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfOutbox>().Where(o => o.ExecutionId == id).ToListAsync();

    private static Task<List<WfTask>> ReadTasksAsync(ISqlSugarClient db, long instanceId) =>
        db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).ToListAsync();

    private static async Task<WfTokenStatus> ReadTokenStatusAsync(ISqlSugarClient db, long id) =>
        (await db.Queryable<WfToken>().Where(t => t.Id == id).FirstAsync()).Status;

    private static async Task<WfInstanceStatus> ReadInstanceStatusAsync(ISqlSugarClient db, long id) =>
        (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().Where(i => i.Id == id).FirstAsync()).Status;

    private static Task<int> ExpireLeaseAsync(ISqlSugarClient db, long id) =>
        db.Updateable<WfNodeExecution>()
            .SetColumns(e => new WfNodeExecution { LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) })
            .Where(e => e.Id == id)
            .ExecuteCommandAsync();

    private sealed class SequenceTransport(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        public Action? OnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            OnSend?.Invoke();
            return Task.FromResult(responseFactory(SendCount));
        }
    }

    private sealed class CrashBeforeCommitEngine : IWorkflowEngine
    {
        public Task<WfEngineResult> ExecuteAsync(
            IWfCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WfEngineResult>(
                new InvalidOperationException("模拟 Webhook 外呼完成后、tx2 提交前崩溃。"));
    }

    private sealed class CancellingAiDecisionProvider(CancellationTokenSource cancellation) : IAiDecisionProvider
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class BlockingNodeHandler : IWorkflowNodeHandler
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<WfNodeExecutionResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WfNodeType NodeType => WfNodeType.Webhook;

        public int CallCount { get; private set; }

        public Task<WfNodeExecutionResult> ExecuteAsync(
            WfNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            started.TrySetResult();
            return completion.Task;
        }

        public Task WaitUntilStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Succeed() => completion.TrySetResult(WfNodeExecutionResult.Succeeded(summary: "late-ok"));
    }

    private static HttpResponseMessage Ok(string body) =>
        Response(HttpStatusCode.OK, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };
}
