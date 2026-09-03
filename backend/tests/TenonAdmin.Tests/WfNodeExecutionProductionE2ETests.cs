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

    private static WorkflowAppFactory NewFactory(SequenceTransport transport) => new()
    {
        Overrides = services => services.Insert(
            0,
            ServiceDescriptor.Scoped<IWorkflowNodeHandler>(_ => new WebhookNodeHandler(
                new HttpClient(transport),
                new AdminJobsOptions(),
                TimeProvider.System))),
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

    private static HttpResponseMessage Ok(string body) =>
        Response(HttpStatusCode.OK, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };
}
