using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 1「<see cref="IWorkflowNotifier"/> 落地」调用点契约测试。用捕获式 fake 前置替换
/// <see cref="IWorkflowNotifier"/>(<see cref="WorkflowAppFactory.Overrides"/>,仿
/// <c>ReplaceabilityTests</c> 里 <c>FakeRealtimePublisher</c> 的 <c>Replace</c> 写法),锁三个调用点:
/// <see cref="EnterNodeOp.CreateTaskAsync"/> 建任务、<see cref="TakeTransitionOp"/> 的 Approved 完结分支、
/// <see cref="CompleteTaskOp.RejectInstanceAsync"/> 的 Rejected 完结分支。另外用一个抛异常的
/// <see cref="IRealtimePublisher"/> fake 钉住 <see cref="WfDefaultNotifier"/> 的 try/catch:通知失败不得
/// 拖垮审批事务。
/// </summary>
public class WorkflowNotifierTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Approve_flow_notifies_task_assigned_and_instance_completed()
    {
        var capturing = new CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-notify-starter");
        var approverId = await AddUser(admin, "wf-notify-approver");
        var definitionId = await Publish(admin, "通知-审批", UserApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-notify-starter");
        var approver = await ClientFor(f, "wf-notify-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 建任务已触发一次 TaskAssignedAsync,目标是审批人;此时实例还没完结。
        var assignedCall = Assert.Single(capturing.TaskAssignedCalls);
        Assert.Equal(new[] { approverId }, assignedCall.UserIds);
        Assert.Equal((int)WfInstanceStatus.Running, (int)assignedCall.Ctx.Status);
        Assert.Empty(capturing.InstanceCompletedCalls);

        var approve = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // Approved 完结出口(TakeTransitionOp.CompleteInstanceAsync)必须调一次 InstanceCompletedAsync,
        // 且 Status 必须是 Approved(不是硬编码/默认值)。
        var completedCall = Assert.Single(capturing.InstanceCompletedCalls);
        Assert.Equal(WfInstanceStatus.Approved, completedCall.Status);
    }

    [Fact]
    public async Task Sequential_two_approvers_first_task_assigned_call_targets_only_first_approver()
    {
        var capturing = new CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-notify-seq-starter");
        var firstId = await AddUser(admin, "wf-notify-seq-first");
        var secondId = await AddUser(admin, "wf-notify-seq-second");
        var definitionId = await Publish(admin, "通知-顺序会签", SequentialApprovalModel(firstId, secondId));

        var starter = await ClientFor(f, "wf-notify-seq-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());

        // 顺序会签建任务时只有第一位是 Pending,通知只该指向第一位,不是全体办理人。
        var assignedCall = Assert.Single(capturing.TaskAssignedCalls);
        Assert.Equal(new[] { firstId }, assignedCall.UserIds);
    }

    [Fact]
    public async Task Sequential_promotion_notifies_task_assigned_for_next_approver()
    {
        var capturing = new CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-notify-promo-starter");
        var firstId = await AddUser(admin, "wf-notify-promo-first");
        var secondId = await AddUser(admin, "wf-notify-promo-second");
        var definitionId = await Publish(admin, "通知-顺序晋级", SequentialApprovalModel(firstId, secondId));

        var starter = await ClientFor(f, "wf-notify-promo-starter");
        var first = await ClientFor(f, "wf-notify-promo-first");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 建任务只通知了第一位;此时还没有针对第二位的调用。
        Assert.Single(capturing.TaskAssignedCalls);

        var approve = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        // 第一位批准后晋级第二位(CompleteTaskOp.TryPassAsync 的 Sequential 分支)必须触发一次
        // TaskAssignedAsync,目标是第二位——这是 P1 finding 的回归点,没有这行代码本用例应当失败。
        Assert.Equal(2, capturing.TaskAssignedCalls.Count);
        var promotedCall = capturing.TaskAssignedCalls[1];
        Assert.Equal(new[] { secondId }, promotedCall.UserIds);
    }

    [Fact]
    public async Task Reject_flow_notifies_instance_completed()
    {
        var capturing = new CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-notify-reject-starter");
        var approverId = await AddUser(admin, "wf-notify-reject-approver");
        var definitionId = await Publish(admin, "通知-拒绝", UserApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-notify-reject-starter");
        var approver = await ClientFor(f, "wf-notify-reject-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Empty(capturing.InstanceCompletedCalls);

        var reject = await PostEnvelope(approver, "/api/v1/workflow/task/reject",
            new { taskId, comment = "不同意" });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Rejected,
            reject.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // Rejected 完结出口(CompleteTaskOp.RejectInstanceAsync)——本任务最容易漏测的调用点。
        // Status 必须真的是 Rejected(不是与 Approve 分支共用同一硬编码值)。
        var completedCall = Assert.Single(capturing.InstanceCompletedCalls);
        Assert.Equal(WfInstanceStatus.Rejected, completedCall.Status);
    }

    [Fact]
    public async Task Cancel_flow_notifies_instance_completed()
    {
        var capturing = new CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-notify-cancel-starter";
        await AddUser(admin, starterAccount);
        var approverId = await AddUser(admin, "wf-notify-cancel-approver");
        var definitionId = await Publish(admin, "通知-撤销", UserApprovalModel(approverId));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());

        // 撤销完结出口(CancelInstanceOp)必须调一次 InstanceCompletedAsync,Status 必须是 Cancelled。
        var completedCall = Assert.Single(capturing.InstanceCompletedCalls);
        Assert.Equal(WfInstanceStatus.Cancelled, completedCall.Status);
    }

    [Fact]
    public async Task Publisher_throwing_does_not_fail_approval_transaction()
    {
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IRealtimePublisher>(new ThrowingRealtimePublisher())),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-notify-throw-starter");
        var approverId = await AddUser(admin, "wf-notify-throw-approver");
        var definitionId = await Publish(admin, "通知-异常吞掉", UserApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-notify-throw-starter");
        var approver = await ClientFor(f, "wf-notify-throw-approver");

        // 发起本身就走 TaskAssignedAsync → 真实(非 capturing)WfDefaultNotifier → 抛异常的 publisher;
        // 若 try/catch 被去掉,这条请求本身就会变成非 0 code / 500。
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
    }

    private static object UserApprovalModel(long userId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object SequentialApprovalModel(long firstId, long secondId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "顺序审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { firstId, secondId } },
                    },
                    mode = "seq",
                },
                next = (object?)null,
            },
        },
    };

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var id = await AddDefinition(admin, name, model);
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<long> AddDefinition(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        return added.GetProperty("data").GetInt64();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    internal sealed class CapturingWorkflowNotifier : IWorkflowNotifier
    {
        public List<(WfNotifyContext Ctx, IReadOnlyList<long> UserIds)> TaskAssignedCalls { get; } = [];
        public List<WfNotifyContext> InstanceCompletedCalls { get; } = [];
        public List<(WfNotifyContext Ctx, long TaskId, long? FromUserId, IReadOnlyList<long> ToUserIds)> TaskUrgedCalls { get; } = [];

        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default)
        {
            TaskAssignedCalls.Add((ctx, userIds));
            return Task.CompletedTask;
        }

        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default)
        {
            InstanceCompletedCalls.Add(ctx);
            return Task.CompletedTask;
        }

        public Task TaskUrgedAsync(
            WfNotifyContext ctx,
            long taskId,
            long? fromUserId,
            IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default)
        {
            TaskUrgedCalls.Add((ctx, taskId, fromUserId, toUserIds));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRealtimePublisher : IRealtimePublisher
    {
        public Task NotifyUserAsync(
            long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");

        public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");

        public Task NotifySessionAsync(
            string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}
