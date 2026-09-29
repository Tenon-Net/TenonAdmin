extern alias integrationhost;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Integration;
using TenonAdmin.Samples.MockPartner;
using TenonAdmin.Services;
using DemoPartnerClient = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerClient;
using PartnerTicketResult = integrationhost::TenonAdmin.IntegrationTestHost.PartnerTicketResult;

namespace TenonAdmin.Tests;

/// <summary>进程内启动的模拟第三方(Kestrel 随机端口,真实网络栈)。测试直接读它的内存状态,核对对方真正执行了几次。</summary>
public sealed class MockPartnerFixture : IAsyncLifetime
{
    public const string Token = "partner-secret";

    private WebApplication? _app;

    public string BaseUrl { get; private set; } = "";

    public int Port { get; private set; }

    public MockPartnerState State => _app!.Services.GetRequiredService<MockPartnerState>();

    public async Task InitializeAsync()
    {
        _app = MockPartnerServer.Build(options: new MockPartnerOptions { Token = Token }, url: "http://127.0.0.1:0");
        await _app.StartAsync();
        BaseUrl = MockPartnerServer.Address(_app).TrimEnd('/');
        Port = new Uri(BaseUrl).Port;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>编排某个写接口接下来几次请求的表现。</summary>
    public void Script(string route, params string[] behaviors) => State.Script(route, behaviors);

    public int Executions(string key) => State.Executions.TryGetValue(key, out var n) ? n : 0;

    /// <summary>在宿主配置里声明一个出站目标(默认信任回环;认证方式、秘密等为空即不写)。</summary>
    public static void AddTarget(IDictionary<string, string?> settings, string name, string baseUrl, string? auth, string? secret,
        string? trusted = "127.0.0.1/32", string? header = null, string? username = null, int timeoutSeconds = 5)
    {
        var prefix = $"TenonAdmin:Integration:Outbound:Targets:{name}:";
        settings[prefix + "BaseUrl"] = baseUrl;
        settings[prefix + "TimeoutSeconds"] = timeoutSeconds.ToString();
        if (trusted is not null) settings[prefix + "TrustedCidrs:0"] = trusted;
        if (auth is not null) settings[prefix + "Auth:Type"] = auth;
        if (header is not null) settings[prefix + "Auth:HeaderName"] = header;
        if (username is not null) settings[prefix + "Auth:Username"] = username;
        if (secret is not null) settings[prefix + "Secret"] = secret;
    }
}

/// <summary>
/// G07 普通出站调用:用本地可控第三方验证凭据注入、超时与取消、错误映射、不安全目标、写操作不被底层重发、
/// 响应上限、秘密不外泄、自定义适配器复用基础设施,以及出站记录的后台查询权限。
/// </summary>
public class IntegrationOutboundTests(MockPartnerFixture mock) : IClassFixture<MockPartnerFixture>
{
    private static readonly JsonSerializerOptions RawJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private IntegrationAppFactory NewHost(Action<Dictionary<string, string?>>? extra = null, Action<IServiceCollection>? overrides = null)
    {
        mock.State.Reset();
        var settings = new Dictionary<string, string?>();
        var api = $"{mock.BaseUrl}/api/";
        MockPartnerFixture.AddTarget(settings, "partner", api, "Bearer", MockPartnerFixture.Token);
        MockPartnerFixture.AddTarget(settings, "partner-header", api, "Header", MockPartnerFixture.Token, header: "X-Partner-Key");
        MockPartnerFixture.AddTarget(settings, "partner-basic", api, "Basic", MockPartnerFixture.Token, username: "tenon");
        MockPartnerFixture.AddTarget(settings, DemoPartnerClient.Target, api, null, MockPartnerFixture.Token);   // 适配器自己签名
        MockPartnerFixture.AddTarget(settings, "partner-nosecret", api, "Bearer", null);
        MockPartnerFixture.AddTarget(settings, "partner-wrong", api, "Bearer", "wrong-secret");
        MockPartnerFixture.AddTarget(settings, "partner-localhost", $"http://localhost:{mock.Port}/api/", "Bearer", MockPartnerFixture.Token, trusted: null);
        MockPartnerFixture.AddTarget(settings, "partner-closed", $"http://127.0.0.1:{FreePort()}/api/", "Bearer", MockPartnerFixture.Token);
        MockPartnerFixture.AddTarget(settings, "partner-dns", $"http://partner.test:{mock.Port}/api/", "Bearer", MockPartnerFixture.Token);
        extra?.Invoke(settings);
        return new IntegrationAppFactory { Settings = settings, Overrides = overrides, SchemaNeutral = true };
    }

    private static async Task<OutboundResponse> CallAsync(IntegrationAppFactory f, OutboundRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboundHttpInvoker>().SendAsync(request, cancellationToken);
    }

    private static OutboundRequest Get(string target, string path, TimeSpan? timeout = null) =>
        new() { Target = target, Method = HttpMethod.Get, Path = path, Timeout = timeout, Operation = "test.get" };

    private static OutboundRequest Post(string target, string path, object? body, string? key = null) =>
        new() { Target = target, Method = HttpMethod.Post, Path = path, Content = body is null ? null : JsonContent.Create(body), IdempotencyKey = key, Operation = "ticket.create" };

    private static async Task<List<IntegrationOutboundLog>> LogsAsync(IntegrationAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationOutboundLog>().OrderBy(l => l.Id).ToListAsync();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private Dictionary<string, string> LastHeaders() => mock.State.ReceivedHeaders.Last();

    [Fact]
    public async Task Credentials_are_injected_per_target_and_every_call_carries_its_call_id()
    {
        using var f = NewHost();

        var bearer = await CallAsync(f, Get("partner", "partners/acme"));
        Assert.Equal(OutboundOutcome.Succeeded, bearer.Outcome);
        Assert.Equal(200, bearer.StatusCode);
        Assert.Equal("工单对接方 acme", bearer.ReadJson<JsonElement>().GetProperty("name").GetString());
        Assert.Equal("Bearer " + MockPartnerFixture.Token, LastHeaders()["Authorization"]);
        Assert.Equal(bearer.CallId, LastHeaders()["X-Request-Id"]);

        var header = await CallAsync(f, Get("partner-header", "partners/acme"));
        Assert.Equal(OutboundOutcome.Succeeded, header.Outcome);
        Assert.Equal(MockPartnerFixture.Token, LastHeaders()["X-Partner-Key"]);
        Assert.False(LastHeaders().ContainsKey("Authorization"));

        var basic = await CallAsync(f, Get("partner-basic", "partners/acme"));
        Assert.Equal(OutboundOutcome.Succeeded, basic.Outcome);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("tenon:" + MockPartnerFixture.Token)), LastHeaders()["Authorization"]);

        var received = mock.State.ReceivedHeaders.Count;
        var missing = await CallAsync(f, Get("partner-nosecret", "partners/acme"));
        Assert.Equal(OutboundOutcome.NotSent, missing.Outcome);
        Assert.Equal("credential_missing", missing.Reason);
        Assert.False(missing.Transient);
        Assert.Equal(received, mock.State.ReceivedHeaders.Count);   // 缺凭据根本不发出

        var wrong = await CallAsync(f, Get("partner-wrong", "partners/acme"));
        Assert.Equal(OutboundOutcome.AuthenticationFailed, wrong.Outcome);
        Assert.Equal("http_401", wrong.Reason);

        var unknownTarget = await CallAsync(f, Get("nobody", "x"));
        Assert.Equal(OutboundOutcome.NotSent, unknownTarget.Outcome);
        Assert.Equal("target_unknown", unknownTarget.Reason);
        Assert.Equal(IntegrationErrorCode.OutboundTargetNotConfigured, (int)Assert.Throws<TenonAdmin.Core.AdminException>(() => unknownTarget.EnsureSucceeded()).Code);

        // 每次调用一条记录,调用 Id 与发给对方的 X-Request-Id 一致
        var logs = await LogsAsync(f);
        Assert.Equal(6, logs.Count);
        Assert.Equal(bearer.CallId, logs[0].CallId);
        Assert.Equal($"{mock.BaseUrl}/api/partners/acme", logs[0].Url);
        Assert.Equal("test.get", logs[0].Operation);
        Assert.Equal(["credential_missing", "http_401", "target_unknown"], logs.Skip(3).Select(l => l.Reason!).ToArray());
    }

    [Fact]
    public async Task Secret_changes_in_configuration_apply_to_the_next_call_without_restart()
    {
        using var f = NewHost();
        Assert.Equal(OutboundOutcome.Succeeded, (await CallAsync(f, Get("partner", "partners/acme"))).Outcome);

        var configuration = f.Services.GetRequiredService<IConfiguration>();
        configuration["TenonAdmin:Integration:Outbound:Targets:partner:Secret"] = "rotated-elsewhere";
        Assert.Equal(OutboundOutcome.AuthenticationFailed, (await CallAsync(f, Get("partner", "partners/acme"))).Outcome);
        Assert.Equal("Bearer rotated-elsewhere", LastHeaders()["Authorization"]);

        configuration["TenonAdmin:Integration:Outbound:Targets:partner:Secret"] = MockPartnerFixture.Token;
        Assert.Equal(OutboundOutcome.Succeeded, (await CallAsync(f, Get("partner", "partners/acme"))).Outcome);
    }

    [Fact]
    public async Task Remote_statuses_map_to_the_contract_outcomes()
    {
        (int Status, OutboundOutcome Expected)[] table =
        [
            (200, OutboundOutcome.Succeeded), (201, OutboundOutcome.Succeeded), (204, OutboundOutcome.Succeeded),
            (202, OutboundOutcome.Accepted),
            (301, OutboundOutcome.Rejected), (400, OutboundOutcome.Rejected), (404, OutboundOutcome.Rejected),
            (409, OutboundOutcome.Rejected), (422, OutboundOutcome.Rejected),
            (401, OutboundOutcome.AuthenticationFailed), (403, OutboundOutcome.AuthenticationFailed),
            (408, OutboundOutcome.Unknown), (500, OutboundOutcome.Unknown), (502, OutboundOutcome.Unknown), (503, OutboundOutcome.Unknown), (504, OutboundOutcome.Unknown),
            (429, OutboundOutcome.NotSent),
        ];
        using var f = NewHost();
        foreach (var (status, expected) in table)
        {
            var response = await CallAsync(f, Get("partner", $"status/{status}"));
            Assert.True(expected == response.Outcome, $"{status} → {response.Outcome},期望 {expected}");
            Assert.Equal(status, response.StatusCode);
            Assert.Equal($"http_{status}", response.Reason);
            Assert.Equal(expected == OutboundOutcome.NotSent, response.Transient);
            Assert.Equal(status is 429 or 503 ? TimeSpan.FromSeconds(2) : null, response.RetryAfter);
            Assert.Equal(expected is OutboundOutcome.Succeeded or OutboundOutcome.Accepted, response.IsSuccess);
        }
        Assert.Equal(table.Length, (await LogsAsync(f)).Count);
    }

    [Fact]
    public async Task Timeouts_and_caller_cancellation_are_reported_and_recorded_separately()
    {
        using var f = NewHost();
        var timedOut = await CallAsync(f, Get("partner", "slow?ms=5000", TimeSpan.FromMilliseconds(400)));
        Assert.Equal(OutboundOutcome.Unknown, timedOut.Outcome);
        Assert.Equal("timeout", timedOut.Reason);
        Assert.InRange(timedOut.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(3));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var cancelled = await CallAsync(f, Get("partner", "slow?ms=5000"), cts.Token);
        Assert.Equal(OutboundOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal("cancelled", cancelled.Reason);
        Assert.InRange(cancelled.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));

        var logs = await LogsAsync(f);
        Assert.Equal([OutboundOutcome.Unknown, OutboundOutcome.Cancelled], logs.Select(l => l.Outcome).ToArray());
        Assert.Equal(["timeout", "cancelled"], logs.Select(l => l.Reason!).ToArray());
        Assert.All(logs, l => Assert.Equal($"{mock.BaseUrl}/api/slow", l.Url));   // 查询串不入记录
        Assert.Equal(IntegrationErrorCode.OutboundResultUnknown, (int)Assert.Throws<TenonAdmin.Core.AdminException>(() => timedOut.EnsureSucceeded()).Code);
    }

    [Fact]
    public async Task Outbound_calls_made_inside_an_open_request_share_its_trace_id()
    {
        using var f = NewHost();
        var (_, key) = await IntegrationOpenApiTestKit.NewAppAsync(f.Services, "lookup", [IntegrationOpenApiTestKit.PartnerLookup], []);
        using var response = await f.OpenClient(key).GetAsync("/api/open/v1/partner-lookups/any-key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trace = response.Headers.GetValues(OpenApiHeaders.TraceId).Single();

        // 响应头、开放调用记录与它触发的出站调用记录是同一个标识:按它能把一次开放请求和它引起的外呼串起来
        Assert.Equal(trace, Assert.Single(await LogsAsync(f)).TraceId);
        using (var scope = f.Services.CreateScope())
            Assert.Equal(trace, (await scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationInboundLog>().FirstAsync()).TraceId);

        // 请求之外(如后台任务)发起的调用没有请求标识,退回 W3C Activity 的 TraceId
        using var background = new Activity("background-job").Start();
        await CallAsync(f, Get("partner", "partners/acme"));
        Assert.Equal(background.TraceId.ToHexString(), (await LogsAsync(f))[1].TraceId);
    }

    [Fact]
    public async Task A_delivery_call_cannot_outlast_its_lease()
    {
        var capture = new CapturingLoggerProvider();
        using var f = NewHost(
            extra: s =>
            {
                foreach (var key in s.Keys.Where(k => k.EndsWith(":TimeoutSeconds", StringComparison.Ordinal)).ToList()) s[key] = "1";
                s["TenonAdmin:Integration:Outbound:DefaultTimeoutSeconds"] = "1";
                s["TenonAdmin:Integration:Delivery:LeaseSeconds"] = "32";   // 租约 32 秒 → 为投递发起的调用最多 2 秒
            },
            overrides: s => s.AddSingleton<ILoggerProvider>(capture));

        // 适配器按调用把超时放宽到 10 秒:仍被截断到租约减安全余量,否则租约先到期,恢复会与仍在进行的调用重叠
        var capped = await CallAsync(f, new OutboundRequest
        {
            Target = "partner", Method = HttpMethod.Get, Path = "slow?ms=4000", Timeout = TimeSpan.FromSeconds(10),
            DeliveryId = 1, AttemptNo = 1, Operation = "ticket.query",
        });
        Assert.Equal((OutboundOutcome.Unknown, "timeout"), (capped.Outcome, capped.Reason));
        Assert.Contains("2 秒", capped.ErrorSummary);
        Assert.Contains(capture.Lines, l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains("租约"));
    }

    [Fact]
    public async Task Failures_before_the_request_reaches_the_partner_are_not_sent_and_retryable()
    {
        var dns = new ScriptedDnsClientFactory();
        dns.Answer("partner.test", null);   // 解析失败
        using var f = NewHost(overrides: s => s.AddSingleton<OutboundHttpClientFactory>(dns));

        var refused = await CallAsync(f, Get("partner-closed", "partners/acme"));
        Assert.Equal(OutboundOutcome.NotSent, refused.Outcome);
        Assert.Equal("connection_failed", refused.Reason);
        Assert.True(refused.Transient);

        var unresolved = await CallAsync(f, Get("partner-dns", "partners/acme"));
        Assert.Equal(OutboundOutcome.NotSent, unresolved.Outcome);
        Assert.Equal("dns_failed", unresolved.Reason);
        Assert.True(unresolved.Transient);
        Assert.Equal(IntegrationErrorCode.OutboundNotSent, (int)Assert.Throws<TenonAdmin.Core.AdminException>(() => unresolved.EnsureSucceeded()).Code);
        Assert.Empty(mock.State.ReceivedHeaders);
    }

    [Fact]
    public async Task Connect_timeouts_are_not_sent_rather_than_unknown()
    {
        // 只监听、从不 accept 且积压队列已满的端口:内核丢弃新 SYN,建连一直挂起直到建连超时
        using var blackhole = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        blackhole.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        blackhole.Listen(1);
        var port = ((IPEndPoint)blackhole.LocalEndPoint!).Port;
        var fillers = Enumerable.Range(0, 4).Select(_ => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)).ToList();
        try
        {
            foreach (var filler in fillers) _ = filler.ConnectAsync(IPAddress.Loopback, port);
            await Task.Delay(300);

            using var f = NewHost(s => s[$"TenonAdmin:Integration:Outbound:Targets:partner-closed:BaseUrl"] = $"http://127.0.0.1:{port}/api/");
            var response = await CallAsync(f, Get("partner-closed", "partners/acme", TimeSpan.FromSeconds(5)));
            Assert.Equal(OutboundOutcome.NotSent, response.Outcome);
            Assert.Equal("connect_timeout", response.Reason);
            Assert.True(response.Transient);
            Assert.InRange(response.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));   // 建连超时 = 调用超时的一半
        }
        finally
        {
            foreach (var filler in fillers) filler.Dispose();
        }
    }

    [Fact]
    public async Task Unsafe_targets_are_refused_without_contacting_them()
    {
        var dns = new ScriptedDnsClientFactory();
        dns.Answer("partner.test", IPAddress.Loopback);                    // 第一次:受信回环
        dns.Answer("partner.test", IPAddress.Parse("169.254.169.254"));    // DNS 变化:云元数据
        dns.Answer("partner.test", IPAddress.Parse("10.0.0.8"));           // DNS 变化:未受信内网
        using var f = NewHost(overrides: s => s.AddSingleton<OutboundHttpClientFactory>(dns));

        // 域名解析到未受信的回环地址:建连前拒绝
        var local = await CallAsync(f, Get("partner-localhost", "partners/acme"));
        Assert.Equal(OutboundOutcome.NotSent, local.Outcome);
        Assert.Equal("target_blocked", local.Reason);
        Assert.False(local.Transient);
        Assert.Empty(mock.State.ReceivedHeaders);

        // 同一域名的解析结果变化:写请求每次新建连接,每次都对新地址复检
        var first = await CallAsync(f, Post("partner-dns", "tickets-nodedupe", new { title = "a" }));
        Assert.Equal(OutboundOutcome.Succeeded, first.Outcome);
        var rebound = await CallAsync(f, Post("partner-dns", "tickets-nodedupe", new { title = "b" }));
        Assert.Equal(OutboundOutcome.NotSent, rebound.Outcome);
        Assert.Equal("target_blocked", rebound.Reason);
        var internalNet = await CallAsync(f, Post("partner-dns", "tickets-nodedupe", new { title = "c" }));
        Assert.Equal("target_blocked", internalNet.Reason);
        Assert.Equal(1, mock.Executions("(no-key)"));

        // 重定向不跟随:按拒绝处理,绝不去访问 Location 指向的元数据地址
        var redirect = await CallAsync(f, Get("partner", "redirect"));
        Assert.Equal(OutboundOutcome.Rejected, redirect.Outcome);
        Assert.Equal(302, redirect.StatusCode);
        Assert.Contains("169.254.169.254", redirect.Headers["Location"]);

        // 路径只能落在基础地址之下
        await Assert.ThrowsAsync<ArgumentException>(() => CallAsync(f, Get("partner", "../../_mock/stats")));
        await Assert.ThrowsAsync<ArgumentException>(() => CallAsync(f, Get("partner", "http://169.254.169.254/latest")));
        await Assert.ThrowsAsync<ArgumentException>(() => CallAsync(f, new OutboundRequest
        {
            Target = "partner", Method = HttpMethod.Get, Path = "partners/acme",
            Headers = new Dictionary<string, string> { ["X-Evil"] = "a\r\nHost: evil" },
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => CallAsync(f, new OutboundRequest
        {
            Target = "partner", Method = HttpMethod.Get, Path = "partners/acme",
            Headers = new Dictionary<string, string> { ["X-Request-Id"] = "forged" },
        }));
        var slash = await CallAsync(f, Get("partner", "/partners/acme"));   // 开头的 / 仍相对基础地址
        Assert.Equal(OutboundOutcome.Succeeded, slash.Outcome);
    }

    [Fact]
    public async Task A_write_whose_response_is_lost_is_never_resent_by_the_transport()
    {
        using var f = NewHost();

        // 读走连接池(同一连接);写每次独立连接并带 Connection: close,不依赖传输层「复用连接失败后重试」的内部判定
        await CallAsync(f, Get("partner", "partners/a"));
        await CallAsync(f, Get("partner", "partners/b"));
        var reads = mock.State.ReceivedHeaders.TakeLast(2).Select(h => h[MockPartnerState.ConnectionKey]).ToArray();
        Assert.Equal(reads[0], reads[1]);

        mock.Script("tickets-nodedupe", "ok", "drop", "drop");
        var ok = await CallAsync(f, Post("partner", "tickets-nodedupe", null));
        Assert.Equal(OutboundOutcome.Succeeded, ok.Outcome);
        var lost = await CallAsync(f, Post("partner", "tickets-nodedupe", null));
        Assert.Equal(OutboundOutcome.Unknown, lost.Outcome);
        Assert.Contains(lost.Reason, new[] { "response_ended", "transport_error" });
        Assert.Equal(2, mock.Executions("(no-key)"));   // 恰好各执行一次:结果未知如实上报,不被底层掩盖
        var writes = mock.State.ReceivedHeaders.TakeLast(2).ToArray();
        Assert.NotEqual(writes[0][MockPartnerState.ConnectionKey], writes[1][MockPartnerState.ConnectionKey]);
        Assert.All(writes, h => Assert.Equal("close", h["Connection"]));

        // 带请求体与幂等标识的写:同样只执行一次,幂等标识原样传出
        var withBody = await CallAsync(f, Post("partner", "tickets", new { title = "x", amount = 1 }, key: "order-42"));
        Assert.Equal(OutboundOutcome.Succeeded, withBody.Outcome);
        Assert.Equal("order-42", LastHeaders()["Idempotency-Key"]);
        Assert.Equal("close", LastHeaders()["Connection"]);
        var lostWithBody = await CallAsync(f, Post("partner", "tickets-nodedupe", new { title = "y" }));
        Assert.Equal(OutboundOutcome.Unknown, lostWithBody.Outcome);
        Assert.Equal(3, mock.Executions("(no-key)"));
        Assert.Equal(1, mock.Executions("order-42"));
    }

    [Fact]
    public async Task Response_bodies_are_bounded_and_query_strings_are_never_recorded()
    {
        using var f = NewHost(s => s["TenonAdmin:Integration:Outbound:MaxResponseBytes"] = "65536");
        var big = await CallAsync(f, Get("partner", "big?kb=200"));
        Assert.Equal(OutboundOutcome.Succeeded, big.Outcome);
        Assert.True(big.BodyTruncated);
        Assert.Equal(65536, big.Body!.Length);

        await CallAsync(f, Get("partner", "partners/acme?token=query-secret-123&x=1"));
        var logs = await LogsAsync(f);
        Assert.Equal($"{mock.BaseUrl}/api/partners/acme", logs[^1].Url);
        Assert.DoesNotContain("query-secret-123", JsonSerializer.Serialize(logs, RawJson));
    }

    [Fact]
    public async Task A_custom_adapter_overrides_only_protocol_steps_and_reuses_the_infrastructure()
    {
        using var f = NewHost();
        using var scope = f.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<DemoPartnerClient>();

        var created = await client.CreateTicketAsync("演示工单", 12.5m, "demo-key-1");
        Assert.Equal(OutboundOutcome.Succeeded, created.Outcome);
        Assert.StartsWith("T", created.ReadJson<PartnerTicketResult>()!.TicketNo);
        var headers = LastHeaders();
        Assert.True(headers.ContainsKey("X-Partner-Signature"));   // 自定义签名认证
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.Equal(created.CallId, headers["X-Request-Id"]);
        Assert.Equal("demo-key-1", headers["Idempotency-Key"]);

        mock.Script("tickets", "business-reject");
        var rejected = await client.CreateTicketAsync("演示工单", 99m, "demo-key-2");
        Assert.Equal(200, rejected.StatusCode);
        Assert.Equal(OutboundOutcome.Rejected, rejected.Outcome);   // 默认分类会记成成功;适配器识别出业务拒绝
        Assert.Equal("business_rejected", rejected.Reason);

        mock.Script("tickets", "accept");
        var accepted = await client.CreateTicketAsync("演示工单", 5m, "demo-key-3");
        Assert.Equal(OutboundOutcome.Accepted, accepted.Outcome);
        Assert.Equal(IntegrationErrorCode.OutboundAccepted, (int)Assert.Throws<TenonAdmin.Core.AdminException>(() => accepted.EnsureSucceeded(allowAccepted: false)).Code);
        accepted.EnsureSucceeded();

        var found = await client.FindByKeyAsync("demo-key-1");
        Assert.Equal("done", found!.Status);
        Assert.Equal(1, mock.Executions("demo-key-1"));

        var logs = await LogsAsync(f);
        Assert.Equal(["ticket.create", "ticket.create", "ticket.create", "ticket.query"], logs.Select(l => l.Operation!).ToArray());
        Assert.Equal([OutboundOutcome.Succeeded, OutboundOutcome.Rejected, OutboundOutcome.Accepted, OutboundOutcome.Succeeded], logs.Select(l => l.Outcome).ToArray());
        Assert.All(logs, l => Assert.Equal(DemoPartnerClient.Target, l.Target));
    }

    [Fact]
    public async Task A_replaced_credential_source_is_used_without_touching_adapters()
    {
        using var f = NewHost(overrides: s => s.Replace(ServiceDescriptor.Singleton<IOutboundCredentialProvider>(new VaultCredentialProvider())));
        var response = await CallAsync(f, Get("partner-nosecret", "partners/acme"));
        Assert.Equal(OutboundOutcome.Succeeded, response.Outcome);
        Assert.Equal("Bearer " + MockPartnerFixture.Token, LastHeaders()["Authorization"]);
    }

    [Fact]
    public async Task Secrets_never_reach_records_responses_admin_views_or_logs()
    {
        const string sentinel = "sentinel-secret";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var malformedServer = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? line;
            do
            {
                line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) throw new EndOfStreamException("客户端未发送完整请求头。");
            } while (line.Length > 0);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\n{sentinel} invalid: x\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        });
        var capture = new CapturingLoggerProvider();
        using var f = NewHost(
            extra: s => MockPartnerFixture.AddTarget(s, "malformed", $"http://127.0.0.1:{port}/", null, null),
            overrides: s => s.AddSingleton<ILoggerProvider>(capture));
        await CallAsync(f, Get("partner", "partners/acme"));
        await CallAsync(f, Get("partner-header", "partners/acme"));
        await CallAsync(f, Get("partner-basic", "partners/acme"));
        await CallAsync(f, Get("partner-wrong", "partners/acme"));
        await CallAsync(f, Get("partner-nosecret", "partners/acme"));
        await CallAsync(f, Get("partner-localhost", "partners/acme"));
        await CallAsync(f, Get("partner", "slow?ms=3000", TimeSpan.FromMilliseconds(200)));
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DemoPartnerClient>().CreateTicketAsync("t", 1, "leak-check");
        var malformed = await CallAsync(f, Get("malformed", "response")).WaitAsync(TimeSpan.FromSeconds(5));
        await malformedServer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OutboundOutcome.Unknown, malformed.Outcome);
        Assert.Equal("invalid_response", malformed.Reason);
        var authFailure = await CallAsync(f, new OutboundRequest
        {
            Target = "partner", Method = HttpMethod.Get, Path = "partners/acme", Operation = "test.auth-failure",
            Authenticate = (_, _, _) => throw new InvalidOperationException(sentinel + "-auth"),
        });
        var classifierFailure = await CallAsync(f, new OutboundRequest
        {
            Target = "partner", Method = HttpMethod.Get, Path = "partners/acme", Operation = "test-classifier-failure",
            Classify = _ => throw new InvalidOperationException(sentinel + "-classifier"),
        });

        var admin = await AuthProbe.SuperAdmin(f);
        var page = await admin.Send(HttpMethod.Get, "/api/v1/integration/outbound-log/page?size=100");
        var targets = await admin.Send(HttpMethod.Get, "/api/v1/integration/outbound-log/targets");
        Assert.Equal(0, page.Body.Code());
        Assert.Equal(11, page.Body.GetProperty("data").GetProperty("total").GetInt32());

        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("tenon:" + MockPartnerFixture.Token));
        var surfaces = new[]
        {
            JsonSerializer.Serialize(await LogsAsync(f), RawJson),
            page.Body.GetRawText(),
            targets.Body.GetRawText(),
            capture.AllText,
            JsonSerializer.Serialize(new[] { malformed, authFailure, classifierFailure }, RawJson),
        };
        foreach (var secret in new[] { MockPartnerFixture.Token, "wrong-secret", basicToken, sentinel })
            Assert.All(surfaces, text => Assert.DoesNotContain(secret, text));

        var views = targets.Body.GetProperty("data").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        Assert.True(views["partner"].GetProperty("hasCredential").GetBoolean());
        Assert.True(views[DemoPartnerClient.Target].GetProperty("hasCredential").GetBoolean());
        Assert.False(views["partner-nosecret"].GetProperty("hasCredential").GetBoolean());
        Assert.Equal((int)OutboundAuthType.Header, views["partner-header"].GetProperty("authType").GetInt32());
        Assert.Equal("X-Partner-Key", views["partner-header"].GetProperty("authHeaderName").GetString());
        Assert.Equal($"{mock.BaseUrl}/api/", views["partner"].GetProperty("baseUrl").GetString());
    }

    [Fact]
    public async Task Outbound_records_are_admin_only_and_filterable()
    {
        using var f = NewHost();
        var ok = await CallAsync(f, Get("partner", "partners/acme"));
        await CallAsync(f, Get("partner", "status/503"));
        await CallAsync(f, Get("partner-header", "partners/acme"));

        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/integration/outbound-log/page"));
        await AuthProbe.AssertUnauthorized(await f.CreateClient().GetAsync("/api/v1/integration/outbound-log/targets"));
        var limited = await AuthProbe.PingOnly(f);
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/integration/outbound-log/page"));
        await AuthProbe.AssertForbidden(await limited.GetAsync("/api/v1/integration/outbound-log/targets"));

        var admin = await AuthProbe.SuperAdmin(f);
        async Task<JsonElement> Page(string query) =>
            (await admin.Send(HttpMethod.Get, "/api/v1/integration/outbound-log/page?" + query)).Body.GetProperty("data");

        Assert.Equal(2, (await Page("target=partner")).GetProperty("total").GetInt32());
        var unknown = await Page($"outcome={(int)OutboundOutcome.Unknown}");
        Assert.Equal(1, unknown.GetProperty("total").GetInt32());
        Assert.Equal("http_503", unknown.GetProperty("items")[0].GetProperty("reason").GetString());
        var byCall = await Page("callId=" + ok.CallId);
        Assert.Equal(1, byCall.GetProperty("total").GetInt32());
        Assert.Equal("partner", byCall.GetProperty("items")[0].GetProperty("target").GetString());
        Assert.Equal(3, (await Page("operation=test")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Page("deliveryId=1")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Retention_also_deletes_expired_outbound_records()
    {
        using var f = NewHost(s => s["TenonAdmin:Integration:Retention:BatchSize"] = "2");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var now = DateTime.Now;
        await db.Insertable(new[] { -60, -31, -45, -29, 0 }
            .Select(days => new IntegrationOutboundLog { Target = "partner", HttpMethod = "GET", Url = "u", CallId = "c" + days, CreateTime = now.AddDays(days) })
            .ToList()).ExecuteCommandAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IIntegrationRetentionService>().CleanupAsync();
        Assert.Equal(3, result.OutboundLogs);
        Assert.Equal(new HashSet<string> { "c-29", "c0" }, (await db.Queryable<IntegrationOutboundLog>().Select(l => l.CallId).ToListAsync()).ToHashSet());
        Assert.Contains("出站调用记录 3 行", result.ToString());
    }

    /// <summary>可编排的 DNS:按主机名依次返回预设答案(null 表示解析失败),每个答案都照常经过地址策略。</summary>
    private sealed class ScriptedDnsClientFactory : OutboundHttpClientFactory
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<IPAddress?>> _answers = new(StringComparer.OrdinalIgnoreCase);

        public void Answer(string host, IPAddress? address) =>
            _answers.GetOrAdd(host, _ => new ConcurrentQueue<IPAddress?>()).Enqueue(address);

        protected override ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            if (_answers.TryGetValue(host, out var queue) && queue.TryDequeue(out var address))
                return address is null
                    ? ValueTask.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound))
                    : ValueTask.FromResult<IPAddress[]>([address]);
            return base.ResolveAsync(host, cancellationToken);
        }
    }

    /// <summary>模拟密钥管理服务的凭据来源。</summary>
    private sealed class VaultCredentialProvider : IOutboundCredentialProvider
    {
        public ValueTask<OutboundCredential?> GetAsync(OutboundTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OutboundCredential?>(new OutboundCredential(MockPartnerFixture.Token));
    }
}
