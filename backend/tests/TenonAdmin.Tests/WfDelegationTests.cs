using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T15 长期委托：一跳解析、有效期/启停、规则审计、循环和机构隔离。</summary>
public class WfDelegationTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Active_rule_resolves_one_hop_and_snapshots_original_owner()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var starterId = await AddUser(admin, "wf-delegation-starter");
        var originalId = await AddUser(admin, "wf-delegation-original");
        var delegateId = await AddUser(admin, "wf-delegation-delegate");
        var chainedId = await AddUser(admin, "wf-delegation-chained");
        var definitionId = await Publish(admin, "长期委托一跳", Model(originalId));
        var starter = await ClientFor(f, "wf-delegation-starter");
        var delegateClient = await ClientFor(f, "wf-delegation-delegate");

        var startsAt = DateTime.UtcNow.AddMinutes(-5);
        var endsAt = DateTime.UtcNow.AddHours(1);
        var added = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = originalId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt,
            endsAt,
            requestId = "wf-delegation-add-a",
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var ruleId = added.GetProperty("data").GetProperty("id").GetInt64();

        var replay = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = originalId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt,
            endsAt,
            requestId = "wf-delegation-add-a",
        });
        Assert.Equal(0, replay.GetProperty("code").GetInt32());
        Assert.Equal(ruleId, replay.GetProperty("data").GetProperty("id").GetInt64());

        var chain = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = delegateId,
            delegateUserId = chainedId,
            enabled = true,
            startsAt,
            endsAt,
            requestId = "wf-delegation-add-b",
        });
        Assert.Equal(0, chain.GetProperty("code").GetInt32());

        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "wf-delegation-start-1" });
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var actors = await db.Queryable<WfTaskActor>().Where(a => a.TaskId == taskId).ToListAsync();
            var actor = Assert.Single(actors);
            Assert.Equal(delegateId, actor!.UserId);
            Assert.Equal(originalId, actor.OriginalUserId);
            Assert.Equal(ruleId, actor.DelegationRuleId);
        }

        var approved = await PostEnvelope(delegateClient, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal((int)WfInstanceStatus.Approved, approved.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var history = await db.Queryable<WfHisTask>().Where(h => h.TaskId == taskId && h.Action == WfTaskAction.Approve).FirstAsync();
            Assert.Equal(delegateId, history!.UserId);
            Assert.Equal(originalId, history.OriginalUserId);
            Assert.Equal(ruleId, history.DelegationRuleId);
            Assert.Equal(1, await db.Queryable<WfDelegationRuleHistory>().Where(h => h.RuleId == ruleId).CountAsync());
        }

        var disabled = await PutEnvelope(admin, $"/api/v1/workflow/delegation/{ruleId}", new
        {
            originalUserId = originalId,
            delegateUserId = delegateId,
            enabled = false,
            startsAt,
            endsAt,
            requestId = "wf-delegation-disable-a",
        });
        Assert.Equal(0, disabled.GetProperty("code").GetInt32());

        var disabledReplay = await PutEnvelope(admin, $"/api/v1/workflow/delegation/{ruleId}", new
        {
            originalUserId = originalId,
            delegateUserId = delegateId,
            enabled = false,
            startsAt,
            endsAt,
            requestId = "wf-delegation-disable-a",
        });
        Assert.Equal(0, disabledReplay.GetProperty("code").GetInt32());

        var second = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "wf-delegation-start-2" });
        var secondTaskId = second.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        using var finalScope = f.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var fallback = await finalDb.Queryable<WfTaskActor>().Where(a => a.TaskId == secondTaskId).FirstAsync();
        Assert.Equal(originalId, fallback!.UserId);
        Assert.Null(fallback.DelegationRuleId);

        var deleted = await (await admin.DeleteAsync($"/api/v1/workflow/delegation/{ruleId}?requestId=wf-delegation-delete-a")).ReadEnvelope();
        Assert.Equal(0, deleted.GetProperty("code").GetInt32());
        var deletedReplay = await (await admin.DeleteAsync($"/api/v1/workflow/delegation/{ruleId}?requestId=wf-delegation-delete-a")).ReadEnvelope();
        Assert.Equal(0, deletedReplay.GetProperty("code").GetInt32());

        var reenabled = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = originalId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt,
            endsAt,
            requestId = "wf-delegation-reenable-a",
        });
        Assert.Equal(0, reenabled.GetProperty("code").GetInt32());
        Assert.Equal(ruleId, reenabled.GetProperty("data").GetProperty("id").GetInt64());
        var ruleHistory = await finalDb.Queryable<WfDelegationRuleHistory>()
            .Where(h => h.RuleId == ruleId)
            .OrderBy(h => h.Id)
            .ToListAsync();
        Assert.Equal(4, ruleHistory.Count);
        Assert.Equal(WfDelegationRuleChangeType.Enabled, ruleHistory[^1].ChangeType);
        _ = starterId;
    }

    [Fact]
    public async Task Multiple_originals_delegating_to_one_user_create_one_effective_actor()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var starterId = await AddUser(admin, "wf-delegation-dedup-starter");
        var firstOwnerId = await AddUser(admin, "wf-delegation-dedup-first");
        var secondOwnerId = await AddUser(admin, "wf-delegation-dedup-second");
        var delegateId = await AddUser(admin, "wf-delegation-dedup-target");
        var definitionId = await Publish(admin, "委托实际办理人去重", Model(firstOwnerId, secondOwnerId));

        var window = new { startsAt = DateTime.UtcNow.AddMinutes(-1), endsAt = DateTime.UtcNow.AddHours(1) };
        long firstRuleId = 0;
        foreach (var (ownerId, requestId) in new[]
        {
            (firstOwnerId, "wf-delegation-dedup-first-rule"),
            (secondOwnerId, "wf-delegation-dedup-second-rule"),
        })
        {
            var added = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
            {
                originalUserId = ownerId,
                delegateUserId = delegateId,
                enabled = true,
                window.startsAt,
                window.endsAt,
                requestId,
            });
            Assert.Equal(0, added.GetProperty("code").GetInt32());
            if (ownerId == firstOwnerId)
                firstRuleId = added.GetProperty("data").GetProperty("id").GetInt64();
        }

        var starter = await ClientFor(f, "wf-delegation-dedup-starter");
        var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new
        {
            definitionId,
            requestId = "wf-delegation-dedup-start",
        });
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var actor = Assert.Single(await db.Queryable<WfTaskActor>().Where(a => a.TaskId == taskId).ToListAsync());
        Assert.Equal(delegateId, actor.UserId);
        Assert.Equal(firstOwnerId, actor.OriginalUserId);
        Assert.Equal(firstRuleId, actor.DelegationRuleId);
        _ = starterId;
    }

    [Fact]
    public async Task Delegation_rejects_cycles_and_cross_organization_targets()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var aId = await AddUser(admin, "wf-delegation-cycle-a");
        var bId = await AddUser(admin, "wf-delegation-cycle-b");
        var otherOrgId = await AddUser(admin, "wf-delegation-other-org", orgId: 2);
        var window = new { startsAt = DateTime.UtcNow.AddMinutes(-1), endsAt = DateTime.UtcNow.AddHours(1), enabled = true };

        var first = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = aId, delegateUserId = bId, startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
            requestId = "wf-delegation-cycle-1",
        });
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        var cycle = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = bId, delegateUserId = aId, startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
            requestId = "wf-delegation-cycle-2",
        });
        Assert.Equal(WorkflowErrorCode.DelegationCycle, cycle.GetProperty("code").GetInt32());

        var concurrentA = await AddUser(admin, "wf-delegation-concurrent-a");
        var concurrentB = await AddUser(admin, "wf-delegation-concurrent-b");
        var concurrentResults = await Task.WhenAll(
            PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
            {
                originalUserId = concurrentA, delegateUserId = concurrentB,
                startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
                requestId = "wf-delegation-concurrent-ab",
            }),
            PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
            {
                originalUserId = concurrentB, delegateUserId = concurrentA,
                startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
                requestId = "wf-delegation-concurrent-ba",
            }));
        Assert.Single(concurrentResults, result => result.GetProperty("code").GetInt32() == 0);
        Assert.Single(concurrentResults, result =>
            result.GetProperty("code").GetInt32() == WorkflowErrorCode.DelegationCycle);

        var scope = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = aId, delegateUserId = otherOrgId, startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
            requestId = "wf-delegation-scope",
        });
        Assert.Equal(WorkflowErrorCode.DelegationScopeDenied, scope.GetProperty("code").GetInt32());

        var ownerId = await AddUser(admin, "wf-delegation-owner");
        var ownerClient = await ClientFor(f, "wf-delegation-owner");
        var ownerCannotManageOther = await PostEnvelope(ownerClient, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = otherOrgId,
            delegateUserId = ownerId,
            startsAt = window.startsAt,
            endsAt = window.endsAt,
            enabled = true,
            requestId = "wf-delegation-owner-scope",
        });
        Assert.Equal(WorkflowErrorCode.DelegationScopeDenied, ownerCannotManageOther.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task MySql_reverse_rules_are_checked_after_both_transactions_reach_scope_lock()
    {
        var barrier = new DelegationLockBarrier();
        using var f = new WorkflowAppFactory
        {
            Overrides = services =>
            {
                services.AddSingleton(barrier);
                services.AddScoped<IWfDelegationService, BarrierDelegationService>();
            },
        };
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var aId = await AddUser(admin, "wf-delegation-barrier-a");
        var bId = await AddUser(admin, "wf-delegation-barrier-b");
        var window = new { startsAt = DateTime.UtcNow.AddMinutes(-1), endsAt = DateTime.UtcNow.AddHours(1), enabled = true };

        barrier.Arm();
        var results = await Task.WhenAll(
            PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
            {
                originalUserId = aId, delegateUserId = bId,
                startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
                requestId = "wf-delegation-barrier-ab",
            }),
            PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
            {
                originalUserId = bId, delegateUserId = aId,
                startsAt = window.startsAt, endsAt = window.endsAt, enabled = window.enabled,
                requestId = "wf-delegation-barrier-ba",
            }));

        Assert.Single(results, result => result.GetProperty("code").GetInt32() == 0);
        Assert.Single(results, result => result.GetProperty("code").GetInt32() == WorkflowErrorCode.DelegationCycle);
    }

    [Fact]
    public async Task Same_org_manager_can_manage_other_owners_and_replays_before_current_rule_validation()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var managerId = await AddUser(admin, "wf-delegation-manager");
        var ownerId = await AddUser(admin, "wf-delegation-managed-owner");
        var delegateId = await AddUser(admin, "wf-delegation-managed-target");
        var manager = await ClientFor(f, "wf-delegation-manager");
        var window = new { startsAt = DateTime.UtcNow.AddMinutes(-1), endsAt = DateTime.UtcNow.AddHours(1) };

        var added = await PostEnvelope(manager, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = ownerId, delegateUserId = delegateId, enabled = true,
            window.startsAt, window.endsAt, requestId = "wf-delegation-manager-add",
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var ruleId = added.GetProperty("data").GetProperty("id").GetInt64();

        var changed = await PutEnvelope(manager, $"/api/v1/workflow/delegation/{ruleId}", new
        {
            originalUserId = ownerId, delegateUserId = delegateId, enabled = false,
            window.startsAt, window.endsAt, requestId = "wf-delegation-manager-update",
        });
        Assert.Equal(0, changed.GetProperty("code").GetInt32());

        var deleted = await (await manager.DeleteAsync($"/api/v1/workflow/delegation/{ruleId}?requestId=wf-delegation-manager-delete")).ReadEnvelope();
        Assert.Equal(0, deleted.GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            await db.Updateable<SysUser>().SetColumns(u => new SysUser { OrgId = 2 }).Where(u => u.Id == ownerId).ExecuteCommandAsync();
        }

        var addReplay = await PostEnvelope(manager, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = ownerId, delegateUserId = delegateId, enabled = true,
            window.startsAt, window.endsAt, requestId = "wf-delegation-manager-add",
        });
        Assert.Equal(0, addReplay.GetProperty("code").GetInt32());
        Assert.Equal(ruleId, addReplay.GetProperty("data").GetProperty("id").GetInt64());

        var updateReplay = await PutEnvelope(manager, $"/api/v1/workflow/delegation/{ruleId}", new
        {
            originalUserId = ownerId, delegateUserId = delegateId, enabled = false,
            window.startsAt, window.endsAt, requestId = "wf-delegation-manager-update",
        });
        Assert.Equal(0, updateReplay.GetProperty("code").GetInt32());

        var deleteReplay = await (await manager.DeleteAsync($"/api/v1/workflow/delegation/{ruleId}?requestId=wf-delegation-manager-delete")).ReadEnvelope();
        Assert.Equal(0, deleteReplay.GetProperty("code").GetInt32());

        var noOrgId = await AddUser(admin, "wf-delegation-no-org", orgId: null);
        var noOrg = await ClientFor(f, "wf-delegation-no-org");
        var denied = await PostEnvelope(noOrg, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = noOrgId, delegateUserId = delegateId, enabled = true,
            window.startsAt, window.endsAt, requestId = "wf-delegation-no-org",
        });
        Assert.Equal(WorkflowErrorCode.DelegationScopeDenied, denied.GetProperty("code").GetInt32());
        _ = managerId;
    }

    [Fact]
    public async Task Notification_failure_does_not_rollback_committed_rule()
    {
        using var f = new WorkflowAppFactory
        {
            Overrides = services => services.Insert(
                0,
                ServiceDescriptor.Scoped<IWfDelegationNotifier, ThrowingDelegationNotifier>()),
        };
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var ownerId = await AddUser(admin, "wf-delegation-notify-owner");
        var delegateId = await AddUser(admin, "wf-delegation-notify-target");
        var added = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", new
        {
            originalUserId = ownerId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt = DateTime.UtcNow.AddMinutes(-1),
            endsAt = DateTime.UtcNow.AddHours(1),
            requestId = "wf-delegation-notify-failure",
        });

        Assert.Equal(0, added.GetProperty("code").GetInt32());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        Assert.True(await db.Queryable<WfDelegationRule>()
            .AnyAsync(rule => rule.OriginalUserId == ownerId && rule.DelegateUserId == delegateId));
    }

    [Fact]
    public async Task Corrupt_replayed_delegation_receipt_returns_operation_error()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await GrantPermissions(f);
        var ownerId = await AddUser(admin, "wf-delegation-corrupt-owner");
        var delegateId = await AddUser(admin, "wf-delegation-corrupt-target");
        var requestId = "wf-delegation-corrupt-receipt";
        var input = new
        {
            originalUserId = ownerId,
            delegateUserId = delegateId,
            enabled = true,
            startsAt = DateTime.UtcNow.AddMinutes(-1),
            endsAt = DateTime.UtcNow.AddHours(1),
            requestId,
        };
        Assert.Equal(0, (await PostEnvelope(admin, "/api/v1/workflow/delegation/add", input))
            .GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
            var receipt = await db.Queryable<WfOperationReceipt>()
                .Where(r => r.CommandType == WfCommandType.DelegationRuleAdd && r.RequestKey == requestId)
                .FirstAsync();
            Assert.NotNull(receipt);
            Assert.Equal(1, await db.Updateable<WfOperationReceipt>()
                .SetColumns(r => new WfOperationReceipt { ResultJson = "{" })
                .Where(r => r.Id == receipt!.Id)
                .ExecuteCommandAsync());
        }

        var replay = await PostEnvelope(admin, "/api/v1/workflow/delegation/add", input);
        Assert.Equal(WorkflowErrorCode.OperationFailed, replay.GetProperty("code").GetInt32());
        Assert.Equal("receiptResultInvalid", replay.GetProperty("args").GetProperty("reason").GetString());
    }

    private static object Model(params long[] userIds) => new
    {
        version = 1,
        root = new
        {
            id = "start", type = "start", name = "发起",
            next = new
            {
                id = "approval", type = "approval", name = "审批",
                props = new { assignee = new { provider = "user", @params = new Dictionary<string, object> { ["userIds"] = userIds } } },
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

    private static async Task<long> AddUser(HttpClient admin, string account, long? orgId = 1)
    {
        var env = await PostEnvelope(admin, "/api/v1/sys/user", new
        {
            account, password = Password, name = account, enabled = true, orgId, roleIds = new[] { 2L },
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

    private static async Task GrantPermissions(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var permissions = new[]
        {
            "GET:/api/v1/workflow/delegation/page",
            "POST:/api/v1/workflow/delegation/add",
            "PUT:/api/v1/workflow/delegation/{id}",
            "DELETE:/api/v1/workflow/delegation/{id}",
        };
        var menuIds = await db.Queryable<SysMenu>().Where(m => permissions.Contains(m.Permission)).Select(m => m.Id).ToListAsync();
        foreach (var menuId in menuIds)
            await db.Insertable(new SysRoleMenu { RoleId = 2, MenuId = menuId }).ExecuteCommandAsync();
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private static async Task<JsonElement> PutEnvelope(HttpClient client, string path, object body) =>
        await (await client.PutJson(path, body)).ReadEnvelope();

    private sealed class DelegationLockBarrier
    {
        private volatile bool armed;
        private int arrivals;
        private TaskCompletionSource both = Completed();

        public void Arm()
        {
            arrivals = 0;
            both = new(TaskCreationOptions.RunContinuationsAsynchronously);
            armed = true;
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            if (!armed) return Task.CompletedTask;
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                armed = false;
                both.TrySetResult();
            }
            return both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        private static TaskCompletionSource Completed()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }
    }

    private sealed class BarrierDelegationService(
        IRepository<WfDelegationRule> rules,
        IRepository<WfDelegationRuleHistory> history,
        IRepository<SysUser> users,
        IWfOperationReceiptService receipts,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IWfDelegationNotifier notifier,
        DelegationLockBarrier barrier)
        : WfDelegationService(rules, history, users, receipts, currentUser, timeProvider, notifier)
    {
        protected override async Task LockScopeAsync(long scope, CancellationToken cancellationToken)
        {
            if (TestDb.UseMySql)
                await barrier.WaitAsync(cancellationToken);
            await base.LockScopeAsync(scope, cancellationToken);
        }
    }

    private sealed class ThrowingDelegationNotifier : IWfDelegationNotifier
    {
        public Task RuleChangedAsync(
            WfDelegationRuleOutput rule,
            WfDelegationRuleChangeType changeType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("notification test failure");
    }
}
