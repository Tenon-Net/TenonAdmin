extern alias integrationhost;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.Services;
using DemoPartnerDedupeOnlyAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerDedupeOnlyAdapter;
using DemoPartnerDeliveryAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerDeliveryAdapter;
using DemoPartnerNoDedupeAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerNoDedupeAdapter;
using DemoPartnerQueryOnlyAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerQueryOnlyAdapter;
using DemoTicket = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicket;
using DemoTicketPayload = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicketPayload;
using DemoTicketService = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicketService;
using IDemoTicketService = integrationhost::TenonAdmin.IntegrationTestHost.IDemoTicketService;
using static TenonAdmin.Tests.IntegrationTestSupport;

namespace TenonAdmin.Tests;

/// <summary>收集告警的出口(验证告警扩展点;可选地模拟出口故障)。</summary>
internal sealed class CapturingAlertSink(bool fail = false) : IDeliveryAlertSink
{
    public ConcurrentQueue<DeliveryAlert> Alerts { get; } = new();

    public Task NotifyAsync(DeliveryAlert alert, CancellationToken cancellationToken = default)
    {
        Alerts.Enqueue(alert);
        return fail ? Task.FromException(new InvalidOperationException("sentinel-alert-secret")) : Task.CompletedTask;
    }
}

internal sealed class ThrowingDeliveryAdapter : IDeliveryAdapter
{
    public const string AdapterName = "throwing";
    public string Name => AdapterName;
    public bool SupportsIdempotency => false;
    public bool SupportsQuery => false;
    public Task<DeliverySendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken) =>
        Task.FromException<DeliverySendResult>(new InvalidOperationException("sentinel-adapter-secret"));
    public Task<DeliveryQueryResult> QueryAsync(DeliveryContext context, CancellationToken cancellationToken) =>
        Task.FromException<DeliveryQueryResult>(new InvalidOperationException("sentinel-adapter-secret"));
}

/// <summary>
/// G09 投递恢复、核对与管理:本地可控第三方 + 可拨时钟下,验证去重/查询能力决定的恢复、响应丢失、异步受理、
/// 次数与时间上限、栅栏挡住迟到结果、双宿主争抢、人工操作的服务端复检与幂等标识不变、告警、保留与任务种子。
/// </summary>
public class IntegrationDeliveryDispatchTests(MockPartnerFixture mock) : IClassFixture<MockPartnerFixture>
{
    private const string Dedupe = DemoPartnerDeliveryAdapter.AdapterName;
    private const string QueryOnly = DemoPartnerQueryOnlyAdapter.AdapterName;
    private const string NoDedupe = DemoPartnerNoDedupeAdapter.AdapterName;
    private const string DedupeOnly = DemoPartnerDedupeOnlyAdapter.AdapterName;

    private IntegrationAppFactory NewHost(
        MutableTime clock, CapturingAlertSink? sink = null, Action<Dictionary<string, string?>>? extra = null,
        Action<IServiceCollection>? overrides = null, string? dbPath = null, bool reset = true, int? workerId = null, bool resetMock = true)
    {
        if (resetMock) mock.State.Reset();
        var settings = new Dictionary<string, string?>();
        MockPartnerFixture.AddTarget(settings, "partner", $"{mock.BaseUrl}/api/", "Bearer", MockPartnerFixture.Token, timeoutSeconds: 3);
        extra?.Invoke(settings);
        return new IntegrationAppFactory
        {
            DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"tenon-itg-dlv-{Guid.NewGuid():N}.db"),
            ResetDatabase = reset,
            WorkerId = workerId,
            DeleteDbOnDispose = dbPath is null,
            Settings = settings,
            SchemaNeutral = true,
            Overrides = services =>
            {
                IntegrationTestSupport.UseClock(clock)(services);
                if (sink is not null) services.AddSingleton<IDeliveryAlertSink>(sink);
                overrides?.Invoke(services);
            },
        };
    }

    private static Task<DeliveryEnqueueResult> EnqueueAsync(IntegrationAppFactory f, string adapter, string key, int? maxAttempts = null, TimeSpan? maxAge = null) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(new DeliveryRequest
        {
            Adapter = adapter, Operation = "ticket.create", DeliveryKey = key, Payload = new DemoTicketPayload("工单 " + key, 10m),
            MaxAttempts = maxAttempts, MaxAge = maxAge, Standalone = true,
        }));

    private static Task<DeliveryDispatchSummary> RunAsync(IntegrationAppFactory f) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryDispatcher>().RunOnceAsync());

    private static Task<IntegrationDelivery> RowAsync(IntegrationAppFactory f, string key) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationDelivery>().Where(d => d.DeliveryKey == key).FirstAsync());

    private static Task<List<IntegrationDeliveryAttempt>> AttemptsAsync(IntegrationAppFactory f, long id) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationDeliveryAttempt>().Where(a => a.DeliveryId == id).OrderBy(a => a.Id).ToListAsync());

    private static string[] Kinds(IEnumerable<IntegrationDeliveryAttempt> attempts) =>
        attempts.Select(a => $"{a.Kind}:{a.Outcome}").ToArray();

    private int Executions(string key) => mock.Executions(key);

    private int Requests(string route) => mock.State.Requests.TryGetValue(route, out var n) ? n : 0;

    private string[] SentKeys() =>
        mock.State.ReceivedHeaders.Where(h => h.ContainsKey("Idempotency-Key")).Select(h => h["Idempotency-Key"]).ToArray();

    [Fact]
    public async Task A_delivery_is_sent_once_with_its_key_and_every_call_is_traceable()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        var enqueued = await EnqueueAsync(f, Dedupe, "ok-1");

        var summary = await RunAsync(f);
        Assert.Equal(1, summary.Sent);
        Assert.Equal(1, summary.Succeeded);

        var row = await RowAsync(f, "ok-1");
        Assert.Equal(DeliveryStatus.Succeeded, row.Status);
        Assert.StartsWith("T", row.RemoteReference);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.Null(row.LeaseUntilUtc);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(1, Executions("ok-1"));
        Assert.Equal(["ok-1"], SentKeys());

        var attempt = Assert.Single(await AttemptsAsync(f, enqueued.Id));
        Assert.Equal(DeliveryAttemptKind.Send, attempt.Kind);
        Assert.Equal(nameof(OutboundOutcome.Succeeded), attempt.Outcome);
        Assert.Equal(201, attempt.HttpStatus);
        var log = await InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationOutboundLog>().Where(l => l.CallId == attempt.CallId).FirstAsync());
        Assert.Equal(enqueued.Id, log.DeliveryId);
        Assert.Equal(1, log.AttemptNo);
        Assert.Equal("ticket.create", log.Operation);

        Assert.Equal(0, (await RunAsync(f)).Scanned);   // 成功即终态,不再被扫描
    }

    [Fact]
    public async Task A_lost_response_is_resolved_by_partner_capability_and_never_duplicates_business()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        mock.Script("tickets", "drop");
        mock.Script("tickets-nodedupe", "drop", "drop");
        await EnqueueAsync(f, Dedupe, "lost-dedupe");
        await EnqueueAsync(f, QueryOnly, "lost-query");
        await EnqueueAsync(f, NoDedupe, "lost-none");

        await RunAsync(f);   // 三条都在对方执行后断开:结果未知
        Assert.Equal(DeliveryStatus.Pending, (await RowAsync(f, "lost-dedupe")).Status);
        var verifying = await RowAsync(f, "lost-query");
        Assert.Equal(DeliveryStatus.Pending, verifying.Status);
        Assert.True(verifying.VerifyBeforeSend);
        var reconcile = await RowAsync(f, "lost-none");
        Assert.Equal(DeliveryStatus.NeedsReconciliation, reconcile.Status);
        Assert.Equal(NoDedupe, Assert.Single(sink.Alerts).Adapter);

        clock.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(f);

        // 去重:同一幂等标识重发,对方回放首次结果——业务只执行一次
        var dedupe = await RowAsync(f, "lost-dedupe");
        Assert.Equal(DeliveryStatus.Succeeded, dedupe.Status);
        Assert.Equal(1, Executions("lost-dedupe"));
        Assert.Equal(["Send:Unknown", "Send:Succeeded"], Kinds(await AttemptsAsync(f, dedupe.Id)));
        Assert.Equal(2, SentKeys().Count(k => k == "lost-dedupe"));

        // 可查询:先查再定,查到已完成即成功,不再发送
        var queried = await RowAsync(f, "lost-query");
        Assert.Equal(DeliveryStatus.Succeeded, queried.Status);
        Assert.Equal(1, Executions("lost-query"));
        Assert.Equal(["Send:Unknown", "Query:Succeeded"], Kinds(await AttemptsAsync(f, queried.Id)));

        // 两者皆无:停在待核对,不会再被自动发送
        Assert.Equal(DeliveryStatus.NeedsReconciliation, (await RowAsync(f, "lost-none")).Status);
        Assert.Equal(1, Executions("lost-none"));
        Assert.Equal(2, Requests("tickets-nodedupe"));   // 仅首次发送(lost-query 与 lost-none 各一次)
    }

    [Fact]
    public async Task A_503_after_commit_is_unknown_and_is_not_resent_without_a_safety_capability()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets-nodedupe", "unavailable-after-commit");
        await EnqueueAsync(f, NoDedupe, "503-after-commit");

        await RunAsync(f);
        var row = await RowAsync(f, "503-after-commit");
        Assert.Equal(DeliveryStatus.NeedsReconciliation, row.Status);
        Assert.Contains("http_503", row.LastError);
        Assert.Equal(1, Executions("503-after-commit"));

        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, (await RunAsync(f)).Sent);
        Assert.Equal(1, Executions("503-after-commit"));
    }

    [Fact]
    public async Task Adapter_exception_secrets_do_not_reach_delivery_records_or_logs()
    {
        var clock = IntegrationTestSupport.NewClock();
        var capture = new CapturingLoggerProvider();
        using var f = NewHost(clock, overrides: s =>
        {
            s.AddSingleton<IDeliveryAdapter, ThrowingDeliveryAdapter>();
            s.AddSingleton<ILoggerProvider>(capture);
        });
        await EnqueueAsync(f, ThrowingDeliveryAdapter.AdapterName, "throw-secret");

        await RunAsync(f);
        var row = await RowAsync(f, "throw-secret");
        var attempts = await AttemptsAsync(f, row.Id);
        Assert.Equal(DeliveryStatus.NeedsReconciliation, row.Status);
        Assert.DoesNotContain("sentinel-adapter-secret", JsonSerializer.Serialize(new { row, attempts }));
        Assert.DoesNotContain("sentinel-adapter-secret", capture.AllText);
    }

    [Fact]
    public async Task Accepted_is_not_success_and_waits_for_poll_or_callback_confirmation()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        mock.Script("tickets", "accept", "accept");
        mock.Script("tickets-nodedupe", "accept");
        await EnqueueAsync(f, Dedupe, "acc-poll");
        await EnqueueAsync(f, Dedupe, "acc-callback");
        await EnqueueAsync(f, NoDedupe, "acc-timeout");
        await RunAsync(f);

        var polled = await RowAsync(f, "acc-poll");
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, polled.Status);
        Assert.NotNull(polled.ConfirmDeadlineAtUtc);
        Assert.Null(polled.CompletedAtUtc);

        // 轮询:对方仍在处理 → 继续等待
        clock.Advance(TimeSpan.FromSeconds(61));
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, "acc-poll")).Status);

        // 对方完成 → 下一次轮询确认成功
        var ticketNo = polled.RemoteReference!;
        mock.State.Tickets[ticketNo] = mock.State.Tickets[ticketNo] with { Status = "done" };
        clock.Advance(TimeSpan.FromSeconds(61));
        await RunAsync(f);
        polled = await RowAsync(f, "acc-poll");
        Assert.Equal(DeliveryStatus.Succeeded, polled.Status);
        Assert.Equal(["Send:Accepted", "Query:Accepted", "Query:Succeeded"], Kinds(await AttemptsAsync(f, polled.Id)));
        Assert.Equal(1, Executions("acc-poll"));

        // 回调确认:同样按栅栏更新,重复回调幂等
        var confirmations = await InScopeAsync(f.Services, async sp =>
        {
            var service = sp.GetRequiredService<IDeliveryConfirmationService>();
            Task<DeliveryConfirmOutcome> Confirm(string key, bool succeeded, string? remoteReference = null, string? note = null) =>
                service.ConfirmAsync(new DeliveryConfirmation
                {
                    DeliveryKey = key, Succeeded = succeeded, Adapters = [Dedupe], RemoteReference = remoteReference, Note = note,
                });
            return new[]
            {
                await Confirm("acc-callback", succeeded: true, remoteReference: "CB-1", note: "对方回调"),
                await Confirm("acc-callback", succeeded: true),
                await Confirm("acc-callback", succeeded: false),
                await Confirm("no-such-key", succeeded: true),
            };
        });
        Assert.Equal([DeliveryConfirmOutcome.Applied, DeliveryConfirmOutcome.AlreadyApplied, DeliveryConfirmOutcome.Rejected, DeliveryConfirmOutcome.NotFound], confirmations);
        var callback = await RowAsync(f, "acc-callback");
        Assert.Equal(DeliveryStatus.Succeeded, callback.Status);
        Assert.Equal("CB-1", callback.RemoteReference);
        Assert.Contains(await AttemptsAsync(f, callback.Id), a => a.Kind == DeliveryAttemptKind.Confirm && a.Trigger == DeliveryAttemptTrigger.Callback);

        // 不可查询的受理:等到确认时限仍无回调 → 待核对并告警
        var timeout = await RowAsync(f, "acc-timeout");
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, timeout.Status);
        Assert.Equal(timeout.ConfirmDeadlineAtUtc, timeout.NextAttemptAtUtc);
        clock.Advance(TimeSpan.FromHours(73));
        await RunAsync(f);
        timeout = await RowAsync(f, "acc-timeout");
        Assert.Equal(DeliveryStatus.NeedsReconciliation, timeout.Status);
        Assert.Equal("confirm_timeout", timeout.LastError);
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == "acc-timeout" && a.Status == DeliveryStatus.NeedsReconciliation);
    }

    [Fact]
    public async Task A_callback_can_only_confirm_deliveries_inside_the_calling_apps_scope()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets", "accept", "accept");
        var p1 = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDemoTicketService>().CreateAndSyncAsync("P1 工单", "P1", 1m));
        var p2 = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDemoTicketService>().CreateAndSyncAsync("P2 工单", "P2", 2m));
        await RunAsync(f);   // 两条都被对方受理,等待回调确认
        var p2Key = DemoTicketService.DeliveryKeyOf(p2);
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, p2Key)).Status);

        var (_, keyA) = await IntegrationOpenApiTestKit.NewAppAsync(f.Services, "cb-a", [IntegrationOpenApiTestKit.PartnerCallback],
            [IntegrationOpenApiTestKit.PartnerScope("P1")]);
        var (_, keyB) = await IntegrationOpenApiTestKit.NewAppAsync(f.Services, "cb-b", [IntegrationOpenApiTestKit.PartnerCallback],
            [IntegrationOpenApiTestKit.PartnerScope("P2")]);
        static string OutcomeOf((int Status, JsonElement Body) response) =>
            response.Body.GetProperty("data").GetProperty("outcome").GetString()!;

        // A 拿到 P2 的投递标识冒充回调「已完成」:与标识不存在同样答复,记录不动
        var spoofed = await f.OpenClient(keyA).Send(HttpMethod.Post, "/api/open/v1/partner-callbacks/ticket-status",
            new { deliveryKey = p2Key, status = "done" });
        Assert.Equal(nameof(DeliveryConfirmOutcome.NotFound), OutcomeOf(spoofed));
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, p2Key)).Status);

        // B 回调自己范围内的受理记录:生效
        var own = await f.OpenClient(keyB).Send(HttpMethod.Post, "/api/open/v1/partner-callbacks/ticket-status",
            new { deliveryKey = p2Key, status = "done", ticketNo = "CB-9" });
        Assert.Equal(nameof(DeliveryConfirmOutcome.Applied), OutcomeOf(own));
        var confirmed = await RowAsync(f, p2Key);
        Assert.Equal(DeliveryStatus.Succeeded, confirmed.Status);
        Assert.Equal("CB-9", confirmed.RemoteReference);
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, DemoTicketService.DeliveryKeyOf(p1))).Status);
    }

    [Fact]
    public async Task Confirmation_is_limited_to_declared_adapters_business_key_and_sent_records()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets", "accept");
        await EnqueueAsync(f, Dedupe, "cb-sent");
        await RunAsync(f);                           // 已发送并被受理
        await EnqueueAsync(f, Dedupe, "cb-unsent");  // 尚未发送

        Task<DeliveryConfirmOutcome> Confirm(string key, string[] adapters, string? businessKey = null) =>
            InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryConfirmationService>().ConfirmAsync(new DeliveryConfirmation
            {
                DeliveryKey = key, Succeeded = true, Adapters = adapters, BusinessKey = businessKey,
            }));

        Assert.Equal(DeliveryConfirmOutcome.NotFound, await Confirm("cb-sent", [NoDedupe]));                  // 不是本回调负责的适配器
        Assert.Equal(DeliveryConfirmOutcome.NotFound, await Confirm("cb-sent", [Dedupe], businessKey: "999"));  // 业务键不符
        Assert.Equal(DeliveryConfirmOutcome.NotFound, await Confirm("cb-unsent", [Dedupe]));                  // 从未发送,没有对方结果可言
        Assert.Equal(DeliveryConfirmOutcome.NotFound, await Confirm("no-such-key", [Dedupe]));
        await Assert.ThrowsAsync<ArgumentException>(() => Confirm("cb-sent", []));
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, "cb-sent")).Status);
        Assert.Equal(DeliveryStatus.Pending, (await RowAsync(f, "cb-unsent")).Status);

        await RunAsync(f);   // 伪造的回调没能让未发送的记录跳过投递:照常发送
        Assert.Equal(DeliveryStatus.Succeeded, (await RowAsync(f, "cb-unsent")).Status);
        Assert.Equal(1, Executions("cb-unsent"));
        Assert.Equal(DeliveryConfirmOutcome.Applied, await Confirm("cb-sent", [Dedupe.ToUpperInvariant()]));   // 适配器名不区分大小写
    }

    [Fact]
    public async Task Attempt_budget_backoff_and_deadline_are_enforced()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        mock.Script("tickets", Enumerable.Repeat("throttle", 10).ToArray());
        await EnqueueAsync(f, Dedupe, "budget", maxAttempts: 3);

        var t0 = (await RowAsync(f, "budget")).NextAttemptAtUtc;
        await RunAsync(f);
        var first = await RowAsync(f, "budget");
        Assert.Equal(DeliveryStatus.Pending, first.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), first.NextAttemptAtUtc - t0);   // 30 秒 × 2^0(Retry-After 1 秒更短)

        await RunAsync(f);   // 未到期:不处理
        Assert.Equal(1, (await RowAsync(f, "budget")).AttemptCount);

        clock.Advance(TimeSpan.FromSeconds(31));
        await RunAsync(f);
        var second = await RowAsync(f, "budget");
        Assert.Equal(2, second.AttemptCount);
        Assert.InRange((second.NextAttemptAtUtc - first.NextAttemptAtUtc).TotalSeconds, 60, 62);   // 翻倍

        clock.Advance(TimeSpan.FromSeconds(61));
        await RunAsync(f);
        var exhausted = await RowAsync(f, "budget");
        Assert.Equal(DeliveryStatus.Exhausted, exhausted.Status);
        Assert.Equal(3, exhausted.AttemptCount);
        Assert.Equal(0, Executions("budget"));
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == "budget" && a.Status == DeliveryStatus.Exhausted && a.Reason == "max_attempts");

        // 时间上限:截止时刻已过即耗尽,不再发送
        await EnqueueAsync(f, Dedupe, "deadline", maxAge: TimeSpan.FromMinutes(1));
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.Pending, (await RowAsync(f, "deadline")).Status);
        clock.Advance(TimeSpan.FromMinutes(5));
        await RunAsync(f);
        var late = await RowAsync(f, "deadline");
        Assert.Equal(DeliveryStatus.Exhausted, late.Status);
        Assert.Equal(1, late.AttemptCount);
        Assert.Equal(["Send:NotSent", "Expired:Exhausted"], Kinds(await AttemptsAsync(f, late.Id)));
    }

    [Fact]
    public async Task A_configuration_failure_is_not_retried_automatically_but_can_be_retried_by_hand()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink, extra: s => s.Remove("TenonAdmin:Integration:Outbound:Targets:partner:Secret"));
        var id = (await EnqueueAsync(f, Dedupe, "no-secret")).Id;

        await RunAsync(f);   // 凭据缺失:重试也不会好,不耗掉整轮预算,立即交人工
        var row = await RowAsync(f, "no-secret");
        Assert.Equal(DeliveryStatus.Exhausted, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.StartsWith("credential_missing", row.LastError);
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == "no-secret" && a.Reason == "credential_missing");
        Assert.Empty(mock.State.ReceivedHeaders);

        // 什么都没发出去:人工修好配置后可直接重试
        var detail = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryAdminService>().GetAsync(id));
        Assert.Contains(DeliveryActions.Retry, detail.AllowedActions);
    }

    [Fact]
    public async Task Deterministic_rejections_fail_without_touching_the_committed_business_row()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        mock.Script("tickets", "reject");
        var ticketId = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDemoTicketService>().CreateAndSyncAsync("本地已成功", "p-9", 66m));

        await RunAsync(f);
        var row = await RowAsync(f, $"demo-ticket:{ticketId}");
        Assert.Equal(DeliveryStatus.Failed, row.Status);
        Assert.Equal(OutboundOutcome.Rejected, row.LastOutcome);
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == row.DeliveryKey && a.Status == DeliveryStatus.Failed);

        // 外部失败只影响投递记录:本地业务行原样保留
        var ticket = await InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<DemoTicket>().Where(t => t.Id == ticketId).FirstAsync());
        Assert.Equal("本地已成功", ticket.Title);
        Assert.Equal(66m, ticket.Amount);
        Assert.False(ticket.Closed);
    }

    [Fact]
    public async Task An_interrupted_dispatch_is_recovered_by_capability_and_never_blindly_resent()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        var ids = new Dictionary<string, long>();
        foreach (var (adapter, key, route) in new[] { (Dedupe, "crash-dedupe", "tickets"), (QueryOnly, "crash-query", "tickets-nodedupe"), (NoDedupe, "crash-none", "tickets-nodedupe") })
        {
            ids[key] = (await EnqueueAsync(f, adapter, key)).Id;
            // 进程在发出请求后、回写前崩溃:对方已执行,本地停在处理中且租约已过期
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MockPartnerFixture.Token);
            http.DefaultRequestHeaders.Add("Idempotency-Key", key);
            (await http.PostAsJsonAsync($"{mock.BaseUrl}/api/{route}", new { title = key, amount = 1 })).EnsureSuccessStatusCode();
        }
        await EnqueueAsync(f, NoDedupe, "still-leased");
        var now = clock.GetUtcNow().UtcDateTime;
        await InScopeAsync(f.Services, async sp =>
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var dispatching = DeliveryStatus.Dispatching;
            DateTime? expired = now.AddSeconds(-5);
            DateTime? leased = now.AddMinutes(1);
            var crashKeys = ids.Keys.ToList();
            await db.Updateable<IntegrationDelivery>()
                .SetColumns(d => new IntegrationDelivery { Status = dispatching, Fence = 1, AttemptCount = 1, LeaseUntilUtc = expired, LeaseOwner = "crashed-node#9" })
                .Where(d => crashKeys.Contains(d.DeliveryKey)).ExecuteCommandAsync();
            await db.Updateable<IntegrationDelivery>()
                .SetColumns(d => new IntegrationDelivery { Status = dispatching, Fence = 1, AttemptCount = 1, LeaseUntilUtc = leased, LeaseOwner = "busy-node#1" })
                .Where(d => d.DeliveryKey == "still-leased").ExecuteCommandAsync();
        });

        var summary = await RunAsync(f);
        Assert.Equal(3, summary.Recovered);

        var dedupe = await RowAsync(f, "crash-dedupe");
        Assert.Equal(DeliveryStatus.Succeeded, dedupe.Status);
        Assert.Equal(["Recovery:lease_expired", "Send:Succeeded"], Kinds(await AttemptsAsync(f, dedupe.Id)));
        Assert.Contains("crashed-node#9", (await AttemptsAsync(f, dedupe.Id))[0].Note);

        var queried = await RowAsync(f, "crash-query");
        Assert.Equal(DeliveryStatus.Succeeded, queried.Status);
        Assert.Equal(["Recovery:lease_expired", "Query:Succeeded"], Kinds(await AttemptsAsync(f, queried.Id)));

        var none = await RowAsync(f, "crash-none");
        Assert.Equal(DeliveryStatus.NeedsReconciliation, none.Status);
        Assert.Equal("interrupted_unknown", none.LastError);
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == "crash-none");

        // 三条业务在对方都只执行过一次;租约未过期的记录不被触碰
        Assert.All(ids.Keys, key => Assert.Equal(1, Executions(key)));
        var leasedRow = await RowAsync(f, "still-leased");
        Assert.Equal(DeliveryStatus.Dispatching, leasedRow.Status);
        Assert.Equal(1, leasedRow.Fence);
    }

    [Fact]
    public async Task An_interrupted_dispatch_that_cannot_be_resent_is_exhausted_with_the_real_reason()
    {
        var clock = IntegrationTestSupport.NewClock();
        var sink = new CapturingAlertSink();
        using var f = NewHost(clock, sink);
        // 对方去重但不能查询:中断后只能重发。截止已过(预算仍有余)与预算已尽都不再重发,但耗尽原因不同
        await EnqueueAsync(f, DedupeOnly, "crash-deadline");
        await EnqueueAsync(f, DedupeOnly, "crash-budget", maxAttempts: 1);
        var now = clock.GetUtcNow().UtcDateTime;
        await InScopeAsync(f.Services, async sp =>
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var dispatching = DeliveryStatus.Dispatching;
            DateTime? expired = now.AddSeconds(-5);
            var passed = now.AddSeconds(-1);
            await db.Updateable<IntegrationDelivery>()
                .SetColumns(d => new IntegrationDelivery { Status = dispatching, Fence = 1, AttemptCount = 1, LeaseUntilUtc = expired, LeaseOwner = "crashed-node#9", DeadlineAtUtc = passed })
                .Where(d => d.DeliveryKey == "crash-deadline").ExecuteCommandAsync();
            await db.Updateable<IntegrationDelivery>()
                .SetColumns(d => new IntegrationDelivery { Status = dispatching, Fence = 1, AttemptCount = 1, LeaseUntilUtc = expired, LeaseOwner = "crashed-node#9" })
                .Where(d => d.DeliveryKey == "crash-budget").ExecuteCommandAsync();
        });

        var summary = await RunAsync(f);
        Assert.Equal(2, summary.Recovered);
        Assert.Equal(0, summary.Sent);

        var pastDeadline = await RowAsync(f, "crash-deadline");
        Assert.Equal(DeliveryStatus.Exhausted, pastDeadline.Status);
        Assert.Equal("deadline", pastDeadline.LastError);
        var outOfBudget = await RowAsync(f, "crash-budget");
        Assert.Equal(DeliveryStatus.Exhausted, outOfBudget.Status);
        Assert.Equal("max_attempts", outOfBudget.LastError);
        Assert.Contains(sink.Alerts, a => a.DeliveryKey == "crash-deadline" && a.Reason == "deadline");
        Assert.Empty(mock.State.ReceivedHeaders);   // 两条都没有再发给对方
    }

    [Fact]
    public async Task A_late_result_never_overwrites_a_newer_claim()
    {
        var clock = IntegrationTestSupport.NewClock();
        // A 的调用何时结束由测试放行决定(超时放宽到 30 秒),不靠 3 秒墙钟与 B 的领取赛跑
        using var f = NewHost(clock, extra: s => s["TenonAdmin:Integration:Outbound:Targets:partner:TimeoutSeconds"] = "30");
        mock.Script("tickets", "hold", "ok");
        var id = (await EnqueueAsync(f, Dedupe, "fenced")).Id;

        var workerA = RunAsync(f);   // 领取后对方挂起不回
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Requests("tickets") < 1 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(1, Requests("tickets"));

        clock.Advance(TimeSpan.FromMinutes(3));   // A 的租约在本端看来已过期
        var workerB = await RunAsync(f);
        Assert.Equal(1, workerB.Recovered);
        Assert.Equal(DeliveryStatus.Succeeded, (await RowAsync(f, "fenced")).Status);

        mock.State.ReleaseHeld();   // 对方这才断开 A 的连接:A 拿到「结果未知」时,B 早已夺回并成功
        var a = await workerA;
        Assert.Equal(1, a.Late);
        var row = await RowAsync(f, "fenced");
        Assert.Equal(DeliveryStatus.Succeeded, row.Status);   // 迟到的「结果未知」没有覆盖新领取的成功
        var attempts = await AttemptsAsync(f, id);
        Assert.Equal(["Recovery:lease_expired", "Send:Succeeded", "Send:Unknown"], Kinds(attempts));
        Assert.StartsWith("迟到的结果", attempts[2].Note);
        Assert.Equal(1, Executions("fenced"));
    }

    [Fact]
    public async Task Two_hosts_competing_for_the_same_records_deliver_each_exactly_once()
    {
        var clockA = IntegrationTestSupport.NewClock();
        var clockB = IntegrationTestSupport.NewClock();
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-itg-dlv-race-{Guid.NewGuid():N}.db");
        try
        {
            using var hostA = NewHost(clockA, dbPath: dbPath, workerId: 41);
            _ = hostA.CreateClient();
            using var hostB = NewHost(clockB, dbPath: dbPath, reset: false, workerId: 42, resetMock: false);
            _ = hostB.CreateClient();

            var keys = Enumerable.Range(1, 12).Select(i => $"race-{i}").ToList();
            foreach (var key in keys) await EnqueueAsync(hostA, NoDedupe, key);   // 对方不去重:任何重复发送都会在对方计数里现形

            await Task.WhenAll(RunAsync(hostA), RunAsync(hostB), RunAsync(hostA), RunAsync(hostB));

            foreach (var key in keys)
            {
                Assert.Equal(1, Executions(key));
                var row = await RowAsync(hostB, key);
                Assert.Equal(DeliveryStatus.Succeeded, row.Status);
                Assert.Equal(["Send:Succeeded"], Kinds(await AttemptsAsync(hostA, row.Id)));
            }
            Assert.Equal(keys.Count, Requests("tickets-nodedupe"));
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    [Fact]
    public async Task Manual_actions_are_rechecked_on_the_server_and_keep_the_idempotency_key()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets", "reject");
        mock.Script("tickets-nodedupe", "drop");
        var failed = (await EnqueueAsync(f, Dedupe, "man-failed")).Id;
        var reconcile = (await EnqueueAsync(f, NoDedupe, "man-reconcile")).Id;
        await RunAsync(f);

        var admin = await AuthProbe.SuperAdmin(f);
        async Task<JsonElement> Detail(long id) => (await admin.Send(HttpMethod.Get, $"/api/v1/integration/delivery/{id}")).Body.GetProperty("data");
        static string[] Actions(JsonElement detail) => detail.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!).ToArray();
        Task<(int Status, JsonElement Body)> Act(long id, string action, object body) => admin.Send(HttpMethod.Post, $"/api/v1/integration/delivery/{id}/{action}", body);

        // 失败:可重试、可取消;不可确认
        var failedDetail = await Detail(failed);
        Assert.Equal((int)DeliveryStatus.Failed, failedDetail.GetProperty("status").GetInt32());
        Assert.Equal([DeliveryActions.Retry, DeliveryActions.Query, DeliveryActions.Cancel], Actions(failedDetail));
        Assert.Equal(IntegrationErrorCode.DeliveryActionNotAllowed, (await Act(failed, DeliveryActions.ConfirmSucceeded, new { note = "x" })).Body.Code());
        Assert.Equal(IntegrationErrorCode.DeliveryConcurrentlyModified, (await Act(failed, DeliveryActions.Retry, new { fence = 999 })).Body.Code());

        var retried = (await Act(failed, DeliveryActions.Retry, new { note = "对方已放开额度", fence = failedDetail.GetProperty("fence").GetInt64() })).Body;
        Assert.Equal(0, retried.Code());
        Assert.Equal((int)DeliveryStatus.Pending, retried.GetProperty("data").GetProperty("status").GetInt32());
        Assert.Equal(0, retried.GetProperty("data").GetProperty("attemptsInBudget").GetInt32());   // 预算已重置
        await RunAsync(f);
        var afterRetry = await Detail(failed);
        Assert.Equal((int)DeliveryStatus.Succeeded, afterRetry.GetProperty("status").GetInt32());
        Assert.Equal(["man-failed", "man-failed"], SentKeys().Where(k => k == "man-failed").ToArray());   // 两次发送同一幂等标识
        var timeline = afterRetry.GetProperty("attempts").EnumerateArray().Reverse()
            .Select(a => $"{(DeliveryAttemptKind)a.GetProperty("kind").GetInt32()}:{a.GetProperty("outcome").GetString()}").ToArray();
        Assert.Equal(["Send:Rejected", "Manual:retry", "Send:Succeeded"], timeline);
        var manual = afterRetry.GetProperty("attempts").EnumerateArray().Single(a => a.GetProperty("outcome").GetString() == "retry");
        Assert.Equal("对方已放开额度", manual.GetProperty("note").GetString());
        Assert.False(string.IsNullOrEmpty(manual.GetProperty("operatorName").GetString()));

        // 待核对(对方不去重、结果未知):不能直接重试;确认须写说明
        var reconcileDetail = await Detail(reconcile);
        Assert.Equal([DeliveryActions.ConfirmSucceeded, DeliveryActions.ConfirmNotExecuted, DeliveryActions.Cancel], Actions(reconcileDetail));
        Assert.Equal(IntegrationErrorCode.DeliveryActionNotAllowed, (await Act(reconcile, DeliveryActions.Retry, new { })).Body.Code());
        Assert.Equal(IntegrationErrorCode.DeliveryNoteRequired, (await Act(reconcile, DeliveryActions.ConfirmSucceeded, new { })).Body.Code());
        var confirmed = (await Act(reconcile, DeliveryActions.ConfirmSucceeded, new { note = "已在对方后台核实单号" })).Body;
        Assert.Equal((int)DeliveryStatus.Succeeded, confirmed.GetProperty("data").GetProperty("status").GetInt32());
        Assert.Empty(Actions(confirmed.GetProperty("data")));
        Assert.Equal(1, Executions("man-reconcile"));

        // 汇总与审计
        var summary = (await admin.Send(HttpMethod.Get, "/api/v1/integration/delivery/summary")).Body.GetProperty("data").EnumerateArray()
            .ToDictionary(s => (DeliveryStatus)s.GetProperty("status").GetInt32(), s => s.GetProperty("count").GetInt32());
        Assert.Equal(2, summary[DeliveryStatus.Succeeded]);
        var titles = await InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<SysOpLog>().Select(l => l.Title).ToListAsync());
        Assert.Contains("重试可靠投递", titles);
        Assert.Contains("确认可靠投递已成功", titles);

        // 权限
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/integration/delivery/page"));
        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/integration/delivery/page"));
        await AuthProbe.AssertForbidden(await limited.GetAsync($"/api/v1/integration/delivery/{failed}"));
        await AuthProbe.AssertForbidden(await limited.PostAsJsonAsync($"/api/v1/integration/delivery/{failed}/cancel", new { }));
    }

    [Fact]
    public async Task Manual_query_and_confirm_not_executed_follow_the_remote_truth()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets-nodedupe", "throttle", "drop");
        var exhausted = (await EnqueueAsync(f, QueryOnly, "q-exhausted", maxAttempts: 1)).Id;
        await RunAsync(f);   // 逐条处理,保证脚本按顺序被消费
        var reconcile = (await EnqueueAsync(f, NoDedupe, "q-reconcile")).Id;
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.Exhausted, (await RowAsync(f, "q-exhausted")).Status);
        Assert.Equal(DeliveryStatus.NeedsReconciliation, (await RowAsync(f, "q-reconcile")).Status);

        var admin = await AuthProbe.SuperAdmin(f);
        Task<(int Status, JsonElement Body)> Act(long id, string action, object body) => admin.Send(HttpMethod.Post, $"/api/v1/integration/delivery/{id}/{action}", body);

        // 耗尽(只有「未发出」)+ 对方可查询:查询得知对方无记录 → 可安全发送,重置预算后由投递器以同一标识发送
        var queried = (await Act(exhausted, DeliveryActions.Query, new { })).Body.GetProperty("data");
        Assert.Equal((int)DeliveryStatus.Pending, queried.GetProperty("status").GetInt32());
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.Succeeded, (await RowAsync(f, "q-exhausted")).Status);
        Assert.Equal(1, Executions("q-exhausted"));

        // 待核对 → 人工确认未执行 → 重新发送(人工对事实负责;发送仍用原标识、仍走出站安全检查)
        var confirmed = (await Act(reconcile, DeliveryActions.ConfirmNotExecuted, new { note = "对方确认未收到" })).Body.GetProperty("data");
        Assert.Equal((int)DeliveryStatus.Pending, confirmed.GetProperty("status").GetInt32());
        await RunAsync(f);
        var row = await RowAsync(f, "q-reconcile");
        Assert.Equal(DeliveryStatus.Succeeded, row.Status);
        Assert.Equal(2, SentKeys().Count(k => k == "q-reconcile"));
    }

    [Fact]
    public async Task A_resent_delivery_accepted_again_gets_a_fresh_confirmation_window()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets-nodedupe", "accept", "accept");
        var id = (await EnqueueAsync(f, NoDedupe, "reaccept")).Id;
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, "reaccept")).Status);

        clock.Advance(TimeSpan.FromHours(73));   // 一直没等到回调:确认超时,交人工
        await RunAsync(f);
        Assert.Equal("confirm_timeout", (await RowAsync(f, "reaccept")).LastError);

        // 人工确认对方未执行 → 新一轮发送,又被受理:确认窗口从这次受理重新计时
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryAdminService>()
            .ExecuteAsync(id, DeliveryActions.ConfirmNotExecuted, new DeliveryActionInput { Note = "对方确认未收到" }));
        await RunAsync(f);
        var reaccepted = await RowAsync(f, "reaccept");
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, reaccepted.Status);
        Assert.True(reaccepted.ConfirmDeadlineAtUtc > clock.GetUtcNow().UtcDateTime);

        await RunAsync(f);   // 下一轮扫描不会拿上一轮早已过期的时限判超时
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, "reaccept")).Status);
    }

    [Fact]
    public async Task A_manual_query_that_finds_the_delivery_accepted_opens_a_fresh_window()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets", "accept");
        var id = (await EnqueueAsync(f, Dedupe, "requery")).Id;
        await RunAsync(f);   // 受理,开始轮询
        clock.Advance(TimeSpan.FromHours(73));   // 对方一直处理中,确认时限已过
        await RunAsync(f);
        var timedOut = await RowAsync(f, "requery");
        Assert.Equal(DeliveryStatus.NeedsReconciliation, timedOut.Status);
        Assert.Equal("confirm_timeout", timedOut.LastError);

        // 管理员手动查询:对方仍在处理 → 回到待确认,拿到新的确认窗口(不是早已过期的旧时限)
        var detail = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryAdminService>()
            .ExecuteAsync(id, DeliveryActions.Query, new DeliveryActionInput()));
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, detail.Status);
        Assert.True(detail.ConfirmDeadlineAt > clock.GetUtcNow());

        clock.Advance(TimeSpan.FromSeconds(61));
        await RunAsync(f);   // 下一次轮询仍在窗口内
        Assert.Equal(DeliveryStatus.AwaitingConfirmation, (await RowAsync(f, "requery")).Status);
    }

    [Fact]
    public async Task Only_unknown_sends_of_the_current_budget_block_a_manual_retry()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        mock.Script("tickets-nodedupe", "drop", "throttle");
        var id = (await EnqueueAsync(f, NoDedupe, "legacy-retry", maxAttempts: 1)).Id;
        await RunAsync(f);   // 响应丢失,对方不去重也不能查询 → 待核对
        Assert.Equal(DeliveryStatus.NeedsReconciliation, (await RowAsync(f, "legacy-retry")).Status);

        // 核实后确认未执行 → 新一轮只有一次「未发出」,预算用尽
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryAdminService>()
            .ExecuteAsync(id, DeliveryActions.ConfirmNotExecuted, new DeliveryActionInput { Note = "已在对方后台核实未执行" }));
        await RunAsync(f);
        Assert.Equal(DeliveryStatus.Exhausted, (await RowAsync(f, "legacy-retry")).Status);

        // 上一轮已核对过的「结果未知」不再挡住重试
        var detail = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryAdminService>().GetAsync(id));
        Assert.Contains(DeliveryActions.Retry, detail.AllowedActions);
    }

    [Fact]
    public async Task Manual_retry_cannot_bypass_the_outbound_address_policy()
    {
        var clock = IntegrationTestSupport.NewClock();
        // 同一对方改用域名且不再信任回环:域名解析到回环,每次调用都在建连前被拒(字面回环地址启动即会被拒)
        using var f = NewHost(clock, extra: s =>
        {
            s.Remove("TenonAdmin:Integration:Outbound:Targets:partner:TrustedCidrs:0");
            s["TenonAdmin:Integration:Outbound:Targets:partner:BaseUrl"] = $"http://localhost:{mock.Port}/api/";
        });
        var id = (await EnqueueAsync(f, Dedupe, "blocked", maxAttempts: 1)).Id;
        await RunAsync(f);
        var exhausted = await RowAsync(f, "blocked");
        Assert.Equal(DeliveryStatus.Exhausted, exhausted.Status);
        Assert.Contains("target_blocked", exhausted.LastError);

        var admin = await AuthProbe.SuperAdmin(f);
        Assert.Equal(0, (await admin.Send(HttpMethod.Post, $"/api/v1/integration/delivery/{id}/retry", new { note = "再试" })).Body.Code());
        await RunAsync(f);
        var again = await RowAsync(f, "blocked");
        Assert.Equal(DeliveryStatus.Exhausted, again.Status);
        Assert.Contains("target_blocked", again.LastError);
        Assert.Empty(mock.State.ReceivedHeaders);   // 对方从未收到任何请求
    }

    [Fact]
    public async Task Alerts_reach_every_sink_and_the_default_warning_log_even_if_one_sink_fails()
    {
        var clock = IntegrationTestSupport.NewClock();
        var capture = new CapturingLoggerProvider();
        var good = new CapturingAlertSink();
        var broken = new CapturingAlertSink(fail: true);
        using var f = NewHost(clock, good, overrides: s =>
        {
            s.AddSingleton<IDeliveryAlertSink>(broken);
            s.AddSingleton<ILoggerProvider>(capture);
        });
        mock.Script("tickets", "reject");
        await EnqueueAsync(f, Dedupe, "alerting");
        await RunAsync(f);

        Assert.Equal(DeliveryStatus.Failed, (await RowAsync(f, "alerting")).Status);
        Assert.Single(good.Alerts);
        Assert.Single(broken.Alerts);
        Assert.Contains(capture.Lines, l => l.StartsWith("Warning") && l.Contains("可靠投递需要人工处理") && l.Contains("alerting"));
        Assert.Contains(capture.Lines, l => l.StartsWith("Error") && l.Contains("告警出口"));
        Assert.DoesNotContain("sentinel-alert-secret", capture.AllText);
    }

    [Fact]
    public async Task Retention_removes_only_old_completed_deliveries_and_their_attempts()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock, extra: s => s["TenonAdmin:Integration:Retention:BatchSize"] = "2");
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = new (string Key, DeliveryStatus Status, int? CompletedDaysAgo)[]
        {
            ("old-ok", DeliveryStatus.Succeeded, 100), ("old-cancel", DeliveryStatus.Cancelled, 91), ("old-ok-2", DeliveryStatus.Succeeded, 95),
            ("recent-ok", DeliveryStatus.Succeeded, 10), ("stuck", DeliveryStatus.NeedsReconciliation, null), ("dead", DeliveryStatus.Exhausted, null),
            ("broken", DeliveryStatus.Failed, null), ("waiting", DeliveryStatus.AwaitingConfirmation, null),
        };
        await InScopeAsync(f.Services, async sp =>
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            foreach (var (key, status, days) in rows)
            {
                var delivery = new IntegrationDelivery
                {
                    DeliveryKey = key, Adapter = Dedupe, Operation = "op", PayloadJson = "{}", PayloadHash = new string('0', 64), Status = status,
                    MaxAttempts = 8, NextAttemptAtUtc = now.AddDays(-200), DeadlineAtUtc = now.AddDays(-199),
                    CompletedAtUtc = days is { } d ? now.AddDays(-d) : null,
                };
                await db.Insertable(delivery).ExecuteCommandAsync();
                await db.Insertable(new IntegrationDeliveryAttempt { DeliveryId = delivery.Id, Kind = DeliveryAttemptKind.Send, StartedAtUtc = now, Outcome = "x" }).ExecuteCommandAsync();
            }
        });

        var result = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IIntegrationRetentionService>().CleanupAsync());
        Assert.Equal(3, result.Deliveries);
        var remaining = await InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationDelivery>().Select(d => d.DeliveryKey).ToListAsync());
        Assert.Equal(new HashSet<string> { "recent-ok", "stuck", "dead", "broken", "waiting" }, remaining.ToHashSet());
        var attempts = await InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationDeliveryAttempt>().CountAsync());
        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task The_seeded_job_drives_the_dispatcher()
    {
        var clock = IntegrationTestSupport.NewClock();
        using var f = NewHost(clock);
        await EnqueueAsync(f, Dedupe, "by-job");
        await InScopeAsync(f.Services, async sp =>
        {
            var job = await sp.GetRequiredService<ISqlSugarClient>().Queryable<SysJob>().Where(j => j.Code == "itg-delivery").FirstAsync();
            Assert.Equal(typeof(IntegrationDeliveryJob).FullName, job.HandlerName);
            Assert.Equal(JobTriggerKind.Interval, job.TriggerKind);
            Assert.Equal(5, job.IntervalSeconds);
            Assert.Equal(JobConcurrencyMode.SerialSkip, job.ConcurrencyMode);

            var messages = new List<string>();
            var handler = sp.GetServices<IAdminJob>().OfType<IntegrationDeliveryJob>().Single();
            var now = clock.GetUtcNow().DateTime;
            await handler.ExecuteAsync(new JobExecutionContext
            {
                JobId = job.Id, JobCode = job.Code, JobName = job.Name, FireInstanceId = 1, ScheduledTime = now, FireTime = now, Log = messages.Add,
            }, CancellationToken.None);
            Assert.Contains("成功 1", Assert.Single(messages));
        });
        Assert.Equal(DeliveryStatus.Succeeded, (await RowAsync(f, "by-job")).Status);
    }
}
