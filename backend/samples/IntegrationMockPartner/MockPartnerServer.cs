using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace TenonAdmin.Samples.MockPartner;

/// <summary>模拟第三方的配置。</summary>
public sealed class MockPartnerOptions
{
    /// <summary>
    /// 调用方须出示的凭据。接受四种出示方式:<c>Authorization: Bearer {Token}</c>、<c>X-Partner-Key: {Token}</c>、
    /// <c>Authorization: Basic base64(任意用户名:{Token})</c>,或签名(<c>X-Partner-Timestamp</c> + <c>X-Partner-Signature</c>,
    /// 见 <see cref="MockPartnerServer.Sign"/>)。
    /// </summary>
    public string Token { get; set; } = "partner-secret";
}

/// <summary>对方系统里的一张工单。</summary>
public sealed record MockTicket(string TicketNo, string? IdempotencyKey, string Title, decimal Amount, string Status);

/// <summary>
/// 模拟第三方的内存状态(线程安全)。测试与示例通过 <c>/_mock/*</c> 控制接口编排「下一次请求怎么表现」,
/// 并读回业务真正执行了几次——这是验证「不重复执行」的唯一可信依据。
/// </summary>
public sealed class MockPartnerState
{
    private int _seq;
    // ponytail: 示例用单锁保证查找与创建原子化;模拟服务吞吐成为瓶颈时再改按幂等键加锁。
    internal object TicketGate { get; } = new();
    public ConcurrentDictionary<string, MockTicket> Tickets { get; } = new();
    public ConcurrentDictionary<string, string> TicketByKey { get; } = new();
    public ConcurrentDictionary<string, int> Executions { get; } = new();
    public ConcurrentDictionary<string, int> Requests { get; } = new();
    public ConcurrentDictionary<string, ConcurrentQueue<string>> Scripts { get; } = new();
    public ConcurrentQueue<Dictionary<string, string>> ReceivedHeaders { get; } = new();

    /// <summary>记录请求头时附带的伪键:承载该请求的连接标识(验证连接是否被复用)。</summary>
    public const string ConnectionKey = ":connection";

    public string NextTicketNo() => "T" + Interlocked.Increment(ref _seq).ToString("D6");

    /// <summary>编排某个写接口接下来几次请求的表现(按顺序消费,用完恢复 <c>ok</c>)。</summary>
    public void Script(string route, IEnumerable<string> behaviors)
    {
        var queue = Scripts.GetOrAdd(route, _ => new ConcurrentQueue<string>());
        foreach (var behavior in behaviors) queue.Enqueue(behavior);
    }

    public string NextBehavior(string route) =>
        Scripts.TryGetValue(route, out var queue) && queue.TryDequeue(out var behavior) ? behavior : "ok";

    private TaskCompletionSource _held = NewGate();

    /// <summary>放行被 <c>hold</c> 挂起的请求:它们随即断开连接、不执行业务(调用方拿到「结果未知」的时刻由测试决定)。</summary>
    public void ReleaseHeld() => _held.TrySetResult();

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _held.Task.WaitAsync(cancellationToken);

    public void Reset()
    {
        Tickets.Clear();
        TicketByKey.Clear();
        Executions.Clear();
        Requests.Clear();
        Scripts.Clear();
        while (ReceivedHeaders.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _held, NewGate()).TrySetResult();   // 上一场景没放行的挂起请求一并放掉
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// 模拟第三方系统(业务是「接收工单」)。两类写接口:
/// <list type="bullet">
/// <item><c>POST /api/tickets</c>:按 <c>Idempotency-Key</c> 去重,且可按键查询结果(<c>GET /api/tickets/by-key/{key}</c>);</item>
/// <item><c>POST /api/tickets-nodedupe</c>:既不去重也不能查询(典型的「结果未知只能人工核对」对象)。</item>
/// </list>
/// 行为脚本(<c>POST /_mock/script</c>):<c>ok</c> 正常、<c>accept</c> 异步受理(202,稍后由 <c>/_mock/tickets/{no}/complete</c> 完结)、
/// <c>reject</c> 业务拒绝(422)、<c>business-reject</c> 业务拒绝但 HTTP 200(<c>{"status":"rejected"}</c>,须由适配器识别)、
/// <c>unavailable</c> 未处理(503 + Retry-After)、<c>throttle</c> 限流(429 + Retry-After)、<c>request-timeout</c> 408、
/// <c>fail-after-commit</c> 执行后 500、<c>unavailable-after-commit</c> 执行后 503 + Retry-After、
/// <c>drop</c> 执行后直接断开连接(模拟「远端成功但响应丢失」)、<c>slow</c> 延迟 10 秒、
/// <c>hold</c> 挂起到 <see cref="MockPartnerState.ReleaseHeld"/> 放行后断开连接且不执行(由测试决定调用方何时拿到结果未知,不靠墙钟)。
/// 另有 <c>GET /api/status/{code}</c> 原样返回指定状态码、<c>GET /api/big?kb=</c> 返回指定大小的响应体,供结果分类与上限验证。
/// </summary>
public static class MockPartnerServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>签名出示方式:<c>hex(HMACSHA256(Token, "{METHOD}\n{path}\n{timestamp}"))</c>(小写十六进制)。</summary>
    public static string Sign(string token, string method, string path, string timestamp)
    {
        var data = System.Text.Encoding.UTF8.GetBytes($"{method.ToUpperInvariant()}\n{path}\n{timestamp}");
        return Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token), data));
    }

    private static bool Authorized(HttpRequest request, string token)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization == "Bearer " + token) return true;
        if (request.Headers["X-Partner-Key"].ToString() == token) return true;
        if (authorization.StartsWith("Basic ", StringComparison.Ordinal))
        {
            try
            {
                var pair = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..]));
                var colon = pair.IndexOf(':');
                if (colon > 0 && pair[(colon + 1)..] == token) return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        var signature = request.Headers["X-Partner-Signature"].ToString();
        var timestamp = request.Headers["X-Partner-Timestamp"].ToString();
        return signature.Length > 0 && timestamp.Length > 0
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(signature),
                System.Text.Encoding.ASCII.GetBytes(Sign(token, request.Method, request.Path.Value ?? "", timestamp)));
    }

    /// <summary>构建可运行的应用(未启动)。<paramref name="url"/> 用 <c>http://127.0.0.1:0</c> 即随机端口。</summary>
    public static WebApplication Build(string[]? args = null, MockPartnerOptions? options = null, string? url = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args ?? []);
        builder.Services.AddSingleton(options ?? new MockPartnerOptions());
        builder.Services.AddSingleton<MockPartnerState>();
        if (url is not null) builder.WebHost.UseUrls(url);
        var app = builder.Build();
        Map(app);
        return app;
    }

    /// <summary>已启动应用的实际监听地址(随机端口时用)。</summary>
    public static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    /// <summary>映射全部端点。</summary>
    public static void Map(WebApplication app)
    {
        var state = app.Services.GetRequiredService<MockPartnerState>();
        var options = app.Services.GetRequiredService<MockPartnerOptions>();

        // 业务接口统一要求凭据(Bearer、X-Partner-Key、Basic 或签名任一,见 MockPartnerOptions.Token);控制接口 /_mock/* 不要求(仅本地测试用)
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                headers[MockPartnerState.ConnectionKey] = ctx.Connection.Id;
                state.ReceivedHeaders.Enqueue(headers);
                if (!Authorized(ctx.Request, options.Token))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                    return;
                }
            }
            await next();
        });

        app.MapGet("/api/partners/{code}", (string code) =>
            code == "missing"
                ? Results.Json(new { error = "partner_not_found" }, statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(new { code, name = "工单对接方 " + code, level = "gold" }));

        app.MapGet("/api/slow", async (int ms, CancellationToken ct) =>
        {
            await Task.Delay(ms, ct);
            return Results.Ok(new { waited = ms });
        });

        app.MapGet("/api/redirect", () => Results.Redirect("http://169.254.169.254/latest/meta-data/"));

        app.MapGet("/api/status/{code:int}", (int code, HttpContext ctx) =>
        {
            if (code is 429 or 503) ctx.Response.Headers.RetryAfter = "2";
            // 204/304 不允许带响应体
            return code is 204 or 304 ? Results.StatusCode(code) : Results.Json(new { status = code }, statusCode: code);
        });

        app.MapGet("/api/big", (int kb) => Results.Text(new string('x', kb * 1024), "text/plain"));

        // 显式转 Delegate:单参数 HttpContext 的 lambda 会被当成 RequestDelegate,返回的 IResult 会被丢弃(ASP0016)
        app.MapPost("/api/tickets", (Delegate)((HttpContext ctx) => HandleCreate(ctx, state, "tickets", dedupe: true)));
        app.MapPost("/api/tickets-nodedupe", (Delegate)((HttpContext ctx) => HandleCreate(ctx, state, "tickets-nodedupe", dedupe: false)));

        app.MapGet("/api/tickets/by-key/{key}", (string key) =>
            state.TicketByKey.TryGetValue(key, out var no) && state.Tickets.TryGetValue(no, out var ticket)
                ? Results.Ok(new { ticketNo = ticket.TicketNo, status = ticket.Status })
                : Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound));

        // ── 控制接口 ──
        app.MapPost("/_mock/script", (ScriptInput input) =>
        {
            state.Script(input.Route, input.Behaviors);
            return Results.Ok();
        });
        app.MapPost("/_mock/tickets/{ticketNo}/complete", (string ticketNo, CompleteInput input) =>
        {
            if (!state.Tickets.TryGetValue(ticketNo, out var ticket)) return Results.NotFound();
            state.Tickets[ticketNo] = ticket with { Status = input.Status };
            return Results.Ok();
        });
        app.MapGet("/_mock/stats", () => Results.Ok(new
        {
            executions = state.Executions,
            requests = state.Requests,
            tickets = state.Tickets.Values.OrderBy(t => t.TicketNo),
        }));
        app.MapGet("/_mock/headers", () => Results.Ok(state.ReceivedHeaders.ToArray()));
        app.MapPost("/_mock/reset", () =>
        {
            state.Reset();
            return Results.Ok();
        });
    }

    private static async Task<IResult> HandleCreate(HttpContext ctx, MockPartnerState state, string route, bool dedupe)
    {
        state.Requests.AddOrUpdate(route, 1, (_, n) => n + 1);
        var hasBody = ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0;
        var input = hasBody
            ? await JsonSerializer.DeserializeAsync<TicketInput>(ctx.Request.Body, Json, ctx.RequestAborted) ?? new TicketInput()
            : new TicketInput();
        var key = ctx.Request.Headers["Idempotency-Key"].ToString();
        key = string.IsNullOrWhiteSpace(key) ? null : key;
        var behavior = state.NextBehavior(route);

        switch (behavior)
        {
            case "reject":
                return Results.Json(new { error = "invalid_ticket" }, statusCode: StatusCodes.Status422UnprocessableEntity);
            case "unavailable":
                ctx.Response.Headers.RetryAfter = "1";
                return Results.Json(new { error = "busy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            case "throttle":
                ctx.Response.Headers.RetryAfter = "2";
                return Results.Json(new { error = "too_many_requests" }, statusCode: StatusCodes.Status429TooManyRequests);
            case "request-timeout":
                return Results.Json(new { error = "request_timeout" }, statusCode: StatusCodes.Status408RequestTimeout);
            case "business-reject":
                return Results.Ok(new { status = "rejected", reason = "quota_exceeded" });
            case "slow":
                await Task.Delay(TimeSpan.FromSeconds(10), ctx.RequestAborted);
                break;
            case "hold":
                await state.WaitForReleaseAsync(ctx.RequestAborted);
                ctx.Abort();   // 放行后断开且不执行业务:调用方只能判定结果未知
                return Results.Empty;
        }

        MockTicket ticket;
        // 查找、业务创建、幂等映射与计数必须原子完成,单个 ConcurrentDictionary 操作的线程安全不够。
        lock (state.TicketGate)
        {
            if (dedupe && key is not null && state.TicketByKey.TryGetValue(key, out var existingNo) && state.Tickets.TryGetValue(existingNo, out var existing))
            {
                ctx.Response.Headers["Idempotent-Replayed"] = "true";
                return Results.Json(new { ticketNo = existing.TicketNo, status = existing.Status, replayed = true },
                    statusCode: existing.Status == "pending" ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
            }

            ticket = new MockTicket(state.NextTicketNo(), key, input.Title ?? "", input.Amount, behavior == "accept" ? "pending" : "done");
            state.Tickets[ticket.TicketNo] = ticket;
            if (key is not null) state.TicketByKey[key] = ticket.TicketNo;
            state.Executions.AddOrUpdate(key ?? "(no-key)", 1, (_, n) => n + 1);
        }

        switch (behavior)
        {
            case "drop":
                ctx.Abort();   // 业务已执行,响应在路上丢了
                return Results.Empty;
            case "fail-after-commit":
                return Results.Json(new { error = "internal" }, statusCode: StatusCodes.Status500InternalServerError);
            case "unavailable-after-commit":
                ctx.Response.Headers.RetryAfter = "1";
                return Results.Json(new { error = "busy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            case "accept":
                return Results.Json(new { ticketNo = ticket.TicketNo, status = ticket.Status }, statusCode: StatusCodes.Status202Accepted);
            default:
                return Results.Json(new { ticketNo = ticket.TicketNo, status = ticket.Status }, statusCode: StatusCodes.Status201Created);
        }
    }

    public sealed record TicketInput
    {
        public string? Title { get; init; }
        public decimal Amount { get; init; }
    }

    public sealed record ScriptInput(string Route, string[] Behaviors);

    public sealed record CompleteInput(string Status);
}
