using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WebhookNodeHandler"/> 的契约测试(M3a-1 Task 8,首个真实 <see cref="IWorkflowNodeHandler"/> 实现)。
/// 除 <see cref="The_webhook_http_call_happens_outside_any_database_transaction"/>(需 DB/宿主)与 H 组(需
/// <see cref="WorkflowAppFactory"/> 解析 DI)外,全部纯 <see cref="FakeTransport"/> + handler 直调,零真实网络、
/// 零 DB、不起宿主——分类逻辑是纯函数 + HTTP,拉起整个宿主只会让每条测试慢 100 倍却什么都不多证明。
/// </summary>
public class WfWebhookNodeHandlerTests
{
    private const string Password = "Test@123456";
    private const string DefaultUrl = "http://example.com/hook";

    // ── A 组:ResolveTimeout 纯函数 ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TimeoutCases))]
    public void Timeout_is_clamped_and_capped_by_the_deadline(int? configured, TimeSpan remaining, TimeSpan expected)
    {
        var now = DateTimeOffset.UtcNow;
        var actual = WebhookNodeHandler.ResolveTimeout(configured, now + remaining, now);
        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> TimeoutCases()
    {
        yield return [null!, TimeSpan.FromHours(1), TimeSpan.FromSeconds(30)];
        yield return [500, TimeSpan.FromHours(1), TimeSpan.FromSeconds(120)];
        yield return [0, TimeSpan.FromHours(1), TimeSpan.FromSeconds(1)];
        yield return [-1, TimeSpan.FromHours(1), TimeSpan.FromSeconds(1)];
        yield return [120, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)];
    }

    // ── B 组:状态码分类(★ 头号交付物)────────────────────────────────────────

    [Theory]
    [InlineData(200, WfNodeExecutionResultType.Succeeded, null)]
    [InlineData(204, WfNodeExecutionResultType.Succeeded, null)]
    [InlineData(299, WfNodeExecutionResultType.Succeeded, null)]
    [InlineData(301, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(302, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(400, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(401, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(403, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(404, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(405, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(408, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(409, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(410, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(422, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(423, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(425, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(429, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(500, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(501, WfNodeExecutionResultType.TerminalFailure, 48029)]
    [InlineData(502, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(503, WfNodeExecutionResultType.RetryableFailure, 48029)]
    [InlineData(504, WfNodeExecutionResultType.RetryableFailure, 48029)]
    public async Task Http_status_maps_to_the_contracted_result_type(
        int status, WfNodeExecutionResultType expected, int? expectedErrorCode)
    {
        var transport = FakeTransport.Status((HttpStatusCode)status);
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(expected, result.Type);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    // ── C 组:Retry-After ────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_after_delta_seconds_is_passed_through()
    {
        var transport = FakeTransport.Status((HttpStatusCode)429, retryAfter: "120");
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result.Type);
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_http_date_is_converted_to_a_delta()
    {
        var date = DateTimeOffset.UtcNow.AddSeconds(60);
        var transport = FakeTransport.Status((HttpStatusCode)503, retryAfter: date.UtcDateTime.ToString("R"));
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result.Type);
        Assert.NotNull(result.RetryAfter);
        Assert.InRange(result.RetryAfter!.Value, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(65));
    }

    [Fact]
    public async Task A_non_positive_retry_after_yields_null()
    {
        var transport = FakeTransport.Status((HttpStatusCode)503, retryAfter: "0");
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result.Type);
        Assert.Null(result.RetryAfter);
    }

    // ── D 组:异常分类(★ 含最要紧的 OCE 边界)─────────────────────────────────

    [Fact]
    public async Task A_request_timeout_becomes_a_retryable_failure_and_no_cancellation_escapes()
    {
        var transport = FakeTransport.Hanging();
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookTimeoutSeconds = 1 };

        WfNodeExecutionResult? result = null;
        var ex = await Record.ExceptionAsync(async () => result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None));

        Assert.Null(ex); // 没有 OCE 逸出
        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result!.Type);
        Assert.Equal(48031, result.ErrorCode);
    }

    [Fact]
    public async Task An_externally_cancelled_call_lets_the_cancellation_escape()
    {
        var transport = FakeTransport.Hanging();
        var handler = NewHandler(transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 进门前就取消

        var ex = await Record.ExceptionAsync(() => handler.ExecuteAsync(Ctx(Props()), cts.Token));

        Assert.Equal(0, transport.SendCount); // 副作用先断
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task A_cancellation_mid_flight_escapes_instead_of_becoming_a_result()
    {
        var sendStarted = new TaskCompletionSource();
        var transport = FakeTransport.Hanging();
        transport.OnSend = () => sendStarted.TrySetResult();
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookTimeoutSeconds = 120 };
        using var cts = new CancellationTokenSource();

        var task = handler.ExecuteAsync(Ctx(props), cts.Token);
        await sendStarted.Task; // 确认已进入飞行(而不是进门前)
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() => task); // 没有产生任何 WfNodeExecutionResult
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task A_transport_failure_becomes_a_retryable_failure()
    {
        var transport = FakeTransport.Throwing(new HttpRequestException("boom-connection-refused"));
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result.Type);
        Assert.Equal(48031, result.ErrorCode);
        Assert.False(string.IsNullOrEmpty(result.Summary));
        Assert.Contains("boom-connection-refused", result.Summary);
    }

    [Fact]
    public async Task An_unexpected_exception_is_not_swallowed()
    {
        var transport = FakeTransport.Throwing(new InvalidOperationException("kaboom"));
        var handler = NewHandler(transport);

        var ex = await Record.ExceptionAsync(() => handler.ExecuteAsync(Ctx(Props()), CancellationToken.None));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal(1, transport.SendCount);
    }

    // ── E 组:配置校验(先断副作用为 0,再断结果)───────────────────────────────

    [Fact]
    public async Task A_blank_url_is_a_terminal_config_failure()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(new WfNodeProps { WebhookUrl = null }), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Fact]
    public async Task A_non_http_scheme_is_a_terminal_config_failure()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(
            Ctx(new WfNodeProps { WebhookUrl = "file:///etc/passwd" }), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Fact]
    public async Task A_blocked_metadata_address_never_opens_a_socket()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(
            Ctx(new WfNodeProps { WebhookUrl = "http://169.254.169.254/latest/meta-data" }), CancellationToken.None);

        Assert.Equal(0, transport.SendCount); // SSRF 围栏真的接上了的唯一直接证据
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Fact]
    public async Task A_header_with_crlf_is_a_terminal_config_failure()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);
        var props = new WfNodeProps
        {
            WebhookUrl = DefaultUrl,
            WebhookHeaders = new Dictionary<string, string?> { ["X-Evil"] = "value\r\nX-Injected: 1" },
        };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Fact]
    public async Task A_host_header_is_rejected()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);
        var props = new WfNodeProps
        {
            WebhookUrl = DefaultUrl,
            WebhookHeaders = new Dictionary<string, string?> { ["Host"] = "internal.svc" },
        };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Theory]
    [InlineData("TRACE")]
    [InlineData("BAD METHOD")]
    public async Task An_unsupported_method_is_a_terminal_config_failure(string method)
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookMethod = method };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.TerminalFailure, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    // ── F 组:请求组装 ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_outgoing_request_carries_the_context_and_never_the_variables()
    {
        // handler 在 using(request) 里对请求消息(含 Content)做了 Dispose,ExecuteAsync 返回后再读
        // req.Content 会炸 ObjectDisposedException——body/Content-Type 必须在 SendAsync 回调内、
        // 消息尚未释放时就读出来捕获。
        string? capturedBody = null;
        string? capturedContentType = null;
        var transport = new FakeTransport(async (req, ct) =>
        {
            capturedContentType = req.Content?.Headers.ContentType?.MediaType;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var handler = NewHandler(transport);
        var props = new WfNodeProps
        {
            WebhookUrl = DefaultUrl,
            WebhookHeaders = new Dictionary<string, string?> { ["X-Trace"] = "abc" },
        };
        var ctx = Ctx(props, attempt: 3);

        await handler.ExecuteAsync(ctx, CancellationToken.None);

        var req = transport.LastRequest!;
        Assert.Equal(HttpMethod.Post, req.Method); // 默认值,props 里不配 method
        Assert.Equal(new Uri(DefaultUrl), req.RequestUri);
        Assert.True(req.Headers.TryGetValues("X-Trace", out var values));
        Assert.Equal("abc", values!.Single());
        Assert.Equal("application/json", capturedContentType);

        var root = JsonDocument.Parse(capturedBody!).RootElement;
        Assert.Equal(ctx.ExecutionKey, root.GetProperty("executionKey").GetString());
        Assert.Equal(ctx.InstanceId, root.GetProperty("instanceId").GetInt64());
        Assert.Equal(ctx.NodeId, root.GetProperty("nodeId").GetString());
        Assert.Equal(ctx.Attempt, root.GetProperty("attempt").GetInt32());
        Assert.Equal(ctx.TokenId, root.GetProperty("tokenId").GetInt64());
        Assert.Equal(ctx.NodeVisitId, root.GetProperty("nodeVisitId").GetInt64());
        Assert.Equal(ctx.DefinitionVersionId, root.GetProperty("definitionVersionId").GetInt64());
        Assert.Equal(ctx.BusinessKey, root.GetProperty("businessKey").GetString());
        Assert.False(root.TryGetProperty("variablesJson", out _)); // D5:PII 泄漏面,刻意不含
        Assert.False(root.TryGetProperty("variables", out _));
    }

    [Fact]
    public async Task A_get_webhook_sends_no_body()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookMethod = "get" }; // 小写,顺带钉大小写不敏感

        await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(HttpMethod.Get, transport.LastRequest!.Method);
        Assert.Null(transport.LastRequest!.Content);
    }

    [Fact]
    public async Task An_oversized_response_body_is_capped_and_truncated()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK, body: new string('a', 100_000));
        var handler = NewHandler(transport);

        var result = await handler.ExecuteAsync(Ctx(Props()), CancellationToken.None);

        Assert.Equal(512, result.Summary!.Length);
        Assert.Equal(4096, result.OutputJson!.Length); // MaxResponseLogBytes 默认字面值
    }

    // ── G 组:ManualFallback 开关 ─────────────────────────────────────────────

    [Fact]
    public async Task On_failure_manual_turns_a_terminal_status_into_a_manual_fallback()
    {
        var transport = FakeTransport.Status(HttpStatusCode.NotFound);
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookOnFailure = WfWebhookFailureAction.Manual };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Equal(48029, result.ErrorCode); // 码不变
    }

    [Fact]
    public async Task On_failure_manual_also_covers_config_failures()
    {
        var transport = FakeTransport.Status(HttpStatusCode.OK);
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = null, WebhookOnFailure = WfWebhookFailureAction.Manual };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(0, transport.SendCount);
        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Equal(48030, result.ErrorCode);
    }

    [Fact]
    public async Task On_failure_manual_does_not_touch_retryable_results()
    {
        var transport = FakeTransport.Status((HttpStatusCode)503);
        var handler = NewHandler(transport);
        var props = new WfNodeProps { WebhookUrl = DefaultUrl, WebhookOnFailure = WfWebhookFailureAction.Manual };

        var result = await handler.ExecuteAsync(Ctx(props), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.RetryableFailure, result.Type); // 仍是重试,不是人工
    }

    // ── H 组:DI 与可替换性(需 WorkflowAppFactory)────────────────────────────

    [Fact]
    public async Task The_webhook_handler_is_registered_and_resolvable()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IWorkflowNodeHandler>().ToList();
        var webhook = Assert.Single(handlers, h => h.NodeType == WfNodeType.Webhook);
        Assert.IsType<WebhookNodeHandler>(webhook);
    }

    [Fact]
    public void A_pre_registered_handler_wins_over_the_built_in_one()
    {
        // 裸容器 + 真实消费者路径:消费者在 AddTenonAdminWorkflow 之前 AddScoped 自己的 handler
        // (十件套 BuildProvider 同款姿势)。WorkflowAppFactory 的 Overrides 跑在 AddTenonAdminWorkflow
        // 之后,Insert(0, …) 只是把顺序焊死在 DI 容器语义上,与 WorkflowSetup 里写了什么无关——鉴别力为零。
        var services = new ServiceCollection();
        var jobs = new AdminJobsOptions();
        services.AddSingleton(jobs);
        services.AddSingleton(new TenonAdmin.Services.JobHttpClient(jobs));
        services.AddScoped<IWorkflowNodeHandler>(
            _ => new FakeNodeHandler(WfNodeExecutionResult.Succeeded(), WfNodeType.Webhook));
        services.AddTenonAdminWorkflow();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IWorkflowNodeHandler>()
            .Where(h => h.NodeType == WfNodeType.Webhook).ToList();

        Assert.IsType<FakeNodeHandler>(handlers.First());   // 消费者胜出
        Assert.IsType<WebhookNodeHandler>(handlers.Last()); // 内置仍在(TryAddEnumerable 是追加不是替换)
    }

    // ── I 组:事务边界(需 DB,D8)──────────────────────────────────────────────

    /// <summary>
    /// D8:探针挪到假 <see cref="HttpMessageHandler.SendAsync"/> 内部——即"socket 即将打开的那一刻"没有事务,
    /// 跑的是真的 <see cref="WebhookNodeHandler"/> 经真的 <see cref="WfNodeExecutionDispatcher"/>。不是 T1
    /// 的复制:T1 的探针挂在 <c>FakeNodeHandler.OnExecute</c> 上,证明的是"handler 被调用时"没有事务。
    /// </summary>
    [Fact]
    public async Task The_webhook_http_call_happens_outside_any_database_transaction()
    {
        using var f = new WorkflowAppFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var jobs = scope.ServiceProvider.GetRequiredService<AdminJobsOptions>();

        var s = await TxStartAsync(f, db, engine);
        var execution = await TxBuildExecutionAsync(db, s);

        // 初值故意设成"看见了事务"——只有 SendAsync 真的跑过并把它翻成 false,断言才有鉴别力(照 T1 姿势)。
        var sawTran = true;
        var sawTranObj = true;
        var transport = new FakeTransport((_, _) =>
        {
            sawTran = db.Ado.IsAnyTran();
            sawTranObj = db.Ado.Transaction is not null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        });
        var handler = new WebhookNodeHandler(new HttpClient(transport), jobs, TimeProvider.System);
        var dispatcher = new WfNodeExecutionDispatcher(db, [handler], engine, TimeProvider.System);

        var status = await dispatcher.RunAsync(execution.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.False(sawTran, "webhook 调用时不应处于任何数据库事务中。");
        Assert.False(sawTranObj, "webhook 调用时 db.Ado.Transaction 应为 null。");
        Assert.Equal(1, transport.SendCount);
        Assert.Equal(WfNodeExecutionStatus.Succeeded, status);
    }

    // ────────────────────────── 脚手架 ──────────────────────────

    private sealed class FakeTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public Action? OnSend { get; set; }

        public static FakeTransport Status(HttpStatusCode status, string? retryAfter = null, string? body = null) =>
            new((_, _) =>
            {
                var response = new HttpResponseMessage(status);
                if (body is not null) response.Content = new StringContent(body);
                if (retryAfter is not null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                return Task.FromResult(response);
            });

        public static FakeTransport Throwing(Exception ex) => new((_, _) => throw ex);

        public static FakeTransport Hanging() => new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequest = request;
            OnSend?.Invoke();
            return await handle(request, cancellationToken);
        }
    }

    private static WebhookNodeHandler NewHandler(FakeTransport transport, AdminJobsOptions? jobs = null, TimeProvider? time = null) =>
        new(new HttpClient(transport), jobs ?? new AdminJobsOptions(), time ?? TimeProvider.System);

    private static WfNodeProps Props(string url = DefaultUrl) => new() { WebhookUrl = url };

    private static WfNodeExecutionContext Ctx(WfNodeProps? props, int attempt = 1, DateTimeOffset? deadline = null) => new()
    {
        ExecutionKey = "wh-exec-1",
        InstanceId = 100,
        TokenId = 200,
        NodeVisitId = 300,
        NodeId = "node1",
        NodeType = WfNodeType.Webhook,
        DefinitionVersionId = 400,
        StarterUserId = 500,
        BusinessKey = "biz-1",
        NodeProps = props,
        VariablesJson = """{"secret":"shh"}""",
        Attempt = attempt,
        DeadlineAtUtc = deadline ?? DateTimeOffset.UtcNow.AddMinutes(5),
    };

    // ── W-TX 专属脚手架(照 WfNodeExecutionDispatcherTests 的姿势,node1 的 Props 上叠加 WebhookUrl)──

    private sealed record TxScaffold(long InstanceId, WfToken Token, long DefinitionVersionId);

    private static async Task<TxScaffold> TxStartAsync(WorkflowAppFactory f, ISqlSugarClient db, IWorkflowEngine engine)
    {
        var admin = f.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await admin.LoginToken("superAdmin", Password));
        var starterId = await TxAddUser(admin, $"wh-starter-{Guid.NewGuid():N}");
        var assigneeId = await TxAddUser(admin, $"wh-assignee-{Guid.NewGuid():N}");

        var version = new WfDefinitionVersion
        {
            DefinitionId = Random.Shared.NextInt64(1, long.MaxValue),
            Version = 1,
            ModelJson = WfModelJson.Serialize(TxBuildModel(assigneeId)),
        };
        await db.Insertable(version).ExecuteCommandAsync();

        var result = await engine.ExecuteAsync(new StartInstanceCmd
        {
            DefinitionVersionId = version.Id,
            StarterUserId = starterId,
        });

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == result.InstanceId && t.Status == WfTokenStatus.Active)
            .FirstAsync();

        return new TxScaffold(result.InstanceId, token, version.Id);
    }

    private static async Task<long> TxAddUser(HttpClient admin, string account)
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
        var response = await admin.PostJson("/api/v1/sys/user", body);
        var env = await response.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<WfNodeExecution> TxBuildExecutionAsync(ISqlSugarClient db, TxScaffold s)
    {
        var scopeKey = WfIdentityHash.NormalizeScopeKey(null);
        var key = WfExecutionKey.Compute(scopeKey, s.InstanceId, s.Token.Id, s.Token.NodeVisitId, "node1", s.DefinitionVersionId);
        var row = new WfNodeExecution
        {
            ExecutionKey = key,
            ScopeKey = scopeKey,
            InstanceId = s.InstanceId,
            TokenId = s.Token.Id,
            NodeVisitId = s.Token.NodeVisitId,
            NodeId = "node1",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = s.DefinitionVersionId,
            MaxAttempts = 3,
        };
        return await WfNodeExecutionStore.EnsureAsync(db, row, CancellationToken.None);
    }

    /// <summary>start → node1(approval,真实 assignee,保持流程 Running/等待)。node1.Props 上叠加
    /// <c>WebhookUrl</c>——execution 行的 <c>NodeType</c> 字段(不是模型节点类型)才是 dispatcher 挑 handler
    /// 的依据,照抄 <c>WfNodeExecutionDispatcherTests</c> 的既有姿势(其 <c>BuildExecutionAsync</c> 同样把
    /// <c>NodeType</c> 写死成 <c>Webhook</c>,与 node1 的模型类型 Approval 无关)。</summary>
    private static WfModel TxBuildModel(long assigneeUserId) => new()
    {
        Root = new WfNode
        {
            Id = "start",
            Type = WfNodeType.Start,
            Next = new WfNode
            {
                Id = "node1",
                Type = WfNodeType.Approval,
                Props = new WfNodeProps
                {
                    Assignee = new WfAssignee
                    {
                        Provider = ApproverProviderKeys.User,
                        Params = new Dictionary<string, JsonElement>
                        {
                            ["userIds"] = JsonSerializer.SerializeToElement(new[] { assigneeUserId }),
                        },
                    },
                    Mode = WfApprovalMode.Any,
                    WebhookUrl = DefaultUrl,
                },
                Next = null,
            },
        },
    };
}
