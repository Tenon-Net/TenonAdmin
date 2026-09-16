using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// 预览版门禁:测试专用最小 HTTP <see cref="IWfOutboxTransport"/> + 本地 endpoint。
/// 不改动 <see cref="NoOpWfOutboxTransport"/> 的默认失败语义,也不向核心包引入 HTTP 依赖。
/// </summary>
public class WfOutboxHttpTransportTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Http_transport_moves_pending_through_dispatching_to_dispatched()
    {
        await using var endpoint = await LocalHttpOutboxEndpoint.StartAsync();
        var transport = new HttpWfOutboxTransport(endpoint.BaseUri);
        using var f = NewFactory(transport);
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        Assert.Equal(WfOutboxStatus.Pending, row.Status);

        var status = await CreateDispatcher(db, transport).RunAsync(row.Id, CancellationToken.None);

        Assert.Equal(WfOutboxStatus.Dispatched, status);
        Assert.Equal(1, endpoint.DeliveryCount);
        Assert.Equal(1, endpoint.AppliedCount(row.MessageKey));
        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatched, loaded.Status);
        Assert.Equal(1, loaded.AttemptCount);
        Assert.NotNull(loaded.CompletedAtUtc);
        Assert.Contains(endpoint.SeenStatuses, s => s == WfOutboxStatus.Dispatching);
    }

    [Fact]
    public async Task Http_transport_failure_then_replay_can_dispatch_successfully()
    {
        await using var endpoint = await LocalHttpOutboxEndpoint.StartAsync();
        endpoint.FailNext = 1;
        var transport = new HttpWfOutboxTransport(endpoint.BaseUri);
        using var f = NewFactory(transport);
        var admin = await ClientFor(f, "superAdmin");
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);

        Assert.Equal(WfOutboxStatus.Failed, await CreateDispatcher(db, transport).RunAsync(row.Id, CancellationToken.None));
        var failed = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Failed, failed.Status);
        Assert.Equal(0, endpoint.AppliedCount(row.MessageKey));
        Assert.Contains("forced-fail", failed.LastError, StringComparison.Ordinal);

        var replay = await (await admin.PostAsync(
            $"/api/v1/workflow/outbox/{row.Id}/replay?requestId=wf-outbox-http-replay-1",
            null)).ReadEnvelope();
        Assert.Equal(0, replay.GetProperty("code").GetInt32());
        Assert.Equal((int)WfOutboxStatus.Pending, replay.GetProperty("data").GetProperty("status").GetInt32());

        // 与入队用例一致:把可领时刻推到过去,避开 datetime 秒级舍入边界(见 ReplayFailedAsync 注释)
        await db.Updateable<WfOutbox>()
            .SetColumns(o => new WfOutbox { AvailableAtUtc = DateTime.UtcNow.AddSeconds(-2) })
            .Where(o => o.Id == row.Id)
            .ExecuteCommandAsync();

        Assert.Equal(WfOutboxStatus.Dispatched, await CreateDispatcher(db, transport).RunAsync(row.Id, CancellationToken.None));
        Assert.Equal(1, endpoint.AppliedCount(row.MessageKey));
        var loaded = await db.Queryable<WfOutbox>().Where(o => o.Id == row.Id).FirstAsync();
        Assert.Equal(WfOutboxStatus.Dispatched, loaded.Status);
        Assert.Null(loaded.LastError);
    }

    [Fact]
    public async Task Duplicate_http_delivery_does_not_reapply_business_state()
    {
        await using var endpoint = await LocalHttpOutboxEndpoint.StartAsync();
        var transport = new HttpWfOutboxTransport(endpoint.BaseUri);
        using var f = NewFactory(transport);
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var dispatcher = CreateDispatcher(db, transport);

        Assert.Equal(WfOutboxStatus.Dispatched, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        Assert.Equal(1, endpoint.AppliedCount(row.MessageKey));

        // at-least-once: 同一 MessageKey 再投一次,消费者按幂等键只推进一次业务计数。
        var second = await transport.DispatchAsync(new WfOutboxMessage
        {
            Id = row.Id,
            ExecutionId = row.ExecutionId,
            MessageType = row.MessageType,
            MessageKey = row.MessageKey,
            PayloadJson = row.PayloadJson,
            AttemptCount = 2,
        }, CancellationToken.None);
        Assert.Equal(WfOutboxDispatchOutcome.Succeeded, second.Type);
        Assert.Equal(2, endpoint.DeliveryCount);
        Assert.Equal(1, endpoint.AppliedCount(row.MessageKey));

        // 已 Dispatched 的行不能再被领取,业务状态也不会被 dispatcher 再次推进。
        Assert.Null(await dispatcher.RunAsync(row.Id, CancellationToken.None));
        Assert.Equal(2, endpoint.DeliveryCount);
        Assert.Equal(1, endpoint.AppliedCount(row.MessageKey));
        Assert.Equal(WfOutboxStatus.Dispatched, await StatusAsync(db, row.Id));
    }

    [Fact]
    public async Task Built_in_noop_still_fails_when_http_transport_is_not_registered()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db) = Open(f);
        using var _ = scope;
        var row = await EnqueueAsync(db);
        var dispatcher = scope.ServiceProvider.GetRequiredService<WfOutboxDispatcher>();

        Assert.Equal(WfOutboxStatus.Failed, await dispatcher.RunAsync(row.Id, CancellationToken.None));
        Assert.IsType<NoOpWfOutboxTransport>(scope.ServiceProvider.GetRequiredService<IWfOutboxTransport>());
    }

    private static WorkflowAppFactory NewFactory(HttpWfOutboxTransport transport) => new()
    {
        Overrides = services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IWfOutboxTransport>(_ => transport));
        },
    };

    private static WfOutboxDispatcher CreateDispatcher(ISqlSugarClient db, IWfOutboxTransport transport) =>
        new(db, transport, new WorkflowOptions(), TimeProvider.System, NullLogger<WfOutboxDispatcher>.Instance);

    private static (IServiceScope Scope, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient();
        var scope = f.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }

    private static async Task<WfOutbox> EnqueueAsync(ISqlSugarClient db)
    {
        // 监控/重放 API 经 execution → instance 可见性联表,必须落真实实例行。
        var instance = new WfInstance
        {
            DefinitionVersionId = 1,
            StarterUserId = 1,
            Status = WfInstanceStatus.Running,
            BusinessKey = "http-outbox-" + Guid.NewGuid().ToString("N")[..8],
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var execution = new WfNodeExecution
        {
            ExecutionKey = Guid.NewGuid().ToString("N"),
            ScopeKey = WfIdentityHash.NormalizeScopeKey(null),
            InstanceId = instance.Id,
            TokenId = 92002L,
            NodeVisitId = 1L,
            NodeId = "node-http-outbox",
            NodeType = WfNodeType.Webhook,
            DefinitionVersionId = 1L,
            MaxAttempts = 3,
        };
        await db.Insertable(execution).ExecuteCommandAsync();
        return await WfOutboxStore.EnqueueAsync(
            db,
            execution,
            WfOutboxStore.MessageTypeNodeExecutionCompleted,
            "{\"source\":\"http-transport-gate\"}",
            DateTime.UtcNow.AddSeconds(-2),
            CancellationToken.None);
    }

    private static Task<WfOutboxStatus> StatusAsync(ISqlSugarClient db, long id) =>
        db.Queryable<WfOutbox>().Where(o => o.Id == id).Select(o => o.Status).FirstAsync();

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    /// <summary>测试专用 HTTP transport:POST JSON 到本地 endpoint,按状态码映射投递结果。</summary>
    private sealed class HttpWfOutboxTransport(Uri baseUri) : IWfOutboxTransport
    {
        private readonly HttpClient _http = new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) };

        public async Task<WfOutboxDispatchResult> DispatchAsync(
            WfOutboxMessage message,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            using var response = await _http.PostAsJsonAsync(
                "/outbox/deliver",
                new
                {
                    message.Id,
                    message.ExecutionId,
                    message.MessageType,
                    message.MessageKey,
                    message.PayloadJson,
                    message.AttemptCount,
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
                return WfOutboxDispatchResult.Succeeded("http-ok");

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return WfOutboxDispatchResult.TerminalFailure(
                    WorkflowErrorCode.OutboxTransportUnhandled,
                    string.IsNullOrWhiteSpace(body) ? "http-503" : body);
            }

            return WfOutboxDispatchResult.RetryableFailure(
                WorkflowErrorCode.OutboxTransportUnhandled,
                $"http-{(int)response.StatusCode}");
        }
    }

    /// <summary>
    /// 本地 HTTP 消费者:按 <c>MessageKey</c> 幂等推进业务计数,证明重复投递不会重复生效。
    /// </summary>
    private sealed class LocalHttpOutboxEndpoint : IAsyncDisposable
    {
        // Start 失败后同一 HttpListener 实例会进入不可用状态;每次尝试新建。
        private HttpListener? _listener;
        private readonly ConcurrentDictionary<string, int> _applied = new(StringComparer.Ordinal);
        private readonly ConcurrentBag<WfOutboxStatus> _seenStatuses = [];
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private int _deliveryCount;
        private int _failNext;

        public Uri BaseUri { get; private set; } = null!;

        public int DeliveryCount => Volatile.Read(ref _deliveryCount);

        public int FailNext
        {
            set => Volatile.Write(ref _failNext, value);
        }

        public IReadOnlyCollection<WfOutboxStatus> SeenStatuses => _seenStatuses;

        public int AppliedCount(string messageKey) =>
            _applied.TryGetValue(messageKey, out var count) ? count : 0;

        public static async Task<LocalHttpOutboxEndpoint> StartAsync()
        {
            var endpoint = new LocalHttpOutboxEndpoint();
            Exception? last = null;
            // 按进程错开端口段,避免本机多片并行时抢同一 Prefixe。
            var basePort = 18_800 + (Environment.ProcessId % 40) * 20;
            for (var offset = 0; offset < 20; offset++)
            {
                var port = basePort + offset;
                HttpListener? candidate = null;
                try
                {
                    candidate = new HttpListener();
                    endpoint.BaseUri = new Uri($"http://127.0.0.1:{port}/");
                    candidate.Prefixes.Add(endpoint.BaseUri.ToString());
                    candidate.Start();
                    endpoint._listener = candidate;
                    endpoint._loop = Task.Run(() => endpoint.ServeAsync(endpoint._cts.Token));
                    await Task.Yield();
                    return endpoint;
                }
                catch (Exception ex)
                {
                    last = ex;
                    candidate?.Close();
                }
            }

            throw new InvalidOperationException("无法绑定本地 HTTP outbox endpoint。", last);
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            var listener = _listener ?? throw new ObjectDisposedException(nameof(LocalHttpOutboxEndpoint));
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
                {
                    break;
                }

                if (context is null) continue;
                _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST" ||
                    !string.Equals(context.Request.Url?.AbsolutePath, "/outbox/deliver", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    return;
                }

                Interlocked.Increment(ref _deliveryCount);
                _seenStatuses.Add(WfOutboxStatus.Dispatching);

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var messageKey = doc.RootElement.TryGetProperty("messageKey", out var keyEl)
                    ? keyEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(messageKey))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    return;
                }

                if (Interlocked.Decrement(ref _failNext) >= 0)
                {
                    var bytes = Encoding.UTF8.GetBytes("forced-fail");
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                    return;
                }

                _applied.AddOrUpdate(messageKey, 1, static (_, current) => current);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.Close();
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch
                {
                    // listener 正在关闭时忽略。
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_listener is { IsListening: true } listening)
                listening.Stop();
            _listener?.Close();
            _listener = null;
            if (_loop is not null)
            {
                try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { /* 关闭时忽略 */ }
            }

            _cts.Dispose();
        }
    }
}
