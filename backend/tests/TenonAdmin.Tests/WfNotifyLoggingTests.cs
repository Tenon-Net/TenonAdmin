using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 7「通知失败可观测」契约测试。此前通知失败是**七层静默**:`WfDefaultNotifier` 三个方法各吞一次,
/// 四个调用点又各包一层。内层已删(见 <see cref="WfDefaultNotifier"/> 的类注释),失败现在一律浮到调用点的
/// <c>catch</c> 并记一条结构化 Warning。
/// <para>造失败的办法是换掉 <see cref="IRealtimePublisher"/> 让它抛 —— 这样内置通知实现是**原样**跑的,
/// 钉的是真实调用链而不是一个假 Notifier。日志经自制的最小 <see cref="ILoggerProvider"/> 捕获。</para>
/// <para><b>覆盖的四条路各自独立</b>:待办到达与实例完结走引擎的 <c>DispatchPendingNotificationsAsync</c>;
/// 催办与超时提醒**根本不经引擎**,各有各的 <c>catch</c>。少写一处就有一条路继续无声。</para>
/// </summary>
public class WfNotifyLoggingTests
{
    private const string Password = "Test@123456";

    /// <summary>待办到达通知失败 → 审批**仍然成功**,且留下一条带异常的 Warning。</summary>
    [Fact]
    public async Task A_failed_task_assigned_notification_still_approves_and_logs_a_warning()
    {
        var log = new LogSink();
        using var f = NewFactory(log, publisherThrows: true);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nlog-assign-starter");
        var aId = await AddUser(admin, "wf-nlog-assign-a");
        var bId = await AddUser(admin, "wf-nlog-assign-b");
        var definitionId = await Publish(admin, "通知日志-待办到达", ChainModel(aId, bId));

        var starter = await ClientFor(f, "wf-nlog-assign-starter");
        var a = await ClientFor(f, "wf-nlog-assign-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 进入 node2 会给 b 建待办 → 触发待办到达通知 → publisher 抛。
        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        // 两条待办到达通知都失败了(发起时给 a 建待办、同意后给 b 建待办),这是正确行为 ——
        // 断言"至少一条、且每条都带着异常",而不是硬钉数量。
        var assigned = log.Warnings.Where(e => e.Message.Contains("待办到达通知失败")).ToList();
        Assert.NotEmpty(assigned);
        Assert.All(assigned, e => Assert.NotNull(e.Exception));
    }

    /// <summary>实例完结通知失败 → 审批仍成功 + 一条 Warning(走引擎里**另一处** catch)。</summary>
    [Fact]
    public async Task A_failed_instance_completed_notification_still_approves_and_logs_a_warning()
    {
        var log = new LogSink();
        using var f = NewFactory(log, publisherThrows: true);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nlog-done-starter");
        var aId = await AddUser(admin, "wf-nlog-done-a");
        var definitionId = await Publish(admin, "通知日志-实例完结", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-nlog-done-starter");
        var a = await ClientFor(f, "wf-nlog-done-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var entry = Assert.Single(log.Warnings, e => e.Message.Contains("实例完结通知失败"));
        Assert.NotNull(entry.Exception);
    }

    /// <summary>
    /// 催办通知失败 → `urge` **仍返回成功** + 一条 Warning。
    /// 催办不进引擎,所以它证明的是「引擎那一处不够」——少了这处日志,这条路会继续无声。
    /// </summary>
    [Fact]
    public async Task A_failed_urge_notification_still_succeeds_and_logs_a_warning()
    {
        var log = new LogSink();
        using var f = NewFactory(log, publisherThrows: true);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nlog-urge-starter");
        var aId = await AddUser(admin, "wf-nlog-urge-a");
        var definitionId = await Publish(admin, "通知日志-催办", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-nlog-urge-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId });
        Assert.Equal(0, urge.GetProperty("code").GetInt32());

        var entry = Assert.Single(log.Warnings, e => e.Message.Contains("催办通知失败"));
        Assert.NotNull(entry.Exception);
    }

    /// <summary>
    /// 超时提醒通知失败 → 扫描**不中断** + 一条 Warning。第二条不经引擎的路。
    /// </summary>
    [Fact]
    public async Task A_failed_timeout_remind_notification_does_not_break_the_scan_and_logs_a_warning()
    {
        var log = new LogSink();
        using var f = NewFactory(log, publisherThrows: true);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nlog-remind-starter");
        var aId = await AddUser(admin, "wf-nlog-remind-a");
        var definitionId = await Publish(
            admin, "通知日志-超时提醒", SingleApprovalModel(aId, new { hours = 1, action = "remind" }));

        var starter = await ClientFor(f, "wf-nlog-remind-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await ExpireDueTime(f, instanceId);
        await RunTimeoutJob(f);

        var entry = Assert.Single(log.Warnings, e => e.Message.Contains("超时提醒通知失败"));
        Assert.NotNull(entry.Exception);

        // 扫描没被通知异常打断:提醒事件仍然写进了历史。提醒记的是 TimeoutFired
        // (载荷里 action = Remind),不是 TaskUrged —— 催办与超时提醒共用通知方法,但事件类型不同。
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.True(await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.TimeoutFired)
            .AnyAsync());
    }

    /// <summary>
    /// 通知**正常**时一条本类 Warning 都不记。
    /// <para><b>这条不能省</b>:没有它,一个「无论成败都记一条」的实现能让上面四条全绿,
    /// 而那样的日志等于噪声,排障时反而更糟。</para>
    /// </summary>
    [Fact]
    public async Task A_successful_notification_logs_no_warning()
    {
        var log = new LogSink();
        using var f = NewFactory(log, publisherThrows: false);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-nlog-ok-starter");
        var aId = await AddUser(admin, "wf-nlog-ok-a");
        var definitionId = await Publish(admin, "通知日志-正常", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-nlog-ok-starter");
        var a = await ClientFor(f, "wf-nlog-ok-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal(0, (await PostEnvelope(starter, "/api/v1/workflow/task/urge", new { taskId }))
            .GetProperty("code").GetInt32());
        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        Assert.DoesNotContain(log.Warnings, e => e.Message.Contains("通知失败"));
    }

    // ── 辅助 ──

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>最小日志捕获:只留 Warning 及以上,够断言就行,不引第三方断言库。</summary>
    private sealed class LogSink : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IEnumerable<LogEntry> Warnings => _entries.Where(e => e.Level >= LogLevel.Warning);

        public ILogger CreateLogger(string categoryName) => new SinkLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class SinkLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>每次推送都抛的 publisher —— 让**内置**通知实现原样跑到失败,而不是替换掉 Notifier。</summary>
    private sealed class ThrowingRealtimePublisher : IRealtimePublisher
    {
        public Task NotifyUserAsync(
            long userId, string @event, object? data = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"实时通道故意失败:{@event}");

        public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"实时通道故意失败:{@event}");

        public Task NotifySessionAsync(
            string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"实时通道故意失败:{@event}");
    }

    private static WorkflowAppFactory NewFactory(LogSink log, bool publisherThrows) => new()
    {
        Overrides = services =>
        {
            services.AddSingleton<ILoggerProvider>(log);
            if (publisherThrows)
                services.RemoveAll<IRealtimePublisher>();
            if (publisherThrows)
                services.AddSingleton<IRealtimePublisher, ThrowingRealtimePublisher>();
        },
    };

    private static async Task ExpireDueTime(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var past = DateTime.Now - TimeSpan.FromHours(2);
        var affected = await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { DueTime = past })
            .Where(t => t.InstanceId == instanceId)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有活跃待办可推到期——测试前置条件坏了。");
    }

    private static async Task RunTimeoutJob(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IAdminJob>().OfType<WfTimeoutJob>().Single();
        var now = DateTime.Now;
        await job.ExecuteAsync(
            new JobExecutionContext
            {
                JobId = 1,
                JobCode = "wf-timeout-scan",
                JobName = "流程超时扫描",
                FireInstanceId = 1,
                ScheduledTime = now,
                FireTime = now,
                Log = _ => { },
            },
            CancellationToken.None);
    }

    private static object SingleApprovalModel(long userId, object? timeout = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
            },
            ["mode"] = "any",
        };
        if (timeout is not null)
            props["timeout"] = timeout;

        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new { id = "node1", type = "approval", name = "node1", props, next = (object?)null },
            },
        };
    }

    private static object ChainModel(long aUserId, long bUserId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "node1",
                type = "approval",
                name = "node1",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
                    },
                    mode = "any",
                },
                next = new
                {
                    id = "node2",
                    type = "approval",
                    name = "node2",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { bUserId } },
                        },
                        mode = "any",
                    },
                    next = (object?)null,
                },
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
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
