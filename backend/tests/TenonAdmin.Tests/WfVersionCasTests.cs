using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 9「实例/Token 级 Version CAS」契约测试(数据库评审 §4.1、设计规划 §十五 15.1)。
/// <c>wf_instance.Version</c> 与 <c>wf_token.Version</c> 从 0 起,每次状态推进经
/// 「期望状态 + 版本」双条件 CAS 递增。
/// <para><b>⚠ 射程声明,先说清免得被误读</b>:<b>真实竞态的交错构造不出来,但 CAS 的失败分支可以经
/// 事务内 SPI 注入构造,本文件已覆盖 2 条。</b>
/// <list type="bullet">
/// <item><b>构造不出来的那一半</b>:真实竞态需要「A 读版本 → B 提交推走版本 → A 写」这个交错,而所有
/// <c>BeginXxxAsync</c> 都在自己的事务里现读版本号,单线程顺序执行下读到的必然是最新值
/// (与 Round 28 证伪 <c>Timeout_remind_does_not_block_human_action</c> 的根因逐字同型)。所以「两个请求
/// 并发、只有一个成功」这个形态不在射程内。</item>
/// <item><b>可以构造的那一半</b>:<c>claimed != 1</c> 这条分支<b>本身</b>是可达、可断言的 —— 不需要并发、
/// 不需要给命令加 <c>ExpectedVersion</c>、不改任何产品 API。引擎在自己的事务内调用可替换 SPI
/// (<c>IWorkflowFormBinder.ValidateOnStartAsync</c> 在 <c>BeginResubmitAsync</c> 的领取之前;
/// <c>IApproverResolver</c> 在 <c>TakeTransitionOp.CompleteInstanceAsync</c> 的领取之前),替换掉它、
/// 在里面用同一个 <c>SqlSugarScope</c> 单例把版本推走,后面那次领取拿到的就是过期版本号。见
/// <see cref="Resubmit_losing_token_cas_returns_48004_and_rolls_back_whole_transaction"/>(token 级,
/// <b>同时是「重提有没有锚点」的唯一出口</b>)与
/// <see cref="Instance_losing_cas_returns_48004_and_rolls_back_whole_transaction"/>(实例级)。这两条钉住
/// 了此前完全没有覆盖的四件事:抛而不是静默继续、码是 48004 而不是 500、<c>reason</c> 真的落到
/// <c>args</c>、以及<b>整事务回滚</b>(无半推进状态、通知未派发)。</item>
/// <item><b>仍在射程外的一处</b>:非末位投票那条路(见
/// <see cref="Cosign_first_approve_claims_token_and_locks_out_cancel"/>)的失败分支<b>构造不出来</b> ——
/// 撤销侧整条路上没有任何事务内 SPI 可注入。那一条只钉机制。</item>
/// </list>
/// 其余用例钉的是<b>机制</b>:「这个落点确实做了双条件领取并推进了版本」。这不是套套逻辑 —— 把任何一处
/// CAS 退回成无条件整对象更新,版本就不再前进,对应用例立刻红。</para>
/// <para><b>版本数字是算出来的,不是量出来的。</b>每条断言旁边都写清算式(几次进节点 + 几次终态领取)。
/// 下一轮若因为改了节点数而对不上,正确的处置是重算而不是把期望值改成实测值 —— 后者会让钉子当场失效。</para>
/// </summary>
public class WfVersionCasTests
{
    private const string Password = "Test@123456";

    /// <summary>
    /// <b>列建出来了、且新行读到 0</b> 的正向确认:发起之后还没有任何终态写入,故
    /// <c>wf_instance.Version</c> 必须是 0。这一条不是钉子(列没建出来的话查询直接抛)。
    /// <para><b>射程要说清</b>:这里的 0 来自 C# 属性初值被 <c>Insertable</c> 显式写进去,<b>与 DB 级默认值
    /// 无关</b> —— 本用例<b>不能</b>当作 <c>DefaultValue = "0"</c> 的落地确认。<c>DefaultValue</c> 真正的作用
    /// 是让存量表的 <c>ALTER TABLE ADD COLUMN</c> 走「先加可空列 → 回填 → 改 NOT NULL」三步序列
    /// (见 <see cref="WfInstance.Version"/> 的说明),那条路径只在升级已有库时才走,本套件的测试库全是
    /// 新建的,故它挂在 M2c 的四库契约测试上,验证点是「旧行 <c>Version</c> 读到 0」。</para>
    /// </summary>
    [Fact]
    public async Task New_instance_starts_at_version_zero()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-zero-starter");
        var approverId = await AddUser(admin, "wf-cas-zero-approver");
        var definitionId = await Publish(admin, "CAS-初始版本", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cas-zero-starter");
        var instanceId = await Start(starter, definitionId);

        Assert.Equal(0, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
    }

    /// <summary>
    /// 进节点就是状态推进:一次发起会跑两次 <c>EnterNodeOp</c>(<c>start</c> 一次、审批节点一次),
    /// 每次领取一次 token → <c>wf_token.Version == 2</c>。实例侧未动 → 仍 0。
    /// <para>这条同时证明「一个事务里领取多次 + 把新版本写回内存实例」是成立的:漏写回,第二次领取会对着
    /// 旧版本号抛一个假的 48004,发起直接失败。</para>
    /// </summary>
    [Fact]
    public async Task Start_advances_token_version_once_per_node_entry()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-enter-starter");
        var approverId = await AddUser(admin, "wf-cas-enter-approver");
        var definitionId = await Publish(admin, "CAS-进节点领取", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cas-enter-starter");
        var instanceId = await Start(starter, definitionId);

        // EnterNodeOp(start) + EnterNodeOp(approve-1) = 2 次领取。
        Assert.Equal(2, await TokenVersion(f, instanceId));
        Assert.Equal(0, await InstanceVersion(f, instanceId));
    }

    /// <summary>
    /// 实例完结(<c>TakeTransitionOp.CompleteInstanceAsync</c>)前先领取实例与 token。这是「终态写入 vs
    /// 重提」与「多 token 对同一实例终态」两类竞争的主出口 —— 并行网关下同实例两件待办各自通过任务级 CAS
    /// 后都会走到这里,双条件 CAS 只有一个拿得到 1 行。
    /// </summary>
    [Fact]
    public async Task Approve_to_completion_claims_instance_and_token()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-approve-starter");
        var approverId = await AddUser(admin, "wf-cas-approve-approver");
        var definitionId = await Publish(admin, "CAS-同意完结", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cas-approve-starter");
        var approver = await ClientFor(f, "wf-cas-approve-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(approver, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());

        // 实例:0 → 1(唯一一次终态领取)。
        Assert.Equal(1, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));
        // token:2 次进节点 + 1 次终态领取 = 3。
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Completed, await TokenStatus(f, instanceId));
    }

    /// <summary>
    /// 拒绝终止(<c>CompleteTaskOp.RejectInstanceAsync</c> 的 <c>Terminate</c> 分支)同样先领取实例与 token。
    /// 节点没配 <c>onReject</c> 即默认终止;<c>ToNode</c> 分支压根不写实例/token 状态,故不在本用例射程内。
    /// </summary>
    [Fact]
    public async Task Reject_terminate_claims_instance_and_token()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-reject-starter");
        var approverId = await AddUser(admin, "wf-cas-reject-approver");
        var definitionId = await Publish(admin, "CAS-拒绝终止", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cas-reject-starter");
        var approver = await ClientFor(f, "wf-cas-reject-approver");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var reject = await PostEnvelope(approver, "/api/v1/workflow/task/reject", new { taskId });
        Assert.Equal(0, reject.GetProperty("code").GetInt32());

        Assert.Equal(1, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Rejected, await InstanceStatus(f, instanceId));
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Cancelled, await TokenStatus(f, instanceId));
    }

    /// <summary>
    /// 撤销(<c>CancelInstanceOp</c>)。这里原先只锚<b>状态</b>(<c>Where(Id &amp;&amp; Status == Running)</c>),
    /// 只能拦住第二次撤销;拦不住「撤销 vs 一次会推进 token 的同意」—— 那条路上实例状态在两边看都还是
    /// Running。Task 9 把它升级成「期望状态 + 版本」双条件,本用例钉住的正是<b>版本</b>这一维真的加上了。
    /// </summary>
    [Fact]
    public async Task Cancel_claims_instance_and_token()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-cancel-starter");
        var approverId = await AddUser(admin, "wf-cas-cancel-approver");
        var definitionId = await Publish(admin, "CAS-撤销", AnyApprovalModel(approverId));

        var starter = await ClientFor(f, "wf-cas-cancel-starter");
        var instanceId = await Start(starter, definitionId);

        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(0, cancel.GetProperty("code").GetInt32());

        Assert.Equal(1, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Cancelled, await InstanceStatus(f, instanceId));
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Cancelled, await TokenStatus(f, instanceId));
    }

    /// <summary>
    /// 退回与重提各自领取 token —— <b>重提那一次是本轮补上的唯一锚点</b>。此前
    /// <c>BeginResubmitAsync</c> 全程无 CAS:两处 <c>Updateable(entity)</c> 都无条件,「无活跃待办」校验
    /// 只是读,双击重提会让两个事务都通过校验、都 <c>Plan(EnterNodeOp(root))</c> → 同一节点两套
    /// <c>wf_task</c>/actor + 两条 <c>InstanceResubmitted</c> + 两次通知,批掉一个还会留孤儿。
    /// <para>锚在 token 而不是实例:重提不改实例状态(Running → Running),没有可锚的状态变化;而 token 的
    /// <c>NodeId</c> 归零<b>就是</b>这次重提的状态推进。故本用例一并断言实例版本全程不动。</para>
    /// </summary>
    [Fact]
    public async Task Return_then_resubmit_claims_token_at_every_hop()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-resubmit-starter");
        var aId = await AddUser(admin, "wf-cas-resubmit-a");
        var definitionId = await Publish(admin, "CAS-退回重提", ReturnPrevPolicyModel(aId));

        var starter = await ClientFor(f, "wf-cas-resubmit-starter");
        var a = await ClientFor(f, "wf-cas-resubmit-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // EnterNodeOp(start) + EnterNodeOp(node1)。
        Assert.Equal(2, await TokenVersion(f, instanceId));

        // 退回:ReturnTaskOp 领取一次(状态仍 Active,只推进版本);无先例故 prev 策略退化到 start。
        var ret = await PostEnvelope(a, "/api/v1/workflow/task/return", new { taskId });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Active, await TokenStatus(f, instanceId));
        Assert.Equal(0, await InstanceVersion(f, instanceId));

        // 重提:BeginResubmitAsync 领取 1 次(本事务第一个写操作)+ EnterNodeOp(start) 1 次
        // + EnterNodeOp(node1) 1 次 = 3 → 3 + 3 = 6。
        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });
        Assert.Equal(0, resubmit.GetProperty("code").GetInt32());
        Assert.Equal(6, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Active, await TokenStatus(f, instanceId));
        // 重提不改实例状态,故实例版本全程不动。
        Assert.Equal(0, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
    }

    /// <summary>
    /// <b>前置约束 1 的双向钉子。</b>改派(委托 / 转办)只领取<b>任务级</b>版本,实例与 token 一字不动。
    /// <list type="bullet">
    /// <item><b>保住任务级 CAS 这一侧</b>:<c>ReassignTaskOpBase</c> 那段 <c>wf_task.Version</c> CAS 是转办与
    /// 委托全部并发安全性的<b>唯一</b>锚点。Task 9 把状态推进收口到实例/Token 级 CAS,但那一层对改派
    /// <b>不构成任何保护</b> —— 改派压根不改实例状态、不改 token,两个并发委托同一件待办时实例与 token 一字
    /// 不动,新 CAS 拦不住,后果是两行 Pending actor + 两条 <c>Delegate</c> 历史。把任务级 CAS 当成冗余放松
    /// 掉,这条保护会静默消失,本断言是它的报警器。</item>
    /// <item><b>不过度加锁这一侧</b>:也不能反过来给改派加实例级 CAS —— 那会让同实例上两件<b>不同</b>待办的
    /// 并发委托互相冲突,而它们本该各行其道。故实例与 token 版本必须<b>不变</b>。</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Reassign_claims_task_version_only_and_leaves_instance_and_token_untouched()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-reassign-starter");
        var aId = await AddUser(admin, "wf-cas-reassign-a");
        var bId = await AddUser(admin, "wf-cas-reassign-b");
        var cId = await AddUser(admin, "wf-cas-reassign-c");
        var definitionId = await Publish(admin, "CAS-改派只锚任务", AnyApprovalModel(aId));

        var starter = await ClientFor(f, "wf-cas-reassign-starter");
        var a = await ClientFor(f, "wf-cas-reassign-a");
        var b = await ClientFor(f, "wf-cas-reassign-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var instanceBefore = await InstanceVersion(f, instanceId);
        var tokenBefore = await TokenVersion(f, instanceId);
        var taskBefore = await TaskVersion(f, taskId);

        // 委托 A → B。
        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate",
            new { taskId, toUserId = bId });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());
        Assert.Equal(taskBefore + 1, await TaskVersion(f, taskId));
        Assert.Equal(instanceBefore, await InstanceVersion(f, instanceId));
        Assert.Equal(tokenBefore, await TokenVersion(f, instanceId));

        // 转办 B → C(同一件待办、同一套断言;两个兄弟共用基类那段 CAS)。
        var transferred = await PostEnvelope(b, "/api/v1/workflow/task/transfer",
            new { taskId, toUserId = cId });
        Assert.Equal(0, transferred.GetProperty("code").GetInt32());
        Assert.Equal(taskBefore + 2, await TaskVersion(f, taskId));
        Assert.Equal(instanceBefore, await InstanceVersion(f, instanceId));
        Assert.Equal(tokenBefore, await TokenVersion(f, instanceId));
    }

    /// <summary>
    /// <b>§4.1 第 1 类「审批与撤销」的另一半:会签 / 顺序签的<b>非末位投票</b>。</b>那条路上
    /// <c>CompleteTaskOp</c> 在计票未通过时提前返回,原先的全部写操作是任务级 CAS、<c>wf_task_actor</c>
    /// 翻 Done、插 <c>wf_his_task</c>、插 <c>wf_history</c> —— <b>实例一字不动、token 一字不动</b>,
    /// <c>TakeTransitionOp</c> 与 <c>EnterNodeOp</c> 都没进 Agenda,于是本轮的两级 CAS 一个都不触发:
    /// 并发撤销的 <c>ClaimInstanceAsync(Running)</c> 与 <c>ClaimTokenAsync(Active)</c> 两个条件全都满足,
    /// 两边都成功,落成 <c>Status = Cancelled</c> 与一条 <c>Approve</c> 行共存(与 Round 16 那条 P2 症状
    /// 逐字同型,只是触发条件收窄成「多人签核节点的非末位投票」)。修法取甲案:提前返回之前领取一次
    /// token —— 这个 token 上的签核进度前进了一步,与「进节点也算状态推进」是同一条论证。
    /// <para><b>射程</b>:「两边都成功」这个用户可见后果<b>构造不出来</b> —— 撤销侧
    /// (<c>BeginCancelAsync</c> → <c>CancelInstanceOp</c>)整条路上<b>没有任何事务内 SPI</b>可注入,
    /// 无法让它读到过期的 token 版本;单线程顺序执行下先提交的那一方总会被后一方的**读**判据挡住。
    /// 故本用例的钉子是<b>机制</b>(第一票之后 token 版本必须前进),下面那半段「撤销被 alreadyApproved
    /// 挡住」是端到端冒烟、<b>不是</b>钉子(去掉甲案的领取,它照旧绿)。</para>
    /// </summary>
    [Fact]
    public async Task Cosign_first_approve_claims_token_and_locks_out_cancel()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-cosign-starter");
        var aId = await AddUser(admin, "wf-cas-cosign-a");
        var bId = await AddUser(admin, "wf-cas-cosign-b");
        var definitionId = await Publish(admin, "CAS-会签首票", AllSignModel(aId, bId));

        var starter = await ClientFor(f, "wf-cas-cosign-starter");
        var a = await ClientFor(f, "wf-cas-cosign-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // EnterNodeOp(start) + EnterNodeOp(node1) = 2 次领取。
        Assert.Equal(2, await TokenVersion(f, instanceId));

        // A 投第一票:会签两人,未满票 → 待办仍在、Agenda 空。!passed 分支领取一次 token:2 → 3。
        var first = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal(WfTokenStatus.Active, await TokenStatus(f, instanceId));

        // 未满票不写实例状态 → 实例版本一字不动;待办与那条 Approve 行都在。
        Assert.Equal(0, await InstanceVersion(f, instanceId));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await ActiveTaskCount(f, instanceId));
        Assert.Equal(1, await ApproveRowCount(f, instanceId));

        // 冒烟(非钉子):撤销与这一票只有一个能成立。顺序执行下由准入读挡住。
        var cancel = await PostEnvelope(starter, "/api/v1/workflow/instance/cancel", new { instanceId });
        Assert.Equal(WorkflowErrorCode.CancelNotAllowed, cancel.GetProperty("code").GetInt32());
        Assert.Equal("alreadyApproved", cancel.GetProperty("args").GetProperty("reason").GetString());
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await ApproveRowCount(f, instanceId));
    }

    /// <summary>
    /// <b>token 级 CAS 的失败分支是可达、可断言的 —— 不需要并发、不需要给命令加 <c>ExpectedVersion</c>。</b>
    /// 造法:<c>BeginResubmitAsync</c> 在 <c>ClaimTokenAsync</c> <b>之前</b>、同一事务内调用了可替换 SPI
    /// <c>IWorkflowFormBinder.ValidateOnStartAsync</c>。测试注册一个 binder,它注入引擎正在用的那个
    /// <c>SqlSugarScope</c> 单例(故加入同一个环境事务)并把本实例的 <c>wf_token.Version</c> 推走;随后
    /// <c>ClaimTokenAsync</c> 用的是重提请求读进来的<b>过期</b>版本号 → 影响 0 行 → 抛 48004 →
    /// <c>UseTranAsync</c> 整体回滚。
    /// <para>这条钉住四件此前完全没有覆盖的事:①<c>claimed != 1</c> 真的抛而不是静默继续(把
    /// <c>if (claimed != 1) throw ...</c> 整段删掉,其余 172 条全绿);②错误码真的是 48004 而不是 500;
    /// ③<c>args["reason"]</c> 真的是 <c>tokenVersionConflict</c>;④<b>整事务回滚</b>真的发生 ——
    /// 无 <c>InstanceResubmitted</c> 事件、无新建 <c>wf_task</c>、排队的待办通知未派发、token 的
    /// <c>NodeId</c>/<c>Version</c> 一字未动。第 ④ 条正是「并发下不产生半推进状态」这个用户可见后果的
    /// 直接出口。</para>
    /// </summary>
    [Fact]
    public async Task Resubmit_losing_token_cas_returns_48004_and_rolls_back_whole_transaction()
    {
        var notifier = new CountingNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(notifier));
                s.Replace(ServiceDescriptor.Scoped<IWorkflowFormBinder>(
                    sp => new TokenVersionBumpingFormBinder(sp.GetRequiredService<ISqlSugarClient>())));
            },
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-lose-starter");
        var aId = await AddUser(admin, "wf-cas-lose-a");
        var definitionId = await Publish(admin, "CAS-重提输掉领取", ReturnPrevPolicyModel(aId));

        var starter = await ClientFor(f, "wf-cas-lose-starter");
        var a = await ClientFor(f, "wf-cas-lose-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 退回一次,把实例带进「Running + 无活跃待办」= 重提的唯一入口。
        var ret = await PostEnvelope(a, "/api/v1/workflow/task/return", new { taskId });
        Assert.Equal(0, ret.GetProperty("code").GetInt32());
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal("start", await TokenNodeId(f, instanceId));

        var assignedBefore = notifier.TaskAssignedCalls;
        var resubmitEventsBefore = await HistoryCount(f, instanceId, WfHistoryEventType.InstanceResubmitted);

        var resubmit = await PostEnvelope(starter, "/api/v1/workflow/instance/resubmit", new { instanceId });

        // ① + ②:抛出来了,而且是 48004 而不是 500。
        Assert.Equal(WorkflowErrorCode.InstanceStatusConflict, resubmit.GetProperty("code").GetInt32());
        // ③:reason 有消费方了(此前两个新 reason 值零断言)。
        Assert.Equal("tokenVersionConflict", resubmit.GetProperty("args").GetProperty("reason").GetString());
        Assert.True(resubmit.GetProperty("args").TryGetProperty("tokenId", out _));

        // ④:整事务回滚 —— 版本与节点回到退回后的状态,连 binder 那次推进也被一起撤销。
        Assert.Equal(3, await TokenVersion(f, instanceId));
        Assert.Equal("start", await TokenNodeId(f, instanceId));
        Assert.Equal(WfTokenStatus.Active, await TokenStatus(f, instanceId));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(0, await InstanceVersion(f, instanceId));
        Assert.Equal(resubmitEventsBefore, await HistoryCount(f, instanceId, WfHistoryEventType.InstanceResubmitted));
        Assert.Equal(0, await ActiveTaskCount(f, instanceId));
        Assert.Equal(assignedBefore, notifier.TaskAssignedCalls);
    }

    /// <summary>
    /// 同一手法对<b>实例级</b> CAS 也成立:<c>IApproverResolver</c> 在引擎事务内、且在
    /// <c>TakeTransitionOp.CompleteInstanceAsync</c> 的 <c>ClaimInstanceAsync</c> <b>之前</b>被调用
    /// (多节点链上)。模型是 <c>start → node1[A] → node2[A]</c>:A 批掉 node1 后,同一事务里进 node2 →
    /// 解析审批人(此处被注入,把实例版本推走)→ 「同一人相邻节点去重」判定 node2 整节点自动通过 →
    /// <c>TakeTransitionOp</c> → 实例完结领取,用的却是本事务开头读到的<b>过期</b>版本号。
    /// <para>钉住的四件事同上一条,只是级别换成实例:48004 + <c>instanceVersionConflict</c> + 整事务回滚
    /// (node1 的待办还在、没有 <c>Approve</c> 行、无 <c>InstanceCompleted</c> 事件、实例仍 Running)。</para>
    /// </summary>
    [Fact]
    public async Task Instance_losing_cas_returns_48004_and_rolls_back_whole_transaction()
    {
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IApproverResolver>(
                sp => new InstanceVersionBumpingResolver(sp, sp.GetRequiredService<ISqlSugarClient>()))),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-cas-inst-starter");
        var aId = await AddUser(admin, "wf-cas-inst-a");
        var definitionId = await Publish(admin, "CAS-实例输掉领取", SameApproverTwoNodeModel(aId));

        var starter = await ClientFor(f, "wf-cas-inst-starter");
        var a = await ClientFor(f, "wf-cas-inst-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });

        Assert.Equal(WorkflowErrorCode.InstanceStatusConflict, approve.GetProperty("code").GetInt32());
        Assert.Equal("instanceVersionConflict", approve.GetProperty("args").GetProperty("reason").GetString());
        Assert.True(approve.GetProperty("args").TryGetProperty("instanceId", out _));

        // 整事务回滚:待办没被关、没有 Approve 行、实例没完结。
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await ActiveTaskCount(f, instanceId));
        Assert.Equal(0, await ApproveRowCount(f, instanceId));
        Assert.Equal(0, await HistoryCount(f, instanceId, WfHistoryEventType.InstanceCompleted));
    }

    // ── 事务内 SPI 注入(把 CAS 的失败分支变成可达路径;见上面两条用例的 XML) ──

    /// <summary>
    /// 在 <c>BeginResubmitAsync</c> 的事务内把本实例的 <c>wf_token.Version</c> 推走,模拟「另一个事务
    /// 在领取之前提交了」。注入的 <see cref="ISqlSugarClient"/> 就是引擎在用的那个 <c>SqlSugarScope</c>
    /// 单例,故这条 UPDATE 加入同一个环境事务、随失败一起回滚。
    /// <para><c>InstanceId == 0</c> 时直接返回:发起路径也走 <c>ValidateOnStartAsync</c>,那时实例行还没
    /// 插入(引擎显式传 0),不该被干扰。</para>
    /// </summary>
    private sealed class TokenVersionBumpingFormBinder(ISqlSugarClient db) : NoOpWorkflowFormBinder
    {
        public override async Task ValidateOnStartAsync(
            WfFormBindContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.InstanceId == 0)
                return;

            const int bumped = 99;
            await db.Updateable<WfToken>()
                .SetColumns(t => new WfToken { Version = bumped })
                .Where(t => t.InstanceId == context.InstanceId)
                .ExecuteCommandAsync();
        }
    }

    /// <summary>
    /// 每次解析审批人都把 <c>wf_instance.Version</c> <b>读一次再 +1 写回</b>,再委托给真实解析器。
    /// 读后 +1 而不是设成某个常量:常量在「同一个值被写两次」时不产生过期(本类是 <c>Scoped</c>,
    /// 每个 HTTP 请求一个新实例,实例字段计数器跨请求会重置 —— 那个版本实测<b>钉不住</b>,approve 返回 0)。
    /// 测试库每个 factory 独占,故全表推进是安全的。
    /// </summary>
    private sealed class InstanceVersionBumpingResolver(IServiceProvider services, ISqlSugarClient db)
        : DefaultApproverResolver(services)
    {
        public override async Task<IReadOnlyList<long>> ResolveAsync(
            string providerKey,
            ApproverResolveContext context,
            CancellationToken cancellationToken = default)
        {
            var rows = await db.Queryable<WfInstance>()
                .ClearFilter<TenonAdmin.SqlSugar.IOrgScoped>()
                .ToListAsync();
            foreach (var row in rows)
            {
                var bumped = row.Version + 1;
                var id = row.Id;
                await db.Updateable<WfInstance>()
                    .SetColumns(i => new WfInstance { Version = bumped })
                    .Where(i => i.Id == id)
                    .ExecuteCommandAsync();
            }

            return await base.ResolveAsync(providerKey, context, cancellationToken);
        }
    }

    /// <summary>只数调用次数的通知器,用来断言「事务回滚后排队的通知一条也没派发」。</summary>
    private sealed class CountingNotifier : IWorkflowNotifier
    {
        public int TaskAssignedCalls;

        public Task TaskAssignedAsync(
            WfNotifyContext ctx, IReadOnlyList<long> userIds, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref TaskAssignedCalls);
            return Task.CompletedTask;
        }

        public Task InstanceCompletedAsync(WfNotifyContext ctx, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TaskUrgedAsync(
            WfNotifyContext ctx,
            long taskId,
            long? fromUserId,
            IReadOnlyList<long> toUserIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // ── 模型 ──

    /// <summary>start → node1(<b>all</b>,[A,B]) → null;第一票必然未满票。</summary>
    private static object AllSignModel(long aUserId, long bUserId) => new
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
                name = "node1",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId, bUserId } },
                    },
                    mode = "all",
                },
                next = (object?)null,
            },
        },
    };

    /// <summary>
    /// start → node1(any,[A]) → node2(any,<b>[A]</b>) → null。node2 故意复用 node1 的办理人,好让
    /// 「同一人相邻节点去重」把 node2 整节点自动通过 —— 于是「进 node2 解析审批人」与「实例完结领取」
    /// 落在**同一个**事务里。
    /// </summary>
    private static object SameApproverTwoNodeModel(long aUserId) => new
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
                name = "node1",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
                    },
                    mode = "any",
                },
                next = new
                {
                    id = "node2",
                    type = "approval",
                    name = "node2",
                    props = new
                    {
                        assignee = new
                        {
                            provider = "user",
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
                        },
                        mode = "any",
                    },
                    next = (object?)null,
                },
            },
        },
    };

    /// <summary>start → approve-1(any,[userIds]) → null。</summary>
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

    /// <summary>start → node1(any,[A],returnPolicy=prev) → null;无先例故退回目标退化到 <c>start</c>。</summary>
    private static object ReturnPrevPolicyModel(long aUserId) => new
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
                name = "node1",
                props = new
                {
                    assignee = new
                    {
                        provider = "user",
                        @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
                    },
                    mode = "any",
                    returnPolicy = "prev",
                },
                next = (object?)null,
            },
        },
    };

    // ── DB 直读(版本列没有 HTTP 出口,只能读库) ──

    private static async Task<int> InstanceVersion(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<TenonAdmin.SqlSugar.IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        Assert.NotNull(instance);
        return instance.Version;
    }

    private static async Task<WfInstanceStatus> InstanceStatus(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<TenonAdmin.SqlSugar.IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        Assert.NotNull(instance);
        return instance.Status;
    }

    /// <summary>本实例唯一那个 token(M2b 单 token;不按状态过滤,完结/撤销后也要读得到)。</summary>
    private static async Task<WfToken> TokenOf(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var tokens = await db.Queryable<WfToken>().Where(t => t.InstanceId == instanceId).ToListAsync();
        return Assert.Single(tokens);
    }

    private static async Task<int> TokenVersion(WorkflowAppFactory f, long instanceId) =>
        (await TokenOf(f, instanceId)).Version;

    private static async Task<WfTokenStatus> TokenStatus(WorkflowAppFactory f, long instanceId) =>
        (await TokenOf(f, instanceId)).Status;

    private static async Task<string> TokenNodeId(WorkflowAppFactory f, long instanceId) =>
        (await TokenOf(f, instanceId)).NodeId;

    /// <summary>本实例当前的活跃待办行数(<c>wf_task</c> 完成即物理删)。</summary>
    private static async Task<int> ActiveTaskCount(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).CountAsync();
    }

    /// <summary>本实例的 <c>Approve</c> 历史行数(撤销准入读的那张表)。</summary>
    private static async Task<int> ApproveRowCount(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId && h.Action == WfTaskAction.Approve)
            .CountAsync();
    }

    private static async Task<int> HistoryCount(
        WorkflowAppFactory f, long instanceId, WfHistoryEventType eventType)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == eventType)
            .CountAsync();
    }

    private static async Task<int> TaskVersion(WorkflowAppFactory f, long taskId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var task = await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync();
        Assert.NotNull(task);
        return task.Version;
    }

    // ── 脚手架(与 WfCancelTests 同款) ──

    private static async Task<long> Start(HttpClient starter, long definitionId)
    {
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        return start.GetProperty("data").GetProperty("instanceId").GetInt64();
    }

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

    private static async Task<System.Text.Json.JsonElement> PostEnvelope(
        HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
