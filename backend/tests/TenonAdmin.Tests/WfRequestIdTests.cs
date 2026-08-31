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
