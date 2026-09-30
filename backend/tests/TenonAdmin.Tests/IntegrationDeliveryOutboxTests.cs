extern alias integrationhost;

using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using DemoPartnerDeliveryAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerDeliveryAdapter;
using DemoPartnerNoDedupeAdapter = integrationhost::TenonAdmin.IntegrationTestHost.DemoPartnerNoDedupeAdapter;
using DemoTicket = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicket;
using DemoTicketErrors = integrationhost::TenonAdmin.IntegrationTestHost.DemoTicketErrors;
using IDemoTicketService = integrationhost::TenonAdmin.IntegrationTestHost.IDemoTicketService;
using static TenonAdmin.Tests.IntegrationTestSupport;

namespace TenonAdmin.Tests;

/// <summary>
/// G08 事务内投递记录:真实数据库上证明业务写入与投递记录同事务提交/回滚、重启后仍在、重复标识按契约处理,
/// 以及无事务、非法请求与并发同键的边界。所有断言都在库里核对,不借助假仓储。
/// </summary>
public class IntegrationDeliveryOutboxTests
{
    private static DeliveryRequest Request(string? key, object? payload = null, string adapter = DemoPartnerDeliveryAdapter.AdapterName, string operation = "ticket.create") =>
        new() { Adapter = adapter, Operation = operation, DeliveryKey = key, Payload = payload ?? new { title = "t", amount = 1 } };

    private static Task<List<IntegrationDelivery>> DeliveriesAsync(IntegrationAppFactory f) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<IntegrationDelivery>().OrderBy(d => d.Id).ToListAsync());

    private static Task<List<DemoTicket>> TicketsAsync(IntegrationAppFactory f) =>
        InScopeAsync(f.Services, sp => sp.GetRequiredService<ISqlSugarClient>().Queryable<DemoTicket>().ToListAsync());

    [Fact]
    public async Task A_committed_business_write_and_its_delivery_persist_together_across_a_restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-itg-outbox-{Guid.NewGuid():N}.db");
        try
        {
            long ticketId;
            var before = DateTime.UtcNow.AddSeconds(-2);
            using (var hostA = new IntegrationAppFactory { DbPath = dbPath, WorkerId = 31, DeleteDbOnDispose = false })
            {
                ticketId = await InScopeAsync(hostA.Services, sp => sp.GetRequiredService<IDemoTicketService>().CreateAndSyncAsync("同步工单", "p-1", 88.5m));
            }

            // 「重启」:新进程级宿主连同一个库
            using var hostB = new IntegrationAppFactory { DbPath = dbPath, ResetDatabase = false, WorkerId = 32, DeleteDbOnDispose = false };
            var ticket = Assert.Single(await TicketsAsync(hostB));
            Assert.Equal(ticketId, ticket.Id);
            var delivery = Assert.Single(await DeliveriesAsync(hostB));
            Assert.Equal($"demo-ticket:{ticketId}", delivery.DeliveryKey);
            Assert.Equal(DemoPartnerDeliveryAdapter.AdapterName, delivery.Adapter);
            Assert.Equal("ticket.create", delivery.Operation);
            Assert.Equal(ticketId.ToString(), delivery.BusinessKey);
            Assert.Equal("{\"title\":\"同步工单\",\"amount\":88.5}", delivery.PayloadJson);
            Assert.Equal(64, delivery.PayloadHash.Length);
            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
            Assert.Equal(0, delivery.AttemptCount);
            Assert.Equal(0, delivery.Fence);
            Assert.Equal(8, delivery.MaxAttempts);
            Assert.InRange(delivery.NextAttemptAtUtc, before, DateTime.UtcNow.AddSeconds(2));
            Assert.Equal(TimeSpan.FromHours(24), delivery.DeadlineAtUtc - delivery.NextAttemptAtUtc);
            Assert.Null(delivery.CompletedAtUtc);
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    [Fact]
    public async Task A_rolled_back_transaction_keeps_neither_the_business_row_nor_the_delivery()
    {
        using var f = new IntegrationAppFactory();

        // 消费者服务:入队后业务校验失败 → 整个事务回滚
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            InScopeAsync(f.Services, sp => sp.GetRequiredService<IDemoTicketService>().CreateAndSyncAsync("回滚工单", "p-1", 1m, failAfterEnqueue: true)));
        Assert.Equal(DemoTicketErrors.ValidationFailed, (int)ex.Code);

        // 直接用客户端事务:业务插入 + 入队后抛出
        var result = await InScopeAsync(f.Services, async sp =>
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var outbox = sp.GetRequiredService<IDeliveryOutbox>();
            return await db.Ado.UseTranAsync(async () =>
            {
                await db.Insertable(new DemoTicket { Title = "直接回滚", PartnerCode = "p-2", Amount = 2m }).ExecuteCommandAsync();
                await outbox.EnqueueAsync(Request("rollback-key"));
                throw new InvalidOperationException("模拟业务失败");
            });
        });
        Assert.False(result.IsSuccess);

        Assert.Empty(await TicketsAsync(f));
        Assert.Empty(await DeliveriesAsync(f));
    }

    [Fact]
    public async Task Enqueue_without_a_transaction_is_refused_unless_explicitly_standalone()
    {
        using var f = new IntegrationAppFactory();
        await ExpectCodeAsync(IntegrationErrorCode.DeliveryRequiresTransaction,
            () => InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(Request("no-tran"))));
        Assert.Empty(await DeliveriesAsync(f));

        var standalone = await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(Request("standalone") with { Standalone = true }));
        Assert.True(standalone.Created);
        Assert.Equal("standalone", Assert.Single(await DeliveriesAsync(f)).DeliveryKey);
    }

    [Fact]
    public async Task Repeated_keys_are_idempotent_and_conflicting_reuse_is_refused()
    {
        using var f = new IntegrationAppFactory();
        Task<DeliveryEnqueueResult> Enqueue(DeliveryRequest request) =>
            InScopeAsync(f.Services, async sp =>
            {
                var db = sp.GetRequiredService<ISqlSugarClient>();
                DeliveryEnqueueResult? result = null;
                var tran = await db.Ado.UseTranAsync(async () => result = await sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(request));
                if (!tran.IsSuccess) throw tran.ErrorException;
                return result!;
            });

        var first = await Enqueue(Request("order-1", new { title = "x", amount = 3, tags = new[] { "a", "b" } }));
        Assert.True(first.Created);
        Assert.Equal("order-1", first.DeliveryKey);

        // 同键同内容(原始 JSON 的空白与转义写法不同也算同一内容)→ 返回既有记录
        var again = await Enqueue(new DeliveryRequest
        {
            Adapter = "DEMO-PARTNER", Operation = "ticket.create", DeliveryKey = " order-1 ",
            PayloadJson = "{ \"title\" : \"x\", \"amount\": 3, \"tags\": [\"a\", \"\\u0062\"] }",
        });
        Assert.False(again.Created);
        Assert.Equal(first.Id, again.Id);

        await ExpectCodeAsync(IntegrationErrorCode.DeliveryKeyConflict, () => Enqueue(Request("order-1", new { title = "x", amount = 4 })));
        await ExpectCodeAsync(IntegrationErrorCode.DeliveryKeyConflict, () => Enqueue(Request("order-1", adapter: DemoPartnerNoDedupeAdapter.AdapterName)));
        var conflict = await ExpectCodeAsync(IntegrationErrorCode.DeliveryKeyConflict, () => Enqueue(Request("order-1", operation: "ticket.update")));
        Assert.Equal("order-1", conflict.Args!["deliveryKey"]);

        // 未给标识:各自生成 dlv_{雪花号}
        var generated1 = await Enqueue(Request(null));
        var generated2 = await Enqueue(Request(null));
        Assert.Equal($"dlv_{generated1.Id}", generated1.DeliveryKey);
        Assert.NotEqual(generated1.DeliveryKey, generated2.DeliveryKey);

        Assert.Equal(3, (await DeliveriesAsync(f)).Count);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_before_anything_is_written()
    {
        using var f = new IntegrationAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:Integration:Delivery:MaxPayloadBytes"] = "1024" },
            SchemaNeutral = true,
        };
        async Task Invalid(string field, DeliveryRequest request)
        {
            var ex = await ExpectCodeAsync(IntegrationErrorCode.DeliveryRequestInvalid,
                () => InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(request with { Standalone = true })));
            Assert.Equal(field, ex.Args!["field"]);
        }

        var unknown = await ExpectCodeAsync(IntegrationErrorCode.DeliveryAdapterUnknown,
            () => InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(Request("k", adapter: "nobody") with { Standalone = true })));
        Assert.Equal("nobody", unknown.Args!["adapter"]);
        await Invalid("operation", Request("k", operation: "bad op"));
        await Invalid("operation", Request("k", operation: new string('o', 65)));
        await Invalid("deliveryKey", Request("has space"));
        await Invalid("deliveryKey", Request(new string('k', 129)));
        await Invalid("deliveryKey", Request("中文键"));
        await Invalid("businessKey", Request("k") with { BusinessKey = new string('b', 129) });
        await Invalid("maxAttempts", Request("k") with { MaxAttempts = 0 });
        await Invalid("maxAttempts", Request("k") with { MaxAttempts = 101 });
        await Invalid("maxAge", Request("k") with { MaxAge = TimeSpan.FromSeconds(10) });
        await Invalid("maxAge", Request("k") with { MaxAge = TimeSpan.FromDays(31) });
        await Invalid("payload", Request("k") with { PayloadJson = "{}" });   // 同时给了对象与原始 JSON
        await Invalid("payload", new DeliveryRequest { Adapter = DemoPartnerDeliveryAdapter.AdapterName, Operation = "op", DeliveryKey = "k", PayloadJson = "{not json" });
        await Invalid("payload", Request("k", new { text = new string('x', 2000) }));

        Assert.Empty(await DeliveriesAsync(f));

        // 覆盖项生效:次数上限、时限与延迟
        var notBefore = DateTimeOffset.UtcNow.AddHours(2);
        await InScopeAsync(f.Services, sp => sp.GetRequiredService<IDeliveryOutbox>().EnqueueAsync(Request("custom") with
        {
            Standalone = true, MaxAttempts = 3, MaxAge = TimeSpan.FromHours(1), NotBefore = notBefore,
        }));
        var custom = Assert.Single(await DeliveriesAsync(f));
        Assert.Equal(3, custom.MaxAttempts);
        Assert.InRange(custom.NextAttemptAtUtc, notBefore.UtcDateTime.AddSeconds(-1), notBefore.UtcDateTime.AddSeconds(1));
        Assert.Equal(TimeSpan.FromHours(1), custom.DeadlineAtUtc - custom.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Concurrent_enqueues_of_one_key_leave_exactly_one_record()
    {
        using var f = new IntegrationAppFactory();
        var firstInserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<object> Attempt(bool first)
        {
            if (!first) await firstInserted.Task;
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var outbox = scope.ServiceProvider.GetRequiredService<IDeliveryOutbox>();
            DeliveryEnqueueResult? result = null;
            var tran = await db.Ado.UseTranAsync(async () =>
            {
                result = await outbox.EnqueueAsync(Request("race-key"));
                if (first)
                {
                    firstInserted.SetResult();
                    await Task.Delay(500);   // 未提交期间让另一事务撞上同一个键
                }
            });
            return tran.IsSuccess ? result! : tran.ErrorException;
        }

        var outcomes = await Task.WhenAll(Attempt(first: true), Attempt(first: false));
        var winner = Assert.IsType<DeliveryEnqueueResult>(outcomes[0]);
        Assert.True(winner.Created);
        // 后到者:要么在对方提交后读到既有记录,要么撞唯一索引以异常回滚——绝不产生第二行
        if (outcomes[1] is DeliveryEnqueueResult late)
        {
            Assert.False(late.Created);
            Assert.Equal(winner.Id, late.Id);
        }
        else
        {
            Assert.IsAssignableFrom<Exception>(outcomes[1]);
        }
        Assert.Single(await DeliveriesAsync(f), d => d.DeliveryKey == "race-key");
    }
}
