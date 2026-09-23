using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 人工审批并发:同一待办的重复办理、或签/会签,以及撤销在「已确认无人同意」之后
/// 与一次已提交的同意交错。撤销不能在留下 Approve 历史的同时把实例打成 Cancelled。
/// </summary>
public class WfHumanApprovalConcurrencyTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Cancel_does_not_overwrite_a_committed_approval_on_the_next_node()
    {
        var gate = new ApprovalRaceGate { PauseCancel = true };
        using var f = NewFactory(gate);
        await EnableSqliteWalAsync(f);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-cancel-chain-starter");
        var aId = await AddUser(admin, "wf-ha-cancel-chain-a");
        var bId = await AddUser(admin, "wf-ha-cancel-chain-b");
        var definitionId = await Publish(admin, "并发-撤销对中途同意", Chain([aId], bId));
        var starter = await ClientFor(f, "wf-ha-cancel-chain-starter");
        var approver = await ClientFor(f, "wf-ha-cancel-chain-a");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var cancelTask = PostSafe(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        await gate.WaitEnteredAsync();
        CallResult approve;
        try
        {
            approve = await PostSafe(approver, "/api/v1/workflow/task/approve", new { taskId });
        }
        finally
        {
            gate.Release();
        }

        var cancel = await cancelTask;
        await AssertExclusiveCancelAndApproveAsync(f, instanceId, cancel, approve);
    }

    [Fact]
    public async Task Cancel_does_not_overwrite_a_committed_nonfinal_countersign_vote()
    {
        var gate = new ApprovalRaceGate { PauseCancel = true };
        using var f = NewFactory(gate);
        await EnableSqliteWalAsync(f);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-cancel-all-starter");
        var aId = await AddUser(admin, "wf-ha-cancel-all-a");
        var bId = await AddUser(admin, "wf-ha-cancel-all-b");
        var definitionId = await Publish(admin, "并发-撤销对会签首票", All(aId, bId));
        var starter = await ClientFor(f, "wf-ha-cancel-all-starter");
        var approver = await ClientFor(f, "wf-ha-cancel-all-a");

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var cancelTask = PostSafe(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        await gate.WaitEnteredAsync();
        CallResult approve;
        try
        {
            approve = await PostSafe(approver, "/api/v1/workflow/task/approve", new { taskId });
        }
        finally
        {
            gate.Release();
        }

        var cancel = await cancelTask;
        await AssertExclusiveCancelAndApproveAsync(f, instanceId, cancel, approve);
        if (approve.Ok)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            Assert.Equal(1, await db.Queryable<WfTask>().CountAsync(t => t.InstanceId == instanceId));
        }
    }

    [Fact]
    public async Task Or_sign_concurrent_approves_advance_once()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-any-starter");
        var aId = await AddUser(admin, "wf-ha-any-a");
        var bId = await AddUser(admin, "wf-ha-any-b");
        var cId = await AddUser(admin, "wf-ha-any-c");
        var definitionId = await Publish(admin, "并发-或签", Chain([aId, bId], cId));
        var starter = await ClientFor(f, "wf-ha-any-starter");
        var a = await ClientFor(f, "wf-ha-any-a");
        var b = await ClientFor(f, "wf-ha-any-b");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var results = await Task.WhenAll(
            PostSafe(a, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-any-a" }),
            PostSafe(b, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-any-b" }));

        Assert.True(results.Count(result => result.Ok) == 1, Describe(results));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfHisTask>()
            .CountAsync(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve));
        var tasks = await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal("approve-2", task.NodeId);
        Assert.Equal(WfInstanceStatus.Running,
            (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId))!.Status);
    }

    [Fact]
    public async Task All_sign_concurrent_approves_record_one_vote_until_retry()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-all-starter");
        var aId = await AddUser(admin, "wf-ha-all-a");
        var bId = await AddUser(admin, "wf-ha-all-b");
        var definitionId = await Publish(admin, "并发-会签", All(aId, bId));
        var starter = await ClientFor(f, "wf-ha-all-starter");
        var a = await ClientFor(f, "wf-ha-all-a");
        var b = await ClientFor(f, "wf-ha-all-b");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var results = await Task.WhenAll(
            PostSafe(a, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-all-a" }),
            PostSafe(b, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-all-b" }));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var approves = await db.Queryable<WfHisTask>()
            .CountAsync(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve);
        var successes = results.Count(result => result.Ok);
        // 任务版本把两次投票串行化。后启动的请求若读到已提交版本,两票可以都成功;
        // 读到旧版本则一票冲突,冲突方重试后补上。两种都不得多记投票。
        Assert.InRange(successes, 1, 2);
        Assert.Equal(successes, approves);
        if (successes == 2)
        {
            Assert.Equal(WfInstanceStatus.Approved,
                (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId))!.Status);
            return;
        }

        Assert.Equal(WfInstanceStatus.Running,
            (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId))!.Status);
        var loser = results[0].Ok ? b : a;
        var loserRequest = results[0].Ok ? "wf-ha-all-b-retry" : "wf-ha-all-a-retry";
        var retried = await PostSafe(loser, "/api/v1/workflow/task/approve",
            new { taskId, requestId = loserRequest });
        Assert.True(retried.Ok, retried.Error);
        Assert.Equal(2, await db.Queryable<WfHisTask>()
            .CountAsync(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve));
        Assert.Equal(WfInstanceStatus.Approved,
            (await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId))!.Status);
    }

    [Fact]
    public async Task Same_request_id_does_not_approve_twice()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-idem-starter");
        var aId = await AddUser(admin, "wf-ha-idem-a");
        var bId = await AddUser(admin, "wf-ha-idem-b");
        var definitionId = await Publish(admin, "并发-同一请求键", Chain([aId], bId));
        var starter = await ClientFor(f, "wf-ha-idem-starter");
        var a1 = await ClientFor(f, "wf-ha-idem-a");
        var a2 = await ClientFor(f, "wf-ha-idem-a");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var results = await Task.WhenAll(
            PostSafe(a1, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-idem-same" }),
            PostSafe(a2, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-idem-same" }));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var approves = await db.Queryable<WfHisTask>()
            .CountAsync(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve && h.TaskId == taskId);
        Assert.True(approves == 1, Describe(results) + $" approves={approves}");
        Assert.Contains(results, result => result.Ok);
        Assert.Equal(1, await db.Queryable<WfTask>().CountAsync(t => t.InstanceId == instanceId));
    }

    [Fact]
    public async Task Return_and_approve_do_not_both_commit()
    {
        var gate = new ApprovalRaceGate { PauseReturn = true };
        using var f = NewFactory(gate);
        await EnableSqliteWalAsync(f);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-ha-return-starter");
        var aId = await AddUser(admin, "wf-ha-return-a");
        var bId = await AddUser(admin, "wf-ha-return-b");
        var definitionId = await Publish(admin, "并发-退回对同意", AnyReturn(aId, bId));
        var starter = await ClientFor(f, "wf-ha-return-starter");
        var a = await ClientFor(f, "wf-ha-return-a");
        var b = await ClientFor(f, "wf-ha-return-b");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = started.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTask = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        var moved = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = firstTask });
        Assert.Equal(0, moved.GetProperty("code").GetInt32());
        var taskId = moved.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var returner = await ClientFor(f, "wf-ha-return-b");
        var returnTask = PostSafe(returner, "/api/v1/workflow/task/return", new { taskId });
        await gate.WaitEnteredAsync();
        CallResult approve;
        try
        {
            approve = await PostSafe(b, "/api/v1/workflow/task/approve", new { taskId, requestId = "wf-ha-return-approve" });
        }
        finally
        {
            gate.Release();
        }

        var returned = await returnTask;
        Assert.False(approve.Ok && returned.Ok, Describe([returned, approve]));
        Assert.True(approve.Ok || returned.Ok, Describe([returned, approve]));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var approves = await db.Queryable<WfHisTask>()
            .CountAsync(h => h.TaskId == taskId && h.Action == WfTaskAction.Approve);
        var returns = await db.Queryable<WfHisTask>()
            .CountAsync(h => h.TaskId == taskId && h.Action == WfTaskAction.Return);
        Assert.Equal(1, approves + returns);
        Assert.Equal(approve.Ok ? 1 : 0, approves);
        Assert.Equal(returned.Ok ? 1 : 0, returns);
    }

    private static string Describe(IEnumerable<CallResult> results) =>
        string.Join(" | ", results.Select(result => $"{result.Ok}:{result.Code}:{result.Error}"));

    private static async Task AssertExclusiveCancelAndApproveAsync(
        WorkflowAppFactory f,
        long instanceId,
        CallResult cancel,
        CallResult approve)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().InSingleAsync(instanceId);
        Assert.NotNull(instance);
        var approves = await db.Queryable<WfHisTask>()
            .CountAsync(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve);
        Assert.False(
            instance!.Status == WfInstanceStatus.Cancelled && approves > 0,
            $"cancelOk={cancel.Ok} cancelCode={cancel.Code} approveOk={approve.Ok} approveCode={approve.Code} " +
            $"status={instance.Status} approves={approves} cancelError={cancel.Error} approveError={approve.Error}");

        if (approve.Ok)
        {
            Assert.False(cancel.Ok);
            Assert.NotEqual(WfInstanceStatus.Cancelled, instance.Status);
            Assert.True(approves >= 1);
            return;
        }

        Assert.True(cancel.Ok, cancel.Error);
        Assert.Equal(WfInstanceStatus.Cancelled, instance.Status);
        Assert.Equal(0, approves);
    }

    private static WorkflowAppFactory NewFactory(ApprovalRaceGate gate) => new()
    {
        Overrides = services =>
        {
            services.AddSingleton(gate);
            services.AddScoped<IWorkflowEngine>(sp =>
                ActivatorUtilities.CreateInstance<RacingWorkflowEngine>(sp));
        },
    };

    private static async Task EnableSqliteWalAsync(WorkflowAppFactory f)
    {
        if (TestDb.UseMySql || TestDb.UseSqlServer || TestDb.UsePostgreSql)
            return;

        _ = f.CreateClient();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Ado.GetScalarAsync("PRAGMA journal_mode=WAL;");
    }

    private static object Chain(long[] firstUsers, long second, string modeA = "any") => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "一级",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = firstUsers },
                    },
                    mode = modeA,
                },
                next = new
                {
                    id = "approve-2",
                    type = "approval",
                    name = "二级",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { second } },
                        },
                        mode = "any",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    private static object All(long a, long b) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "会签",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { a, b } },
                    },
                    mode = "all",
                },
                next = (object?)null,
            },
        },
    };

    private static object AnyReturn(long a, long b) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "一级",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { a } },
                    },
                    mode = "any",
                    returnPolicy = "prev",
                },
                next = new
                {
                    id = "approve-2",
                    type = "approval",
                    name = "二级",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { a, b } },
                        },
                        mode = "any",
                        returnPolicy = "prev",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account,
            password = Password,
            name = account,
            enabled = true,
            orgId = 1,
            roleIds = new[] { 2L },
        });
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private static async Task<CallResult> PostSafe(HttpClient client, string path, object body)
    {
        try
        {
            var env = await PostEnvelope(client, path, body);
            var code = env.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : -1;
            return new CallResult(code == 0, code, null);
        }
        catch (Exception ex)
        {
            return new CallResult(false, -1, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private sealed record CallResult(bool Ok, int Code, string? Error);

    private sealed class ApprovalRaceGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool PauseCancel { get; init; }

        public bool PauseReturn { get; init; }

        public Task WaitEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(40));

        public void Release() => _release.TrySetResult();

        public async Task EnterAsync()
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(40));
        }
    }

    private sealed class RacingWorkflowEngine : WorkflowEngine
    {
        private readonly ApprovalRaceGate _gate;
        private bool _pauseAgenda;

        public RacingWorkflowEngine(
            ApprovalRaceGate gate,
            IRepository<WfInstance> instances,
            IApproverResolver approverResolver,
            IWorkflowFormBinder formBinder,
            WorkflowOptions options,
            TimeProvider timeProvider,
            IWfConditionEvaluator conditionEvaluator,
            IWorkflowNotifier notifier,
            IWfOperationReceiptService receipts,
            ILogger<WorkflowEngine> logger,
            IIdGenerator idGenerator,
            IWfDelegationService? delegation = null)
            : base(
                instances,
                approverResolver,
                formBinder,
                options,
                timeProvider,
                conditionEvaluator,
                notifier,
                receipts,
                logger,
                idGenerator,
                delegation)
        {
            _gate = gate;
        }

        protected override async Task<WfExecutionContext> BeginCancelAsync(
            ISqlSugarClient db,
            CancelInstanceCmd cmd,
            CancellationToken cancellationToken)
        {
            var ctx = await base.BeginCancelAsync(db, cmd, cancellationToken);
            if (_gate.PauseCancel)
                _pauseAgenda = true;
            return ctx;
        }

        protected override async Task<WfExecutionContext> BeginReturnAsync(
            ISqlSugarClient db,
            ReturnTaskCmd cmd,
            CancellationToken cancellationToken)
        {
            var ctx = await base.BeginReturnAsync(db, cmd, cancellationToken);
            if (_gate.PauseReturn)
                _pauseAgenda = true;
            return ctx;
        }

        protected override async Task RunAgendaAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
        {
            if (_pauseAgenda)
            {
                _pauseAgenda = false;
                await _gate.EnterAsync();
            }

            await base.RunAgendaAsync(ctx, cancellationToken);
        }
    }
}
