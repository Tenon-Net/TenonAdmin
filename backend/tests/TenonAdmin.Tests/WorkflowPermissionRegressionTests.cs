using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>工作流约束路由、菜单种子与普通用户授权必须使用同一权限码。</summary>
public class WorkflowPermissionRegressionTests
{
    private const string Password = "Workflow@123456";
    private const string Version070 = "8";
    private static readonly long[] DefinitionMenus = [48_000, 48_001, 48_023, 48_002, 48_003, 48_004, 48_005, 48_006, 48_007];

    [Fact]
    public async Task Seeded_definition_permissions_support_the_real_lifecycle_without_weakening_security()
    {
        using var factory = new WorkflowAppFactory();
        var author = await CreateUser(factory, "wf-author", DefinitionMenus);
        var denied = await CreateUser(factory, "wf-no-read", DefinitionMenus.Except([48_003L, 48_004L]).ToArray());
        var approver = await CreateUser(factory, "wf-approver", []);
        var stranger = await CreateUser(factory, "wf-stranger", []);

        var authorClient = await ClientFor(factory, author.Account);
        Assert.False((await GetOk(authorClient, "/api/v1/personal/profile"))
            .GetProperty("isSuperAdmin").GetBoolean());
        await GetOk(authorClient, "/api/v1/workflow/definition/page");
        var definitionId = await AddDefinition(authorClient, "约束路由回归", approver.UserId);

        var detail = await GetOk(authorClient, $"/api/v1/workflow/definition/{definitionId}");
        Assert.Equal("约束路由回归", detail.GetProperty("name").GetString());

        var updated = await (await authorClient.PostJson("/api/v1/workflow/definition/update", new
        {
            id = definitionId,
            name = "约束路由回归-已修改",
            groupName = "回归",
            model = UserApprovalModel(approver.UserId),
        })).ReadEnvelope();
        Assert.Equal(0, updated.GetProperty("code").GetInt32());

        var published = await (await authorClient.PostJson(
            "/api/v1/workflow/definition/publish", new { id = definitionId })).ReadEnvelope();
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        Assert.Equal(1, published.GetProperty("data").GetInt32());

        var versions = await GetOk(authorClient, $"/api/v1/workflow/definition/versions/{definitionId}");
        Assert.Single(versions.EnumerateArray());

        var deniedClient = await ClientFor(factory, denied.Account);
        await AssertForbidden(deniedClient, $"/api/v1/workflow/definition/{definitionId}");
        await AssertForbidden(deniedClient, $"/api/v1/workflow/definition/versions/{definitionId}");

        Assert.Equal(HttpStatusCode.NotFound,
            (await authorClient.GetAsync("/api/v1/workflow/definition/not-long")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await authorClient.GetAsync("/api/v1/workflow/definition/9223372036854775808")).StatusCode);

        var started = await (await authorClient.PostJson("/api/v1/workflow/instance/start", new
        {
            definitionId,
            businessKey = "WF-PERMISSION-REGRESSION",
        })).ReadEnvelope();
        Assert.Equal(0, started.GetProperty("code").GetInt32());
        var taskId = started.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var strangerClient = await ClientFor(factory, stranger.Account);
        var stolen = await (await strangerClient.PostJson(
            "/api/v1/workflow/task/approve", new { taskId, comment = "越权办理" })).ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.TaskConflict, stolen.GetProperty("code").GetInt32());

        var approved = await (await (await ClientFor(factory, approver.Account)).PostJson(
            "/api/v1/workflow/task/approve", new { taskId, comment = "正常办理" })).ReadEnvelope();
        Assert.Equal(0, approved.GetProperty("code").GetInt32());

        Assert.Equal(HttpStatusCode.OK,
            (await authorClient.PostJson("/api/v1/auth/logout", new { })).StatusCode);
        var afterLogout = await authorClient.GetAsync("/api/v1/workflow/definition/page?current=1&size=10");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        Assert.Equal(40006, (await afterLogout.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Seeded_constraint_codes_match_the_real_role_permission_route_catalog()
    {
        using var factory = new WorkflowAppFactory();
        var admin = await ClientFor(factory, "superAdmin", "Test@123456");
        var envelope = await (await admin.GetAsync("/api/v1/sys/menu/routes")).ReadEnvelope();
        Assert.Equal(0, envelope.GetProperty("code").GetInt32());
        var routeCodes = envelope.GetProperty("data").EnumerateArray()
            .Select(x => x.GetProperty("code").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var seeded = await db.Queryable<SysMenu>()
            .Where(m => m.Id >= 48_000 && m.Id < 49_000 && m.Permission.Contains("{"))
            .Select(m => m.Permission)
            .ToListAsync();

        Assert.Equal(6, seeded.Count);
        Assert.All(seeded, code => Assert.Contains(code, routeCodes));
    }

    [Fact]
    public async Task Other_seeded_long_constraint_permissions_reach_their_actions()
    {
        using var factory = new WorkflowAppFactory();
        var user = await CreateUser(factory, "wf-other-constraints", [48_009, 48_042]);
        var client = await ClientFor(factory, user.Account);

        var delete = await (await client.DeleteAsync("/api/v1/workflow/definition/999999999"))
            .ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.DefinitionNotFound, delete.GetProperty("code").GetInt32());

        var replay = await (await client.PostJson("/api/v1/workflow/outbox/999999999/replay?requestId=permission-regression", new { }))
            .ReadEnvelope();
        Assert.Equal(WorkflowErrorCode.OutboxNotFound, replay.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Existing_version_8_database_and_authorization_survive_restarts_and_a_warm_070_cache()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-wf-permission-upgrade-{Guid.NewGuid():N}.db");
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sharedCache = new MemoryCacheProvider(memory, new AdminCacheOptions());
        var overrides = SharedCache(sharedCache);
        long userId;
        long definitionId;
        string account;

        using (var first = new WorkflowAppFactory
        {
            DbPath = dbPath,
            DeleteDbOnDispose = false,
            Overrides = overrides,
        })
        {
            var user = await CreateUser(first, "wf-upgrade", DefinitionMenus);
            userId = user.UserId;
            account = user.Account;
            var client = await ClientFor(first, user.Account);
            definitionId = await AddDefinition(client, "升级前定义", userId);
            var published = await (await client.PostJson(
                "/api/v1/workflow/definition/publish", new { id = definitionId })).ReadEnvelope();
            Assert.Equal(0, published.GetProperty("code").GetInt32());

            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Updateable<SysSchemaVersion>()
                .SetColumns(x => x.Version == Version070)
                .Where(x => x.Id == 1)
                .ExecuteCommandAsync();
            var oldPermissions = new Dictionary<long, string>
            {
                [48_003] = "GET:/api/v1/workflow/definition/{id}",
                [48_004] = "GET:/api/v1/workflow/definition/versions/{id}",
                [48_009] = "DELETE:/api/v1/workflow/definition/{id}",
                [48_042] = "POST:/api/v1/workflow/outbox/{id}/replay",
            };
            foreach (var (menuId, permission) in oldPermissions)
                await db.Updateable<SysMenu>()
                    .SetColumns(x => x.Permission == permission)
                    .Where(x => x.Id == menuId)
                    .ExecuteCommandAsync();
            Assert.Equal(Version070,
                await db.Queryable<SysSchemaVersion>().Where(x => x.Id == 1).Select(x => x.Version).SingleAsync());

            var oldCodes = await db.Queryable<SysRoleMenu>()
                .InnerJoin<SysUserRole>((rm, ur) => rm.RoleId == ur.RoleId && ur.UserId == userId)
                .InnerJoin<SysMenu>((rm, ur, menu) => rm.MenuId == menu.Id && menu.Permission != "")
                .Select((rm, ur, menu) => menu.Permission)
                .ToListAsync();
            await sharedCache.RemoveAsync(CacheKeys.UserPermissions(userId));
            await sharedCache.SetAsync($"perm:{userId}", oldCodes.Distinct().ToArray());
            Assert.Contains("GET:/api/v1/workflow/definition/{id}",
                (await sharedCache.GetAsync<string[]>($"perm:{userId}"))!);
            Assert.Null(await sharedCache.GetAsync<string[]>(CacheKeys.UserPermissions(userId)));
        }

        using (var second = new WorkflowAppFactory
        {
            DbPath = dbPath,
            ResetDatabase = false,
            DeleteDbOnDispose = false,
            Overrides = overrides,
        })
        {
            var client = await ClientFor(second, account);
            Assert.Equal("升级前定义",
                (await GetOk(client, $"/api/v1/workflow/definition/{definitionId}"))
                .GetProperty("name").GetString());
            Assert.Single((await GetOk(client, $"/api/v1/workflow/definition/versions/{definitionId}"))
                .EnumerateArray());
            await AssertStoredUpgradeState(second, userId, definitionId, expectedTitle: "流程定义-详情");
            var currentCodes = await sharedCache.GetAsync<string[]>(CacheKeys.UserPermissions(userId));
            Assert.Contains("GET:/api/v1/workflow/definition/{id:long}", currentCodes!);
            Assert.DoesNotContain("GET:/api/v1/workflow/definition/{id}", currentCodes!);
            Assert.NotNull(await sharedCache.GetAsync<string[]>($"perm:{userId}"));

            using var scope = second.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Updateable<SysMenu>()
                .SetColumns(x => x.Title == "客户自定义详情标题")
                .Where(x => x.Id == 48_003)
                .ExecuteCommandAsync();
        }

        using (var third = new WorkflowAppFactory
        {
            DbPath = dbPath,
            ResetDatabase = false,
            Overrides = overrides,
        })
        {
            var client = await ClientFor(third, account);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync($"/api/v1/workflow/definition/{definitionId}")).StatusCode);
            await AssertStoredUpgradeState(third, userId, definitionId, expectedTitle: "客户自定义详情标题");
        }
    }

    private static Action<IServiceCollection> SharedCache(ICacheProvider cache) => services =>
    {
        services.RemoveAll<ICacheProvider>();
        services.AddSingleton(cache);
    };

    private static async Task AssertStoredUpgradeState(
        WorkflowAppFactory factory,
        long userId,
        long definitionId,
        string expectedTitle)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(SysSchemaVersion.Current,
            await db.Queryable<SysSchemaVersion>().Where(x => x.Id == 1).Select(x => x.Version).SingleAsync());
        Assert.Equal(expectedTitle,
            await db.Queryable<SysMenu>().Where(x => x.Id == 48_003).Select(x => x.Title).SingleAsync());
        var user = await db.Queryable<SysUser>().SingleAsync(x => x.Id == userId);
        Assert.True(user.Enabled);
        Assert.False(user.IsSuperAdmin);
        Assert.Equal(1L, user.OrgId);
        var roleId = await db.Queryable<SysUserRole>()
            .Where(x => x.UserId == userId)
            .Select(x => x.RoleId)
            .SingleAsync();
        var menuIds = await db.Queryable<SysRoleMenu>()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.MenuId)
            .ToListAsync();
        Assert.Equal(DefinitionMenus.Order(), menuIds.Order());
        Assert.True(await db.Queryable<WfDefinition>().AnyAsync(x => x.Id == definitionId));
        var upgradedPermissions = await db.Queryable<SysMenu>()
            .Where(x => new[] { 48_003L, 48_004L, 48_009L, 48_042L }.Contains(x.Id))
            .OrderBy(x => x.Id)
            .Select(x => x.Permission)
            .ToListAsync();
        Assert.Equal(new[]
        {
            "GET:/api/v1/workflow/definition/{id:long}",
            "GET:/api/v1/workflow/definition/versions/{id:long}",
            "DELETE:/api/v1/workflow/definition/{id:long}",
            "POST:/api/v1/workflow/outbox/{id:long}/replay",
        }, upgradedPermissions);
    }

    private static async Task<(long UserId, string Account)> CreateUser(
        WorkflowAppFactory factory,
        string prefix,
        IReadOnlyCollection<long> menuIds)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<IRepository<SysRole>>();
        var rbac = services.GetRequiredService<IRbacService>();
        var users = services.GetRequiredService<IUserService>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = new SysRole
        {
            Name = $"工作流回归-{suffix}",
            Code = $"wf-regression-{suffix}",
            Enabled = true,
        };
        await roles.InsertAsync(role);
        await rbac.SetRoleMenusAsync(role.Id, menuIds);
        await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);

        var account = $"{prefix}-{suffix}";
        var added = await users.AddAsync(new AddUserInput
        {
            Account = account,
            Password = Password,
            Name = account,
            Enabled = true,
            OrgId = 1,
            RoleIds = [role.Id],
        });
        return (added.Id, account);
    }

    private static async Task<HttpClient> ClientFor(
        WorkflowAppFactory factory,
        string account,
        string password = Password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, password));
        return client;
    }

    private static async Task<long> AddDefinition(HttpClient client, string name, long approverId)
    {
        var envelope = await (await client.PostJson("/api/v1/workflow/definition/add", new
        {
            name,
            groupName = "回归",
            model = UserApprovalModel(approverId),
        })).ReadEnvelope();
        Assert.Equal(0, envelope.GetProperty("code").GetInt32());
        return envelope.GetProperty("data").GetInt64();
    }

    private static object UserApprovalModel(long approverId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "发起",
            props = new { initiatorScope = Array.Empty<object>() },
            next = new
            {
                id = "approve",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { approverId } },
                    },
                    mode = "any",
                    formPerms = Array.Empty<object>(),
                },
                next = (object?)null,
            },
        },
    };

    private static async Task<System.Text.Json.JsonElement> GetOk(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.ReadEnvelope();
        Assert.Equal(0, envelope.GetProperty("code").GetInt32());
        return envelope.GetProperty("data");
    }

    private static async Task AssertForbidden(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(41001, (await response.ReadEnvelope()).GetProperty("code").GetInt32());
    }
}
