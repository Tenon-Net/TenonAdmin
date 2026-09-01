using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2a Task 3「multiLeader 发起时快照」回归线。设计定案(§十三 13.2 #1):发起瞬间沿
/// <c>SysUser.DirectorId</c> 链拍平存进 <see cref="WfInstance.LeaderChainJson"/>,之后组织调整不影响在途单。
/// 快照按 <c>level</c> 分别存储,避免启用过滤压缩链后再按下标截断造成越权。测试覆盖组织调整、
/// 分支臂、抄送节点、空快照、老实例回退、level 归一化与消费者自定义参数透传。
/// </summary>
public class WorkflowMultiLeaderSnapshotTests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Snapshot_survives_director_change_between_start_and_multi_leader_node_entry()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var secondId = await AddUser(admin, "wf-snap-second");
        var firstId = await AddUser(admin, "wf-snap-first", directorId: secondId);
        await AddUser(admin, "wf-snap-starter", directorId: firstId);
        var decoyId = await AddUser(admin, "wf-snap-decoy");
        var definitionId = await Publish(admin, "快照-顺序主管", GateThenMultiLeaderModel());

        var starter = await ClientFor(f, "wf-snap-starter");
        var first = await ClientFor(f, "wf-snap-first");
        var second = await ClientFor(f, "wf-snap-second");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var gateTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 发起后组织调整:first 的主管从 second 改成 decoy——按定案,这不该影响已发起实例的快照链。
        await UpdateDirector(admin, firstId, "wf-snap-first", decoyId);

        // 自批 gate 节点,推进到 leaders(multiLeader,level=2)节点——这是本用例的关键:
        // gate 与 leaders 分属两条 HTTP 请求(两个事务),留出组织调整能生效的窗口,
        // 才能把「快照」与「无快照回退实时上溯」的行为差异暴露出来。
        var gateApprove = await PostEnvelope(starter, "/api/v1/workflow/task/approve", new { taskId = gateTaskId });
        Assert.Equal(0, gateApprove.GetProperty("code").GetInt32());
        // 第一级仍是 first(starter 自己的 DirectorId 没变)。
        Assert.Contains(firstId,
            gateApprove.GetProperty("data").GetProperty("newAssigneeUserIds")
                .EnumerateArray().Select(x => x.GetInt64()));
        // multiLeader 顺序会签是同一条 wf_task 逐级晋级 actor,taskId 全程不变(晋级不产出新 createdTaskId)。
        var leadersTaskId = gateApprove.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId = leadersTaskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        // 断言第二级仍是 second,不是发起后才成为 first 主管的 decoy——与发起时一致(杀掉「未落实快照」的变异)。
        var newAssignees = firstApprove.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList();
        Assert.Contains(secondId, newAssignees);
        Assert.DoesNotContain(decoyId, newAssignees);

        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId = leadersTaskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Cc_node_multi_leader_resolution_uses_snapshot_not_live_director()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var secondId = await AddUser(admin, "wf-snap-cc-second");
        var firstId = await AddUser(admin, "wf-snap-cc-first", directorId: secondId);
        await AddUser(admin, "wf-snap-cc-starter", directorId: firstId);
        var decoyId = await AddUser(admin, "wf-snap-cc-decoy");
        var finalApproverId = await AddUser(admin, "wf-snap-cc-final");
        var definitionId = await Publish(admin, "快照-抄送逐级主管", GateThenCcThenApprovalModel(finalApproverId));

        var starter = await ClientFor(f, "wf-snap-cc-starter");
        var finalApprover = await ClientFor(f, "wf-snap-cc-final");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var gateTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 发起后组织调整:first 的主管从 second 改成 decoy——cc 节点这时还没进入(卡在 gate 待办后面,
        // gate 与 cc 分属两条 HTTP 请求/两个事务),留出组织调整能生效的窗口。
        await UpdateDirector(admin, firstId, "wf-snap-cc-first", decoyId);

        // 发起人批掉 gate → 引擎在同一请求内继续走 cc(multiLeader level=2)解析并写 wf_cc,
        // 再进入终审待办——一次性拿到这次 approve 累积的 newCcUserIds。
        var gateApprove = await PostEnvelope(starter, "/api/v1/workflow/task/approve", new { taskId = gateTaskId });
        Assert.Equal(0, gateApprove.GetProperty("code").GetInt32());

        var ccUserIds = gateApprove.GetProperty("data").GetProperty("newCcUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList();
        // 走快照 = [first, second];若 EnterCcAsync 没把 LeaderChainByLevel 传给解析上下文,会退回实时上溯
        // 得到 [first, decoy]——两者不同,才有区分力(杀掉「cc 调用点漏传快照」的变异)。
        Assert.Contains(secondId, ccUserIds);
        Assert.DoesNotContain(decoyId, ccUserIds);

        var finalTaskId = gateApprove.GetProperty("data").GetProperty("createdTaskId").GetInt64();
        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(finalApprover, "/api/v1/workflow/task/approve", new { taskId = finalTaskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Multi_leader_node_inside_branch_arm_is_covered_by_snapshot()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var secondId = await AddUser(admin, "wf-snap-arm-second");
        var firstId = await AddUser(admin, "wf-snap-arm-first", directorId: secondId);
        await AddUser(admin, "wf-snap-arm-starter", directorId: firstId);
        var definitionId = await Publish(admin, "快照-臂内主管", BranchMultiLeaderModel());

        var starter = await ClientFor(f, "wf-snap-arm-starter");
        var first = await ClientFor(f, "wf-snap-arm-first");
        var second = await ClientFor(f, "wf-snap-arm-second");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId, variablesJson = """{"amount":200}""" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 待办必须落在臂内节点 arm-leaders,办理人是链第一级 first(不是 starter 自己)——证明分支照常执行。
        var detail = await GetEnvelope(first, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal("arm-leaders",
            detail.GetProperty("data").GetProperty("myPendingTask").GetProperty("nodeId").GetString());

        // 直接查库断言快照已落——这条钉步骤 2 的树枚举:若 WfModelIndex.Nodes 退回只扫主链,
        // ResolveLeaderLevels 会把臂内这个 multiLeader 节点漏掉,得到空集合,LeaderChainJson 永远是 null。
        using (var scope = f.Services.CreateScope())
        {
            var instances = scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>();
            var row = await instances.GetByIdAsync(instanceId);
            Assert.NotNull(row);
            Assert.NotNull(row!.LeaderChainJson);
            var chains = JsonSerializer.Deserialize<Dictionary<int, List<long>>>(
                row.LeaderChainJson!, WfModelJson.Options);
            Assert.NotNull(chains);
            Assert.Equal([firstId, secondId], chains![2]);
        }

        var firstApprove = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        Assert.Equal(0, firstApprove.GetProperty("code").GetInt32());
        Assert.Contains(secondId,
            firstApprove.GetProperty("data").GetProperty("newAssigneeUserIds")
                .EnumerateArray().Select(x => x.GetInt64()));

        // 同一条 wf_task 逐级晋级,taskId 复用 firstTaskId(晋级不产出新 createdTaskId)。
        var secondApprove = await PostEnvelope(second, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        Assert.Equal(0, secondApprove.GetProperty("code").GetInt32());
        // 臂内 leaders 节点 Next==null,应汇合到 branch1.Next(merge-approve),不是直接完结。
        Assert.Equal((int)WfInstanceStatus.Running,
            secondApprove.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        var mergeTodo = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal("merge-approve",
            mergeTodo.GetProperty("data").GetProperty("myPendingTask").GetProperty("nodeId").GetString());
    }

    [Fact]
    public async Task Provider_without_snapshot_falls_back_to_live_director_lookup()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var secondId = await AddUser(admin, "wf-snap-fallback-second");
        var firstId = await AddUser(admin, "wf-snap-fallback-first", directorId: secondId);
        var starterId = await AddUser(admin, "wf-snap-fallback-starter", directorId: firstId);

        using var scope = f.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IApproverResolver>();
        var chain = await resolver.ResolveAsync(
            ApproverProviderKeys.MultiLeader,
            new ApproverResolveContext
            {
                InitiatorUserId = starterId,
                Params = new Dictionary<string, JsonElement>
                {
                    ["level"] = JsonSerializer.SerializeToElement(2),
                },
                LeaderChainByLevel = null, // 没有快照(老实例语义)→ 必须回退实时上溯 DirectorId 链。
            });

        Assert.Equal([firstId, secondId], chain);
    }

    [Fact]
    public async Task Empty_snapshot_returns_empty_without_querying_live_directors()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        // starter 有真实的主管链(若回退查库会解析出非空结果),用来证明「空快照」真的没有回退查库。
        var secondId = await AddUser(admin, "wf-snap-empty-second");
        var firstId = await AddUser(admin, "wf-snap-empty-first", directorId: secondId);
        var starterId = await AddUser(admin, "wf-snap-empty-starter", directorId: firstId);

        using var scope = f.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IApproverResolver>();
        var chain = await resolver.ResolveAsync(
            ApproverProviderKeys.MultiLeader,
            new ApproverResolveContext
            {
                InitiatorUserId = starterId,
                Params = new Dictionary<string, JsonElement>
                {
                    ["level"] = JsonSerializer.SerializeToElement(2),
                },
                LeaderChainByLevel = new Dictionary<int, IReadOnlyList<long>>
                {
                    [2] = [], // 命中 level=2 的空快照 → 必须原样返回空,不得回退查库。
                },
            });

        Assert.Empty(chain);
    }

    [Fact]
    public async Task Different_levels_keep_exact_filtered_chains_without_granting_higher_level_approval()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var level3Id = await AddUser(admin, "wf-snap-level3");
        var level2Id = await AddUser(admin, "wf-snap-level2", directorId: level3Id);
        var disabledLevel1Id = await AddUser(admin, "wf-snap-level1", directorId: level2Id);
        await AddUser(admin, "wf-snap-level-starter", directorId: disabledLevel1Id);
        var decoyId = await AddUser(admin, "wf-snap-level-decoy");
        await SetEnabled(admin, disabledLevel1Id, false);
        var definitionId = await Publish(admin, "快照-按级别精确链", TwoLevelMultiLeaderModel());

        var starter = await ClientFor(f, "wf-snap-level-starter");
        var level2 = await ClientFor(f, "wf-snap-level2");
        var level3 = await ClientFor(f, "wf-snap-level3");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var level2TaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using (var scope = f.Services.CreateScope())
        {
            var instances = scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>();
            var row = await instances.GetByIdAsync(instanceId);
            Assert.NotNull(row?.LeaderChainJson);
            var chains = JsonSerializer.Deserialize<Dictionary<int, List<long>>>(
                row!.LeaderChainJson!, WfModelJson.Options);
            Assert.NotNull(chains);
            Assert.Equal([level2Id], chains![2]);
            Assert.Equal([level2Id, level3Id], chains[3]);
        }

        // 快照后再改第 2 级主管的上级。若 level=3 快照缺失而回退实时解析,第二位会变成 decoy。
        await UpdateDirector(admin, level2Id, "wf-snap-level2", decoyId);

        // level=2 的精确链只有 level2 一人;批准后必须进入下一个 level=3 节点,
        // 不能把 level3 错挂在当前节点继续审批。
        var enterLevel3 = await PostEnvelope(level2, "/api/v1/workflow/task/approve", new { taskId = level2TaskId });
        Assert.Equal(0, enterLevel3.GetProperty("code").GetInt32());
        var firstLevel3Actors = enterLevel3.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList();
        Assert.Equal([level2Id], firstLevel3Actors);
        var level3TaskId = enterLevel3.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var promoteLevel3 = await PostEnvelope(level2, "/api/v1/workflow/task/approve", new { taskId = level3TaskId });
        Assert.Equal(0, promoteLevel3.GetProperty("code").GetInt32());
        var promoted = promoteLevel3.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList();
        Assert.Equal([level3Id], promoted);
        Assert.DoesNotContain(decoyId, promoted);

        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(level3, "/api/v1/workflow/task/approve", new { taskId = level3TaskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public async Task Level_is_normalized_and_custom_params_are_preserved_when_snapshotting()
    {
        var resolver = new CapturingApproverResolver();
        var engine = new WorkflowEngineProbe(resolver);
        var model = WfModelJson.Deserialize(JsonSerializer.Serialize(SingleMultiLeaderModel(0, "keep-me")))!;

        var levels = engine.GetLeaderLevels(model);
        Assert.Equal([1], levels.Keys);

        var chains = await engine.SnapshotAsync(
            new StartInstanceCmd { DefinitionVersionId = 1, StarterUserId = 7 },
            levels);

        Assert.NotNull(chains);
        Assert.Equal([700L], chains![1]);
        var call = Assert.Single(resolver.Calls);
        Assert.Null(call.LeaderChainByLevel);
        Assert.Equal(1, call.Params!["level"].GetInt32());
        Assert.Equal("keep-me", call.Params["customMarker"].GetString());
    }

    [Fact]
    public async Task Old_instance_null_snapshot_column_falls_back_to_current_director_chain()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var originalSecondId = await AddUser(admin, "wf-snap-old-second");
        var firstId = await AddUser(admin, "wf-snap-old-first", directorId: originalSecondId);
        await AddUser(admin, "wf-snap-old-starter", directorId: firstId);
        var liveSecondId = await AddUser(admin, "wf-snap-old-live-second");
        var definitionId = await Publish(admin, "快照-老实例回退", GateThenMultiLeaderModel());

        var starter = await ClientFor(f, "wf-snap-old-starter");
        var first = await ClientFor(f, "wf-snap-old-first");
        var liveSecond = await ClientFor(f, "wf-snap-old-live-second");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var gateTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using (var scope = f.Services.CreateScope())
        {
            var instances = scope.ServiceProvider.GetRequiredService<IRepository<WfInstance>>();
            var row = await instances.GetByIdAsync(instanceId);
            Assert.NotNull(row);
            row!.LeaderChainJson = null;
            Assert.Equal(1, await instances.UpdateAsync(row));
        }
        await UpdateDirector(admin, firstId, "wf-snap-old-first", liveSecondId);

        var enterLeaders = await PostEnvelope(starter, "/api/v1/workflow/task/approve", new { taskId = gateTaskId });
        Assert.Equal(0, enterLeaders.GetProperty("code").GetInt32());
        Assert.Equal([firstId], enterLeaders.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList());
        var leadersTaskId = enterLeaders.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var promoteLiveSecond = await PostEnvelope(first, "/api/v1/workflow/task/approve", new { taskId = leadersTaskId });
        Assert.Equal([liveSecondId], promoteLiveSecond.GetProperty("data").GetProperty("newAssigneeUserIds")
            .EnumerateArray().Select(x => x.GetInt64()).ToList());
        Assert.DoesNotContain(originalSecondId, promoteLiveSecond.GetProperty("data")
            .GetProperty("newAssigneeUserIds").EnumerateArray().Select(x => x.GetInt64()));

        Assert.Equal((int)WfInstanceStatus.Approved,
            (await PostEnvelope(liveSecond, "/api/v1/workflow/task/approve", new { taskId = leadersTaskId }))
            .GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    [Fact]
    public void Null_snapshot_json_remains_null_when_deserialized()
    {
        var engine = new WorkflowEngineProbe(new CapturingApproverResolver());
        Assert.Null(engine.Deserialize(null));
    }

    private static object TwoLevelMultiLeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "level-2",
                type = "approval",
                name = "二级主管链",
                props = new
                {
                    assignee = new
                    {
                        provider = "multiLeader",
                        @params = new Dictionary<string, object> { ["level"] = 2 },
                    },
                    mode = "any",
                },
                next = new
                {
                    id = "level-3",
                    type = "approval",
                    name = "三级主管链",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "multiLeader",
                            @params = new Dictionary<string, object> { ["level"] = 3 },
                        },
                        mode = "any",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    private static object SingleMultiLeaderModel(int level, string customMarker) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "leaders",
                type = "approval",
                name = "逐级审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "multiLeader",
                        @params = new Dictionary<string, object>
                        {
                            ["level"] = level,
                            ["customMarker"] = customMarker,
                        },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    private static object GateThenMultiLeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "gate",
                type = "approval",
                name = "自批闸门",
                props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                next = new
                {
                    id = "leaders",
                    type = "approval",
                    name = "逐级审批",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "multiLeader",
                            @params = new Dictionary<string, object> { ["level"] = 2 },
                        },
                        mode = "any",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    /// <summary>
    /// start → gate(approval,initiator,自批用来暂停) → cc(multiLeader level=2) → final(approval,专职用户)。
    /// cc 节点必须夹在两个跨事务的审批之间,才能让「组织调整发生在快照之后、cc 解析之前」这个窗口真实存在。
    /// final 用专职用户而非 initiator 自批,避开 Task 3 相邻节点同人去重的默认行为
    /// (否则 starter 批完 gate 后,gate 与 final 会被当成「同一人相邻重复」直接自动通过,拿不到 finalTaskId)。
    /// </summary>
    private static object GateThenCcThenApprovalModel(long finalApproverId) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "gate",
                type = "approval",
                name = "自批闸门",
                props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
                next = new
                {
                    id = "cc",
                    type = "cc",
                    name = "抄送逐级主管",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "multiLeader",
                            @params = new Dictionary<string, object> { ["level"] = 2 },
                        },
                    },
                    next = new
                    {
                        id = "final",
                        type = "approval",
                        name = "终审",
                        props = new
                        {
                            assignee = new
                            {
                                provider = "user",
                                @params = new Dictionary<string, object> { ["userIds"] = new[] { finalApproverId } },
                            },
                            mode = "any",
                        },
                        next = (object?)null,
                    },
                },
            },
        },
    };

    /// <summary>root(start) → branch1(armHigh: amount&gt;100 → arm-leaders[multiLeader level=2];armLow: 默认) → merge-approve。</summary>
    private static object BranchMultiLeaderModel() => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "branch1",
                type = "branch",
                name = "分支",
                conditions = new object[]
                {
                    new
                    {
                        id = "armHigh",
                        name = "大额",
                        isDefault = false,
                        expr = new { field = "amount", op = "gt", value = 100 },
                        next = new
                        {
                            id = "arm-leaders",
                            type = "approval",
                            name = "臂内逐级审批",
                            props = new
                            {
                                assignee = new
                                {
                                    provider = "multiLeader",
                                    @params = new Dictionary<string, object> { ["level"] = 2 },
                                },
                                mode = "any",
                            },
                            next = (object?)null,
                        },
                    },
                    new { id = "armLow", name = "默认", isDefault = true },
                },
                next = new
                {
                    id = "merge-approve",
                    type = "approval",
                    name = "汇合审批",
                    props = new { assignee = new { provider = "initiator", @params = new { } }, mode = "any" },
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

    private static async Task<long> AddUser(
        HttpClient admin,
        string account,
        long? directorId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
            ["directorId"] = directorId,
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task UpdateDirector(HttpClient admin, long userId, string account, long? directorId)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
            ["directorId"] = directorId,
        };
        var env = await (await admin.PutJson($"/api/v1/sys/user/{userId}", body)).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
    }

    private static async Task SetEnabled(HttpClient admin, long userId, bool enabled)
    {
        var env = await (await admin.PutJson(
            $"/api/v1/sys/user/{userId}/enabled",
            new { enabled })).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
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

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();

    private sealed class CapturingApproverResolver : IApproverResolver
    {
        public List<ApproverResolveContext> Calls { get; } = [];

        public Task<IReadOnlyList<long>> ResolveAsync(
            string providerKey,
            ApproverResolveContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ApproverProviderKeys.MultiLeader, providerKey);
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(context);
            return Task.FromResult<IReadOnlyList<long>>([context.InitiatorUserId * 100]);
        }
    }

    private sealed class WorkflowEngineProbe(IApproverResolver resolver)
        : WorkflowEngine(null!, resolver, null!, null!, TimeProvider.System, null!, null!, null!, null!, null!)
    {
        public IReadOnlyDictionary<int, Dictionary<string, JsonElement>?> GetLeaderLevels(WfModel model) =>
            ResolveLeaderLevels(model);

        public Task<IReadOnlyDictionary<int, IReadOnlyList<long>>?> SnapshotAsync(
            StartInstanceCmd cmd,
            IReadOnlyDictionary<int, Dictionary<string, JsonElement>?> levels) =>
            SnapshotLeaderChainsAsync(cmd.StarterUserId, cmd.StarterOrgId, levels, CancellationToken.None);

        public IReadOnlyDictionary<int, IReadOnlyList<long>>? Deserialize(string? json) =>
            DeserializeLeaderChainsByLevel(json);
    }
}
