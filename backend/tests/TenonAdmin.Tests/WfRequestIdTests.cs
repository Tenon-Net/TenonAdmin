using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 4「写命令收 `requestId`」契约测试。HTTP 入参 → 服务 → 引擎命令的**贯穿**,加上
/// <see cref="WfWriteCmd.RequestId"/> 里那份唯一的归一化/校验(空白→<c>null</c>、<c>Trim</c>、
/// ≤64、拒控制字符)。
/// <para><b>⚠ 射程声明</b>:本轮**不碰引擎**,回执与幂等行为都还不存在(Task 5)。所以这里钉的只是
/// 「值原封不动地到达了命令对象」与「非法值在进引擎前就被业务码挡下」。断言靠一个包住内置
/// <see cref="IWorkflowEngine"/> 的装饰器探针:它记下收到的命令后仍委托给真引擎,所以流程照常推进,
/// 用例断的是**真实调用链**而不是一个假的服务。</para>
/// </summary>
public class WfRequestIdTests
{
    private const string Password = "Test@123456";

    /// <summary>同意:请求里的 `requestId` 逐字到达 <see cref="CompleteTaskCmd"/>。</summary>
    [Fact]
    public async Task Approve_carries_the_request_id_into_the_command()
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rid-approve-starter");
        var approverId = await AddUser(admin, "wf-rid-approve-approver");
        var definitionId = await Publish(admin, "RequestId-同意", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-rid-approve-starter");
        var approver = await ClientFor(f, "wf-rid-approve-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(
            approver, "/api/v1/workflow/task/approve", new { taskId, requestId = "req-approve-001" });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        var cmd = Assert.IsType<CompleteTaskCmd>(probe.Last);
        Assert.Equal("req-approve-001", cmd.RequestId);
    }

    /// <summary>
    /// 发起:走的是**另一条**路径 —— `StartAsync` 收的是 DTO,没有新增参数,值从 `input.RequestId` 取。
    /// 两条路径都要钉,不然「服务签名那条通了、DTO 直传那条漏了」不会被发现。
    /// </summary>
    [Fact]
    public async Task Start_carries_the_request_id_into_the_command()
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rid-start-starter");
        var approverId = await AddUser(admin, "wf-rid-start-approver");
        var definitionId = await Publish(admin, "RequestId-发起", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-rid-start-starter");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "req-start-001" });
        Assert.Equal(0, start.GetProperty("code").GetInt32());

        var cmd = Assert.IsType<StartInstanceCmd>(probe.Last);
        Assert.Equal("req-start-001", cmd.RequestId);
    }

    /// <summary>
    /// 归一化:首尾空白被 <c>Trim</c>;**纯空白等同不传**(命令里是 <c>null</c>,不是空串)。
    /// 后半条是硬要求 —— 空白流到 Task 5 的 <c>WfIdentityHash.NormalizeRequestKey</c> 会抛
    /// <c>ArgumentException</c>(500),`null` 才是「本次不做幂等」的合法表达。
    /// </summary>
    [Theory]
    [InlineData("  req-trim-001  ", "req-trim-001")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public async Task Request_id_is_normalized_before_it_reaches_the_command(string sent, string? expected)
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        var suffix = expected ?? "blank" + sent.Length;
        await AddUser(admin, $"wf-rid-norm-{suffix}-s");
        var approverId = await AddUser(admin, $"wf-rid-norm-{suffix}-a");
        var definitionId = await Publish(admin, $"RequestId-归一-{suffix}", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, $"wf-rid-norm-{suffix}-s");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = sent });
        Assert.Equal(0, start.GetProperty("code").GetInt32());

        var cmd = Assert.IsType<StartInstanceCmd>(probe.Last);
        Assert.Equal(expected, cmd.RequestId);
    }

    /// <summary>
    /// 边界两侧各一条:64 字符通过、65 字符按 <see cref="WorkflowErrorCode.RequestIdInvalid"/> 拒。
    /// 上限来自 <c>wf_operation_receipt.RequestKey</c> 的列宽 —— 不拦,MySQL 非严格模式会静默截断诊断列。
    /// </summary>
    [Theory]
    [InlineData(64, 0)]
    [InlineData(65, WorkflowErrorCode.RequestIdInvalid)]
    public async Task Request_id_length_is_bounded_by_the_receipt_column_width(int length, int expectedCode)
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, $"wf-rid-len{length}-s");
        var approverId = await AddUser(admin, $"wf-rid-len{length}-a");
        var definitionId = await Publish(admin, $"RequestId-长度-{length}", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, $"wf-rid-len{length}-s");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = new string('k', length) });

        Assert.Equal(expectedCode, start.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 含控制字符(这里用换行)→ 拒。换行符是 <c>WfIdentityHash</c> 的分隔符,放进值里就是 hash 输入的歧义;
    /// 拦在 DTO 层,免得 Task 5 拿到一个「能进 DTO 却必然抛 <c>ArgumentException</c>」的值。
    /// </summary>
    [Fact]
    public async Task Request_id_with_a_control_character_is_rejected()
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rid-ctrl-s");
        var approverId = await AddUser(admin, "wf-rid-ctrl-a");
        var definitionId = await Publish(admin, "RequestId-控制字符", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-rid-ctrl-s");
        var start = await PostEnvelope(
            starter, "/api/v1/workflow/instance/start", new { definitionId, requestId = "req\nbad" });

        Assert.Equal(WorkflowErrorCode.RequestIdInvalid, start.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 催办带 `requestId` 照常成功。**这条记录的是一个刻意的空缺**:催办不进引擎(只追加事件 + 推通知),
    /// 控制器也刻意不透传该字段 —— 催办可重复、不做幂等(台账 `## 语义契约`)。给 urge 加上透传,
    /// 本条不会红;它钉的是「不因为共用 DTO 就把催办误当成写命令」这一点由 `probe.Last` 仍是发起命令来体现。
    /// </summary>
    [Fact]
    public async Task Urge_accepts_a_request_id_but_never_reaches_the_engine()
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rid-urge-starter");
        var approverId = await AddUser(admin, "wf-rid-urge-approver");
        var definitionId = await Publish(admin, "RequestId-催办", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-rid-urge-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var urge = await PostEnvelope(
            starter, "/api/v1/workflow/task/urge", new { taskId, requestId = "req-urge-001" });
        Assert.Equal(0, urge.GetProperty("code").GetInt32());

        // 催办之后引擎收到的**仍然是**发起那条命令:催办压根没进引擎。
        Assert.IsType<StartInstanceCmd>(probe.Last);
    }

    /// <summary>
    /// 剩下 **6 个**写动词各自的透传。Round 12 review 变异出的缺口:只钉 approve + start 时,把
    /// <c>cancel</c>(或 reject/transfer/delegate/return/resubmit)的 <c>input.RequestId</c> 换成
    /// <c>null</c>,套件**全绿** —— 那个动词就永远不做幂等,而且没人会发现。7 处透传是 7 份独立的手工活,
    /// 一份钉子盖不住另一份。
    /// <para>走一条流水线而不是 6 个夹具:每次 HTTP 调用都会覆盖 <c>probe.Last</c>,所以紧跟着断言即可。
    /// 撤销要求「无人已批的 Running 实例」,故另起一个实例。</para>
    /// </summary>
    [Fact]
    public async Task Every_remaining_write_verb_carries_its_own_request_id()
    {
        var probe = new CommandProbe();
        using var f = NewFactory(probe);
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-rid-all-starter");
        var aId = await AddUser(admin, "wf-rid-all-a");
        var bId = await AddUser(admin, "wf-rid-all-b");
        var cId = await AddUser(admin, "wf-rid-all-c");
        var definitionId = await Publish(admin, "RequestId-全动词", ReturnableApprovalModel(aId));

        var starter = await ClientFor(f, "wf-rid-all-starter");
        var a = await ClientFor(f, "wf-rid-all-a");
        var b = await ClientFor(f, "wf-rid-all-b");
        var c = await ClientFor(f, "wf-rid-all-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 转办:a → b,任务仍开着,只换办理人。
        var transfer = await PostEnvelope(
            a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = bId, requestId = "req-transfer-001" });
        Assert.Equal(0, transfer.GetProperty("code").GetInt32());
        Assert.Equal("req-transfer-001", Assert.IsType<TransferTaskCmd>(probe.Last).RequestId);

        // 委托:b → c。不能弹回给 a —— 委托禁止回给链上已持有过的人(DelegateTargetInvalid)。
        var delegated = await PostEnvelope(
            b, "/api/v1/workflow/task/delegate", new { taskId, toUserId = cId, requestId = "req-delegate-001" });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());
        Assert.Equal("req-delegate-001", Assert.IsType<DelegateTaskCmd>(probe.Last).RequestId);

        // 退回:prev 策略无先例 → 退到 start,实例仍 Running、无活跃待办。
        var returned = await PostEnvelope(
            c, "/api/v1/workflow/task/return", new { taskId, requestId = "req-return-001" });
        Assert.Equal(0, returned.GetProperty("code").GetInt32());
        Assert.Equal("req-return-001", Assert.IsType<ReturnTaskCmd>(probe.Last).RequestId);

        // 重提:发起人把退回的实例重新走一遍。
        var resubmit = await PostEnvelope(
            starter, "/api/v1/workflow/instance/resubmit", new { instanceId, requestId = "req-resubmit-001" });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());
        Assert.Equal("req-resubmit-001", Assert.IsType<ResubmitInstanceCmd>(probe.Last).RequestId);
        var retryTaskId = resubmit.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 拒绝:与同意共用 CompleteTaskCmd,但走的是 Controller 上**另一处**透传。
        var reject = await PostEnvelope(
            a, "/api/v1/workflow/task/reject", new { taskId = retryTaskId, requestId = "req-reject-001" });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());
        var rejectCmd = Assert.IsType<CompleteTaskCmd>(probe.Last);
        Assert.Equal(WfTaskAction.Reject, rejectCmd.Action);
        Assert.Equal("req-reject-001", rejectCmd.RequestId);

        // 撤销:另起一个实例(撤销要求无人已批)。
        var second = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        var secondId = second.GetProperty("data").GetProperty("instanceId").GetInt64();

        var cancel = await PostEnvelope(
            starter, "/api/v1/workflow/instance/cancel", new { instanceId = secondId, requestId = "req-cancel-001" });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());
        Assert.Equal("req-cancel-001", Assert.IsType<CancelInstanceCmd>(probe.Last).RequestId);
    }

    // ── 辅助 ──

    /// <summary>记下引擎收到的最后一条命令,再委托给内置引擎(流程照常推进)。</summary>
    private sealed class CommandProbe
    {
        public IWfCommand? Last { get; private set; }

        public void Record(IWfCommand command) => Last = command;
    }

    private sealed class ProbingEngine(CommandProbe probe, WorkflowEngine inner) : IWorkflowEngine
    {
        public Task<WfEngineResult> ExecuteAsync(IWfCommand command, CancellationToken cancellationToken = default)
        {
            probe.Record(command);
            return inner.ExecuteAsync(command, cancellationToken);
        }
    }

    private static WorkflowAppFactory NewFactory(CommandProbe probe) => new()
    {
        Overrides = services => services.AddScoped<IWorkflowEngine>(sp =>
            new ProbingEngine(probe, ActivatorUtilities.CreateInstance<WorkflowEngine>(sp))),
    };

    private static object AnyApprovalModel(params long[] userIds) => new
    {
        version = 1,
        root = new
        {
            id = "start",
            type = "start",
            name = "",
            next = new
            {
                id = "approve-1",
                type = "approval",
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = userIds },
                    },
                    mode = "any",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>start → node1(any,[userId],returnPolicy=prev) → null。无先例时退回优雅退化到 start。</summary>
    private static object ReturnableApprovalModel(long userId) => new
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
                name = "审批",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
                    },
                    mode = "any",
                    returnPolicy = "prev",
                },
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
