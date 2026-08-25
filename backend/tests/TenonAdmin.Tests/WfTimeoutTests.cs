using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2b Task 8「超时 Job」契约测试。两块:①建任务时按节点 <c>props.timeout.hours</c> 落真实
/// <c>wf_task.DueTime</c>(此前是硬编码 <c>null</c>);②<see cref="WfTimeoutJob"/> 扫到期待办并按
/// <c>timeout.action</c> 分流(提醒 / 自动通过 / 自动拒绝 / 转办)。
/// <para><b>测试一律手动 <c>new JobExecutionContext</c> 直接调 <c>ExecuteAsync</c></b>,不启调度器
/// (<c>WorkflowAppFactory</c> 已把 <c>Jobs:SchedulerEnabled</c> 关掉——种子播下的那行 Ready 任务
/// 若被真调度器按 cron 触发,会与本文件手动调的扫描并发操作同一张 <c>wf_task</c>,造成随机 flake)。</para>
/// <para>到期是靠**直接把 <c>DueTime</c> 改到过去**造出来的,不拨全局时钟:超时判据是不等式,拨钟会
/// 连带影响 JWT / 会话 / 审计字段,而本任务要验的只是「到期了会怎样」。</para>
/// </summary>
public class WfTimeoutTests
{
    private const string Password = "Test@123456";

    // ── 一、DueTime 落库 ────────────────────────────────────────────────

    /// <summary>
    /// 节点配了 <c>timeout.hours = 24</c> → 建任务时 <c>DueTime ≈ now + 24h</c>。
    /// <para><b>区间断言而非精确相等</b>:<c>DueTime</c> 是 <c>DateTime</c> 列,MySQL <c>datetime(0)</c>
    /// 对毫秒四舍五入;而超时判据是不等式(<c>DueTime &lt;= now</c>),对半秒偏差免疫,所以精确相等
    /// 既没必要也会在别的库上偶发红。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_hours_fills_due_time_on_task_creation()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-due-fill-starter");
        var aId = await AddUser(admin, "wf-due-fill-a");
        var definitionId = await Publish(admin, "超时-DueTime 落库",
            SingleApprovalModel([aId], timeout: new { hours = 24, action = "remind" }));

        var starter = await ClientFor(f, "wf-due-fill-starter");
        var a = await ClientFor(f, "wf-due-fill-a");

        var before = DateTime.Now;
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var todo = Assert.Single(await TodoItemsFor(a, instanceId));
        var dueRaw = todo.GetProperty("dueTime");
        Assert.Equal(JsonValueKind.String, dueRaw.ValueKind);
        Assert.InRange(dueRaw.GetDateTime(), before.AddHours(23), DateTime.Now.AddHours(25));
    }

    /// <summary>节点没配 <c>timeout</c> → <c>DueTime</c> 保持 <c>null</c>(否则所有待办突然都会超时)。</summary>
    [Fact]
    public async Task Node_without_timeout_leaves_due_time_null()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-due-none-starter");
        var aId = await AddUser(admin, "wf-due-none-a");
        var definitionId = await Publish(admin, "超时-未配置", SingleApprovalModel([aId]));

        var starter = await ClientFor(f, "wf-due-none-starter");
        var a = await ClientFor(f, "wf-due-none-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var todo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal(JsonValueKind.Null, todo.GetProperty("dueTime").ValueKind);
    }

    /// <summary>
    /// <c>hours = 0</c>(设计器上只点了 <c>action</c>、没填小时数的形态)必须等于「不启用」。
    /// 把 0 当「立刻到期」的话,这类节点建完任务当场就被超时策略处置。
    /// </summary>
    [Fact]
    public async Task Timeout_hours_zero_leaves_due_time_null()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-due-zero-starter");
        var aId = await AddUser(admin, "wf-due-zero-a");
        var definitionId = await Publish(admin, "超时-hours 为 0",
            SingleApprovalModel([aId], timeout: new { hours = 0, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-due-zero-starter");
        var a = await ClientFor(f, "wf-due-zero-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        var todo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal(JsonValueKind.Null, todo.GetProperty("dueTime").ValueKind);
    }

    /// <summary>
    /// <b>Task 6 给本任务留下的前置约束</b>:委托与转办都是「同一件待办换人办」,<b>不得</b>重置超时时钟。
    /// 这条定案在 <c>DueTime</c> 硬编码 <c>null</c> 的年代是空真、零可观测出口;<c>DueTime</c> 一落地它就
    /// 变成可违反的真命题——「换人了该给新办理人重新计时」听起来很合理,却让任何人靠反复委托无限续期。
    /// <c>CreateTime</c> 一并钉住,它是 <c>DurationMs</c> 的计时基准。
    /// </summary>
    [Fact]
    public async Task Delegate_and_transfer_keep_original_due_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-due-keep-starter");
        var aId = await AddUser(admin, "wf-due-keep-a");
        var bId = await AddUser(admin, "wf-due-keep-b");
        var cId = await AddUser(admin, "wf-due-keep-c");
        var definitionId = await Publish(admin, "超时-改派不重置",
            SingleApprovalModel([aId], timeout: new { hours = 24, action = "remind" }));

        var starter = await ClientFor(f, "wf-due-keep-starter");
        var a = await ClientFor(f, "wf-due-keep-a");
        var b = await ClientFor(f, "wf-due-keep-b");
        var c = await ClientFor(f, "wf-due-keep-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var original = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal(JsonValueKind.String, original.GetProperty("dueTime").ValueKind);
        var dueTime = original.GetProperty("dueTime").GetDateTime();
        var createTime = original.GetProperty("createTime").GetDateTime();

        var delegated = await PostEnvelope(a, "/api/v1/workflow/task/delegate", new { taskId, toUserId = bId });
        Assert.Equal(0, delegated.GetProperty("code").GetInt32());

        var afterDelegate = Assert.Single(await TodoItemsFor(b, instanceId));
        Assert.Equal(taskId, afterDelegate.GetProperty("taskId").GetInt64());
        Assert.Equal(dueTime, afterDelegate.GetProperty("dueTime").GetDateTime());
        Assert.Equal(createTime, afterDelegate.GetProperty("createTime").GetDateTime());

        var transferred = await PostEnvelope(b, "/api/v1/workflow/task/transfer", new { taskId, toUserId = cId });
        Assert.Equal(0, transferred.GetProperty("code").GetInt32());

        var afterTransfer = Assert.Single(await TodoItemsFor(c, instanceId));
        Assert.Equal(taskId, afterTransfer.GetProperty("taskId").GetInt64());
        Assert.Equal(dueTime, afterTransfer.GetProperty("dueTime").GetDateTime());
        Assert.Equal(createTime, afterTransfer.GetProperty("createTime").GetDateTime());
    }

    // ── 二、Remind ──────────────────────────────────────────────────────

    /// <summary>
    /// 提醒:写一条 <see cref="WfHistoryEventType.TimeoutFired"/> + 对当前 Pending 办理人推一次催办,
    /// <c>fromUserId == null</c> 即「系统触发」(这个语义是 Task 1 就在 <see cref="IWorkflowNotifier"/>
    /// 注释里留好的插头,本任务一个新方法都不加)。实例与待办状态一字不动。
    /// </summary>
    [Fact]
    public async Task Timeout_remind_writes_timeout_fired_and_notifies_pending_approvers()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-remind-starter");
        var aId = await AddUser(admin, "wf-remind-a");
        var definitionId = await Publish(admin, "超时-提醒",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "remind" }));

        var starter = await ClientFor(f, "wf-remind-starter");
        var a = await ClientFor(f, "wf-remind-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        var urged = Assert.Single(capturing.TaskUrgedCalls);
        Assert.Null(urged.FromUserId);
        Assert.Equal(taskId, urged.TaskId);
        Assert.Equal(new[] { aId }, urged.ToUserIds);
        Assert.Equal(1, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));

        // 只推送不改状态:待办还在 A 手里,实例仍 Running,零 wf_his_task 行。
        var todo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal(taskId, todo.GetProperty("taskId").GetInt64());
        Assert.Equal(0, await HisTaskCount(f, instanceId));
    }

    /// <summary>
    /// 防刷:契约只写了「可重复触发」没写节奏,按字面实现的话一件逾期三天的待办在 5 分钟一拍下会被
    /// 提醒 864 次。最小间隔默认 = 节点自己的 <c>timeout.hours</c>(下限 1 小时),存储就用**我们本来
    /// 就要写的那条 <see cref="WfHistoryEventType.TimeoutFired"/> 事件**当「上次提醒时间」,零新增列。
    /// </summary>
    [Fact]
    public async Task Timeout_remind_is_throttled_within_min_interval()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing)),
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-remind-throttle-starter");
        var aId = await AddUser(admin, "wf-remind-throttle-a");
        var definitionId = await Publish(admin, "超时-提醒防刷",
            SingleApprovalModel([aId], timeout: new { hours = 24, action = "remind" }));

        var starter = await ClientFor(f, "wf-remind-throttle-starter");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(2));
        await RunTimeoutJob(f);
        await RunTimeoutJob(f);

        Assert.Single(capturing.TaskUrgedCalls);
        Assert.Equal(1, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));
    }

    /// <summary>
    /// <b>提醒不做 <c>wf_task</c> 版本 CAS</b>(设计规划 §14.1 第 1 条的精确化,不是翻转)。若给它也加
    /// 版本 CAS:办理人正点「同意」时提醒的 CAS 先提交把 <c>Version</c> 推走,人工 CAS 落空 → 用户
    /// **为了一条提醒**收到「待办已被他人处理」(48007)。
    /// <para><b>承重的是「提醒前后 <c>wf_task.Version</c> 一字不动」这条断言,不是后面那次 approve。</b>
    /// 真实竞态需要「人工侧读 <c>Version</c> → 提醒 CAS 提交 → 人工侧 CAS」的交错,而
    /// <c>CompleteTaskCmd</c> 压根没有 <c>ExpectedVersion</c> 入参——人工路径在**自己的事务里**现读
    /// <c>Version</c>,单线程套件里 <c>RunTimeoutJob</c> 整个跑完之后才发请求,读到的必然是新版本号,
    /// CAS 永远对得上。所以「approve 返 0」这半条是**套套逻辑**(实跑证伪过:给
    /// <c>HandleRemindAsync</c> 加标准版本 CAS 后它照样绿)。版本不变量则直接钉住机制本身:加了 CAS
    /// 就一定红。approve 那半条保留作端到端冒烟,不当钉子。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_remind_does_not_block_human_action()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-remind-human-starter");
        var aId = await AddUser(admin, "wf-remind-human-a");
        var definitionId = await Publish(admin, "超时-提醒不挡人工",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "remind" }));

        var starter = await ClientFor(f, "wf-remind-human-starter");
        var a = await ClientFor(f, "wf-remind-human-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        var versionBeforeRemind = await VersionOf(f, taskId);
        var instanceVersionBeforeRemind = await InstanceVersionOf(f, instanceId);
        var tokenVersionBeforeRemind = await TokenVersionOf(f, instanceId);
        await RunTimeoutJob(f);

        // 这条是钉子:提醒只写事件 + 推送,**不得**碰 wf_task.Version。
        Assert.Equal(versionBeforeRemind, await VersionOf(f, taskId));
        // Task 9 补的两条同款钉子:实例与 token 各有了自己的 Version 之后,「顺手给提醒加 CAS」多了两个
        // 新落点,而症状仍然是办理人为了一条提醒收到 48004/48007。三个级别都不得被提醒碰到。
        Assert.Equal(instanceVersionBeforeRemind, await InstanceVersionOf(f, instanceId));
        Assert.Equal(tokenVersionBeforeRemind, await TokenVersionOf(f, instanceId));

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved,
            approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());
    }

    // ── 三、AutoPass / AutoReject ───────────────────────────────────────

    /// <summary>
    /// 自动通过:token 推进、实例完结,<c>wf_his_task</c> 记一行 <see cref="WfTaskAction.Approve"/>。
    /// <para><b>「张三同意了」而张三什么也没做——这行审计误导真实存在,处置手段是原生动词 + 同事务的
    /// <see cref="WfHistoryEventType.TimeoutFired"/> 事件 + <c>Comment</c>,不是新枚举值。</b>身份没得选:
    /// <c>CompleteTaskOp</c> 的 actor 认领是「仅本人可办」,系统账号必然认领不到。故两条断言都是承重的
    /// ——事件在(结构化证据)、<c>Comment</c> 非空(不读事件流的视图也不误导)。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_auto_pass_advances_token_and_marks_history()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-autopass-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-autopass-a");
        var definitionId = await Publish(admin, "超时-自动通过",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));

        var detail = await GetEnvelope(starter, $"/api/v1/workflow/instance/{instanceId}");
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var hisTask = Assert.Single(detail.GetProperty("data").GetProperty("hisTasks").EnumerateArray().ToList());
        Assert.Equal((int)WfTaskAction.Approve, hisTask.GetProperty("action").GetInt32());
        Assert.Equal(aId, hisTask.GetProperty("userId").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(hisTask.GetProperty("comment").GetString()));

        Assert.Equal(1, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));
    }

    /// <summary>
    /// 会签(<see cref="WfSignMode.All"/>)下自动通过必须**对每个 Pending 办理人各记一次**。只批一个的话
    /// 会签分支看到还有 Pending → 节点原地不动 → 下一拍再来,超时对会签节点等于失效。
    /// </summary>
    [Fact]
    public async Task Timeout_auto_pass_on_all_sign_mode_acts_for_every_pending_approver()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-autopass-all-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-autopass-all-a");
        var bId = await AddUser(admin, "wf-autopass-all-b");
        var definitionId = await Publish(admin, "超时-会签自动通过",
            SingleApprovalModel([aId, bId], mode: "all", timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));
        Assert.Equal(2, await HisTaskCount(f, instanceId, WfTaskAction.Approve));
    }

    /// <summary>
    /// 或签(<see cref="WfSignMode.Any"/>)下自动通过必须**恰好入队一个** <c>CompleteTaskOp</c>。
    /// <para>这条钉的是全任务最迷惑的症状:或签分支直接通过,第一个 Op 就把 <c>wf_task</c> /
    /// <c>wf_task_actor</c> **物理删除**;若对两个 Pending 各入队一个,第二个 Op 的任务级 CAS 影响行数
    /// 为 0 → <c>TaskConflict</c> → **整个事务回滚**,现象是「超时什么都没干」,从日志完全看不出原因。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_auto_pass_on_any_sign_mode_acts_once()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-autopass-any-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-autopass-any-a");
        var bId = await AddUser(admin, "wf-autopass-any-b");
        var definitionId = await Publish(admin, "超时-或签自动通过",
            SingleApprovalModel([aId, bId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await HisTaskCount(f, instanceId, WfTaskAction.Approve));
    }

    /// <summary>自动拒绝:一票否决,实例终止为 <see cref="WfInstanceStatus.Rejected"/>。</summary>
    [Fact]
    public async Task Timeout_auto_reject_terminates_instance()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-autoreject-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-autoreject-a");
        var definitionId = await Publish(admin, "超时-自动拒绝",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoReject" }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Rejected, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await HisTaskCount(f, instanceId, WfTaskAction.Reject));
        Assert.Equal(0, await HisTaskCount(f, instanceId, WfTaskAction.Approve));
    }

    /// <summary>
    /// 超时自动拒绝 + <c>onReject = toNode</c> 是**同一段代码**走的拒绝路由,故 Round 22 的「向后跳转
    /// 重置去重基线」是它自带的副产品——<c>CompleteTaskOp</c> 无条件先插那行 <c>Reject</c>,
    /// <c>EnterNodeOp</c> 倒序遇到它就截断基线。
    /// <para><c>start→node1[A]→node2[B, autoReject, onReject→node1]</c>:A 批过 node1 之后被退回 node1,
    /// **A 必须重新拿到 node1 的待办**。基线若没被重置,node1 会被判成「A 已审过」而整节点自动通过 →
    /// 落回 node2 → 拒绝人 B 拿回自己的待办,拒绝路由退化成可无限循环的空操作。</para>
    /// <para>这条链要用测试钉住的原因:「自动拒绝复用 <c>CompleteTaskOp</c>」这个决定一旦被改成
    /// 「Job 自己拼一段拒绝逻辑」,基线重置就会静默丢失。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_auto_reject_with_on_reject_to_node_resets_dedup_baseline()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-autoreject-route-starter");
        var aId = await AddUser(admin, "wf-autoreject-route-a");
        var bId = await AddUser(admin, "wf-autoreject-route-b");
        var definitionId = await Publish(admin, "超时-自动拒绝路由", RejectRouteModel(aId, bId));

        var starter = await ClientFor(f, "wf-autoreject-route-starter");
        var a = await ClientFor(f, "wf-autoreject-route-a");
        var b = await ClientFor(f, "wf-autoreject-route-b");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var node1TaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = node1TaskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Single(await TodoItemsFor(b, instanceId));

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        var aTodo = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal("node1", aTodo.GetProperty("nodeId").GetString());
        Assert.Empty(await TodoItemsFor(b, instanceId));
    }

    // ── 四、Transfer ────────────────────────────────────────────────────

    /// <summary>
    /// 超时自动转办:目标拿到同一件待办,<b>并且 <c>DueTime</c> 必须被清掉</b>。
    /// <para>转办既不删待办也不推进 token,<c>DueTime</c> 还留在过去 → 下一拍再扫到 → 目标已是 actor →
    /// <c>alreadyActor</c> 抛 48010 → 每拍失败一次直到有人手工办掉。所以第二次扫描「什么都不产生」这条
    /// 断言与「<c>DueTime</c> 为 null」是同一件事的两个出口。</para>
    /// <para>动作标签记的是 <see cref="WfTaskAction.Transfer"/>(与人工转办同标签,不造新枚举值):
    /// 自动通过/拒绝的身份机制上只能复用原生动词,给转办单独造一个值会形成「Job 转办分得清、Job 同意
    /// 分不清」的混合策略,而后者的审计误导明显更重。真相由同事务的 <c>TimeoutFired</c> 说明。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_transfer_hands_task_to_target_and_clears_due_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-timeout-transfer-starter");
        var aId = await AddUser(admin, "wf-timeout-transfer-a");
        var cId = await AddUser(admin, "wf-timeout-transfer-c");
        var definitionId = await Publish(admin, "超时-自动转办",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "transfer", transferUserId = cId }));

        var starter = await ClientFor(f, "wf-timeout-transfer-starter");
        var a = await ClientFor(f, "wf-timeout-transfer-a");
        var c = await ClientFor(f, "wf-timeout-transfer-c");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Empty(await TodoItemsFor(a, instanceId));
        var cTodo = Assert.Single(await TodoItemsFor(c, instanceId));
        Assert.Equal(taskId, cTodo.GetProperty("taskId").GetInt64());
        Assert.Equal(JsonValueKind.Null, cTodo.GetProperty("dueTime").ValueKind);
        Assert.Null(await DueTimeOf(f, taskId));
        Assert.Equal(1, await HisTaskCount(f, instanceId, WfTaskAction.Transfer));
        Assert.Equal(1, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));

        // 第二次扫描必须**压根扫不到**这件待办。断言扫描日志而不只是「没多出行」是有意的:
        // 不清 DueTime 时第二拍的现象不是「多一行」而是**每拍静默失败一次**(目标已是 actor →
        // alreadyActor 抛 48010 → 整个事务连 TimeoutFired 一起回滚),行数断言对它完全瞎。
        Assert.Equal(["超时扫描:无到期待办。"], await RunTimeoutJob(f));
        Assert.Equal(1, await HisTaskCount(f, instanceId, WfTaskAction.Transfer));
        Assert.Single(await TodoItemsFor(c, instanceId));
    }

    /// <summary>
    /// 发布期就拒掉「自动转办但没配目标用户」。不校验的后果是**每一拍失败一次**的永久失败形态:
    /// 待办到期后每次扫描都抛 48010,直到有人手工办掉。复用 48002 + <c>reason</c>,零新增错误码。
    /// </summary>
    [Fact]
    public async Task Timeout_transfer_without_target_user_is_rejected_at_publish()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var aId = await AddUser(admin, "wf-timeout-badcfg-a");

        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new
        {
            name = "超时-转办缺目标",
            model = SingleApprovalModel([aId], timeout: new { hours = 1, action = "transfer" }),
        });
        Assert.Equal(0, added.GetProperty("code").GetInt32());

        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish",
            new { id = added.GetProperty("data").GetInt64() });
        Assert.Equal(WorkflowErrorCode.ModelInvalid, published.GetProperty("code").GetInt32());
        Assert.Equal("timeoutTransferUserIdInvalid",
            published.GetProperty("args").GetProperty("reason").GetString());
    }

    // ── 五、扫描形状:失败隔离 / CAS / 批量 ─────────────────────────────

    /// <summary>
    /// 一条待办处理失败不得拖垮整批。这不是风格选择:一个节点配错了转办目标若能把整个 Job 打成
    /// Failed,重试 → 再失败 → 连败到阈值转 Panic,**全库所有超时策略就此停摆**。
    /// <para>坏的那条(转办目标 = 办理人自己,发布期校验只管「目标为正」拦不住)排在前面(<c>DueTime</c>
    /// 更早),好的那条必须照样被处理完。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_scan_isolates_per_task_failure()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-timeout-isolate-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-timeout-isolate-a");
        // 转办目标 = 当前办理人自己 → ReassignTaskOpBase 抛 48010,是「永久失败」形态。
        var badDefinitionId = await Publish(admin, "超时-配错的转办",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "transfer", transferUserId = aId }));
        var goodDefinitionId = await Publish(admin, "超时-正常的自动通过",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var bad = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = badDefinitionId });
        Assert.Equal(0, bad.GetProperty("code").GetInt32());
        var badInstanceId = bad.GetProperty("data").GetProperty("instanceId").GetInt64();
        var badTaskId = bad.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var good = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = goodDefinitionId });
        Assert.Equal(0, good.GetProperty("code").GetInt32());
        var goodInstanceId = good.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, badInstanceId, TimeSpan.FromDays(2));
        await MakeDue(f, goodInstanceId, TimeSpan.FromDays(1));
        var log = await RunTimeoutJob(f);

        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, badInstanceId));
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, goodInstanceId));
        // 计数也钉上:失败被计数而不是被吞成「成功」,运维在执行记录里看得到。
        // 光有计数还不够定位到具体单子(永久失败形态是每拍失败一次、Job 仍返 Success、永不告警),
        // 故每条失败另起一行带 taskId 与错误码。
        Assert.Equal(2, log.Count);
        Assert.Contains($"待办 {badTaskId}", log[0]);
        Assert.Contains($"错误码 {WorkflowErrorCode.TransferTargetInvalid}", log[0]);
        Assert.Equal(
            "超时扫描:命中 2,提醒 0,自动通过 1,自动拒绝 0,转办 0,跳过 0,失败 1。",
            log[1]);
        // 事务外补的那行 TimeoutFired 是永久失败在数据层面的唯一痕迹(引擎那条随失败一起回滚了)。
        Assert.Equal(1, await HistoryCount(f, badInstanceId, WfHistoryEventType.TimeoutFired));
    }

    /// <summary>
    /// <b>永久失败必须有升级出口。</b>发布期校验只挡「没配 <c>transferUserId</c>」,挡不住运行期成因
    /// (目标用户事后被停用、目标事后成了本待办的 actor)。失败后整事务回滚、<c>DueTime</c> 留在过去 →
    /// 每拍重试一次且**永远**如此;Job 返回 Success,<c>AlertByNotice</c> 只对 Job 级 Failed 生效 →
    /// **永不告警**;同时这行一直占着批量名额(P1「饿死」的第 3 类死行)。
    /// <para>处置不是推翻失败隔离,而是给同一件待办的连续失败一个阈值:超过就落一条带错误码的
    /// <see cref="WfHistoryEventType.TimeoutFired"/> 并清 <c>DueTime</c> 把它移出扫描窗口。待办本身不动,
    /// 人工照办。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_permanently_failing_task_is_retired_after_repeated_failures()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-timeout-giveup-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-timeout-giveup-a");
        // 转办目标 = 当前办理人自己 → ReassignTaskOpBase 抛 48010,是运行期永久失败形态。
        var definitionId = await Publish(admin, "超时-永久失败的转办",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "transfer", transferUserId = aId }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));

        List<string> log = [];
        for (var tick = 0; tick < 5; tick++)
        {
            log = await RunTimeoutJob(f);
            Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        }

        Assert.Contains(log, l => l.Contains($"待办 {taskId}") && l.Contains("已退出扫描窗口"));
        Assert.Null(await DueTimeOf(f, taskId));
        // 下一拍不再空转 —— 这才是「不再永久占着批量名额」的可观测出口。
        Assert.Equal(["超时扫描:无到期待办。"], await RunTimeoutJob(f));
        // 待办没被吞掉:办理人照样能自己办。
        Assert.Single(await TodoItemsFor(await ClientFor(f, "wf-timeout-giveup-a"), instanceId));
    }

    /// <summary>
    /// 领取 CAS(设计规划 §14.1):人工动作先动了这件待办,超时就必须领不到。
    /// <para>单线程套件里没法在 Job 内部插入并发,所以直接把「扫描时读到的版本号」这个入参喂成过期值
    /// ——这正是真实竞态在 <c>ClaimDueTaskAsync</c> 眼里的形状:先扫描(读 <c>Version</c>)、人工动作提交
    /// (<c>Version</c> 前进)、再派命令。</para>
    /// <para>断言「零额外 <c>wf_his_task</c> 行」而不只是「抛了 48007」:去掉 CAS 的
    /// <c>Version == @expected</c> 半句后领取会成功,超时会替**转办后的新办理人**再批一次。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_fire_loses_to_human_action_by_version_cas()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-timeout-cas-starter");
        var aId = await AddUser(admin, "wf-timeout-cas-a");
        var bId = await AddUser(admin, "wf-timeout-cas-b");
        var definitionId = await Publish(admin, "超时-输给人工动作",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-timeout-cas-starter");
        var a = await ClientFor(f, "wf-timeout-cas-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        var scannedVersion = await VersionOf(f, taskId);

        // 人工动作胜出:转办把 Version 推到 scannedVersion + 1(转办不清 DueTime,故待办仍「到期」)。
        var transferred = await PostEnvelope(a, "/api/v1/workflow/task/transfer", new { taskId, toUserId = bId });
        Assert.Equal(0, transferred.GetProperty("code").GetInt32());

        using var scope = f.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var conflict = await Assert.ThrowsAsync<AdminException>(() => engine.ExecuteAsync(new TimeoutFireCmd
        {
            TaskId = taskId,
            ExpectedVersion = scannedVersion,
            Action = WfTimeoutAction.AutoPass,
            Comment = "超时自动通过(系统触发)",
        }));
        Assert.Equal(WorkflowErrorCode.TaskConflict, (int)conflict.Code);

        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, instanceId));
        Assert.Equal(1, await HisTaskCount(f, instanceId));
        Assert.Equal(0, await HisTaskCount(f, instanceId, WfTaskAction.Approve));
        Assert.Equal(0, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));
    }

    /// <summary>
    /// 批量上限:一拍只处理 <see cref="WorkflowOptions.TimeoutScanBatchSize"/> 条,剩下的下一拍继续
    /// (按 <c>DueTime</c> 升序 → 最久的先处理,不会饿死)。
    /// </summary>
    [Fact]
    public async Task Timeout_scan_respects_batch_size()
    {
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton(
                new WorkflowOptions { TimeoutScanBatchSize = 1 })),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-timeout-batch-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-timeout-batch-a");
        var definitionId = await Publish(admin, "超时-批量上限",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var first = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, first.GetProperty("code").GetInt32());
        var firstId = first.GetProperty("data").GetProperty("instanceId").GetInt64();

        var second = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        var secondId = second.GetProperty("data").GetProperty("instanceId").GetInt64();

        await MakeDue(f, firstId, TimeSpan.FromDays(2));
        await MakeDue(f, secondId, TimeSpan.FromDays(1));
        // 命中数就是批量上限本身的可观测出口:忽略 Take 时这里会写 2。
        Assert.Equal(
            ["超时扫描:命中 1,提醒 0,自动通过 1,自动拒绝 0,转办 0,跳过 0,失败 0。"],
            await RunTimeoutJob(f));

        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, firstId));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, secondId));

        // 下一拍轮到第二条。
        await RunTimeoutJob(f);
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, secondId));
    }

    /// <summary>
    /// <b>队头不得被「永不消费的行」永久占死。</b>提醒路径从不清 <c>DueTime</c>(那是「不改状态」契约的
    /// 正确推论),所以已提醒过的待办每一拍都照样落在到期窗口里。批量若按「取回行数」计,升序 +
    /// 永不消费 = 队头永久堵塞:逾期提醒堆到 <c>TimeoutScanBatchSize</c> 之后,更新的自动通过/拒绝/转办
    /// **永远排不进队**,而 Job 返回 Success、不告警,唯一征兆是日志里「命中 N,…,跳过 N」。
    /// <para><see cref="WfTimeoutAction.Remind"/> 是 <see cref="WfTimeoutAction"/> 的枚举默认值(0),
    /// 「只配了 action 没细想」的节点全是它,所以这不是边缘情况。</para>
    /// <para><b>两条提醒的节奏取 24 小时、事件行只推回 2 小时前是有意的</b>:让它们「仍在到期窗口里、
    /// 这一拍照样推不动」,任何「按固定下限把最近提醒过的行排除掉」的省事修法都救不了这一场——只有
    /// 「推不动的行不占处理预算、扫描继续往后翻页」才过得去。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_throttled_reminds_do_not_starve_a_newly_due_task()
    {
        using var f = new WorkflowAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton(
                new WorkflowOptions { TimeoutScanBatchSize = 2 })),
        };
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-timeout-starve-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-timeout-starve-a");
        var remindDefinitionId = await Publish(admin, "超时-饿死-提醒",
            SingleApprovalModel([aId], timeout: new { hours = 24, action = "remind" }));
        var passDefinitionId = await Publish(admin, "超时-饿死-自动通过",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, starterAccount);

        var remindInstanceIds = new List<long>();
        for (var i = 0; i < 2; i++)
        {
            var started = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
                new { definitionId = remindDefinitionId });
            Assert.Equal(0, started.GetProperty("code").GetInt32());
            remindInstanceIds.Add(started.GetProperty("data").GetProperty("instanceId").GetInt64());
        }

        var pass = await PostEnvelope(starter, "/api/v1/workflow/instance/start",
            new { definitionId = passDefinitionId });
        Assert.Equal(0, pass.GetProperty("code").GetInt32());
        var passInstanceId = pass.GetProperty("data").GetProperty("instanceId").GetInt64();

        // 两条提醒逾期更久 → 升序排在队头,正好占满 BatchSize = 2。
        // 建单时刻一并推回(节点 24 小时超时 → 4 天前建的单子 3 天前到期),否则「事件早于待办」这种
        // 物理上不可能的组合会被防刷判据里的 CreateTime 下界当成「本待办还没提醒过」。
        foreach (var id in remindInstanceIds)
        {
            await BackdateTaskCreation(f, id, TimeSpan.FromDays(4));
            await MakeDue(f, id, TimeSpan.FromDays(3));
        }

        await MakeDue(f, passInstanceId, TimeSpan.FromMinutes(10));

        // 第一拍:两条提醒把预算用光,自动通过排不上——这是**正确**的批量上限行为,不是缺陷。
        Assert.Equal(
            ["超时扫描:命中 2,提醒 2,自动通过 0,自动拒绝 0,转办 0,跳过 0,失败 0。"],
            await RunTimeoutJob(f));
        Assert.Equal(WfInstanceStatus.Running, await InstanceStatus(f, passInstanceId));

        foreach (var id in remindInstanceIds)
            await BackdateTimeoutEvents(f, id, TimeSpan.FromHours(2));

        // 第二拍:两条提醒被防刷挡下(只检视、不占预算),扫描继续往后翻到自动通过那条。
        Assert.Equal(
            ["超时扫描:命中 3,提醒 0,自动通过 1,自动拒绝 0,转办 0,跳过 2,失败 0。"],
            await RunTimeoutJob(f));
        Assert.Equal(WfInstanceStatus.Approved, await InstanceStatus(f, passInstanceId));
    }

    /// <summary>
    /// 建任务之后节点的 <c>timeout</c> 被改掉(设计器上把超时关了)→ 这行的 <c>DueTime</c> 是按旧配置算的
    /// 过期数据,必须**退出扫描窗口**,否则它每一拍都被扫到、每一拍都只能跳过,永久占着批量名额。
    /// <para><b>退出不等于静默清 <c>DueTime</c></b>(陷阱记录第 3 条担心的正是那个):清之前先落一条
    /// <see cref="WfHistoryEventType.TimeoutFired"/>(<c>action = "retired"</c> + 原因),清之后再打一行
    /// 带 <c>taskId</c> 的日志。两个出口都断言上。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_task_whose_node_dropped_timeout_leaves_the_scan_window()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        var starterAccount = "wf-timeout-retire-starter";
        await AddUser(admin, starterAccount);
        var aId = await AddUser(admin, "wf-timeout-retire-a");
        var definitionId = await Publish(admin, "超时-配置事后被改掉",
            SingleApprovalModel([aId], timeout: new { hours = 1, action = "remind" }));

        var starter = await ClientFor(f, starterAccount);

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        // 直接篡改已发布快照的 ModelJson(照 WfReturnResubmitTests 的先例):节点不再配 timeout。
        await ReplacePublishedModel(f, instanceId, SingleApprovalModel([aId]));

        var log = await RunTimeoutJob(f);

        Assert.Null(await DueTimeOf(f, taskId));
        Assert.Equal(2, log.Count);
        Assert.Contains($"待办 {taskId}", log[0]);
        Assert.Contains("timeoutNotConfigured", log[0]);
        Assert.Equal(
            "超时扫描:命中 1,提醒 0,自动通过 0,自动拒绝 0,转办 0,跳过 1,失败 0。",
            log[1]);
        Assert.Equal(1, await HistoryCount(f, instanceId, WfHistoryEventType.TimeoutFired));

        // 下一拍压根扫不到它了(否则它就是那种「永远占着名额」的死行)。
        Assert.Equal(["超时扫描:无到期待办。"], await RunTimeoutJob(f));
        // 待办本身一字未动,人工照办。
        Assert.Single(await TodoItemsFor(await ClientFor(f, "wf-timeout-retire-a"), instanceId));
    }

    /// <summary>
    /// 防刷的去重键是 <c>(InstanceId, NodeId)</c>、**不带 <c>TaskId</c>**(一个 token 在一个节点上只有一件
    /// 待办,所以定位是够的)。但向后跳转会让同一个节点被重新进入并建**新的**待办,此时上一轮留下的
    /// <see cref="WfHistoryEventType.TimeoutFired"/> 仍然命中这个键 —— 只要
    /// <see cref="WorkflowOptions.TimeoutRemindMinIntervalHours"/> 配得比节点自己的 <c>hours</c> 大,
    /// **重入后的第一次提醒就会被上一轮的事件行静默挡掉**。判据加一句「事件不早于本待办的
    /// <c>CreateTime</c>」即可,本用例是它的钉子。
    /// </summary>
    [Fact]
    public async Task Timeout_remind_fires_again_after_node_is_re_entered_by_reject_routing()
    {
        var capturing = new WorkflowNotifierTests.CapturingWorkflowNotifier();
        using var f = new WorkflowAppFactory
        {
            Overrides = s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IWorkflowNotifier>(capturing));
                // 间隔(48h)配得比节点 hours(1h)大 —— 这正是缺陷可观测的唯一条件。
                s.Replace(ServiceDescriptor.Singleton(
                    new WorkflowOptions { TimeoutRemindMinIntervalHours = 48 }));
            },
        };
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-remind-reenter-starter");
        var aId = await AddUser(admin, "wf-remind-reenter-a");
        var bId = await AddUser(admin, "wf-remind-reenter-b");
        var definitionId = await Publish(admin, "超时-重入后仍要提醒",
            RejectRouteModel(aId, bId, node1Timeout: new { hours = 1, action = "remind" }));

        var starter = await ClientFor(f, "wf-remind-reenter-starter");
        var a = await ClientFor(f, "wf-remind-reenter-a");

        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var firstTaskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // ① node1 第一轮:到期 → 提醒一次。
        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);
        Assert.Single(capturing.TaskUrgedCalls);

        // 把那条提醒事件推回 2 小时前:仍远在 48 小时间隔之内(所以「上一轮的事件」照样有资格挡人),
        // 又确保它严格早于**重入后新建的**待办,免得毫秒/整秒取整让两个时刻撞在一起。
        await BackdateTimeoutEvents(f, instanceId, TimeSpan.FromHours(2));

        // ② A 批过 node1 → B 在 node2 超时自动拒绝 → 拒绝路由回 node1 → A 拿到**新的** node1 待办。
        var approve = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId = firstTaskId });
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        var reentered = Assert.Single(await TodoItemsFor(a, instanceId));
        Assert.Equal("node1", reentered.GetProperty("nodeId").GetString());
        Assert.NotEqual(firstTaskId, reentered.GetProperty("taskId").GetInt64());

        // ③ 重入后的新待办到期 → 必须**重新**提醒(上一轮那条事件不该挡住它)。
        await MakeDue(f, instanceId, TimeSpan.FromDays(1));
        await RunTimeoutJob(f);

        Assert.Equal(2, capturing.TaskUrgedCalls.Count);
        Assert.Equal(reentered.GetProperty("taskId").GetInt64(), capturing.TaskUrgedCalls[^1].TaskId);
    }

    // ── 六、交付链:种子那一行真的会被调度器跑起来 ───────────────────

    /// <summary>
    /// <b>本文件其余用例全部绕开了调度器</b>——<c>RunTimeoutJob</c> 走
    /// <c>GetServices&lt;IAdminJob&gt;().OfType&lt;WfTimeoutJob&gt;().Single()</c>,不经
    /// <c>IJobHandlerResolver</c> 的按 <c>Name</c> Ordinal 匹配。于是「种子那一行是否真能被调度器跑起来」
    /// 这条交付链零覆盖:<c>Status</c> 不是 <c>Ready</c> / <c>HandlerName</c> 解析不到实例 /
    /// cron 不是调度器认的归一化 6 段,任何一处配错都是**全绿 + build 全绿,而消费者装包上线后超时永不
    /// 触发且无报错**——本任务唯一「全绿但零交付」的失败形态。
    /// <para>三条断言各对一处:<c>Status == Ready</c>(调度器只派发 Ready 行)、
    /// <c>ResolveAsync(HandlerName)</c> 拿到 <see cref="WfTimeoutJob"/>(将来重构挪了类型全名而忘了同步
    /// 种子,这里当场红)、<c>JobTrigger.ComputeNext</c> 非空(cron 真能被调度器算出下一次时刻)。
    /// 不启调度器、不拨钟,零 flake。</para>
    /// <para><b>第三条的射程要说清:它只钉「cron 能被解析」,不钉「是归一化 6 段」。</b>实测把种子改成
    /// 5 段的 <c>*/5 * * * *</c> 本用例仍绿(解析器自己认 5 段),改成 <c>every 5 minutes</c> 才红
    /// (<c>Assert.NotNull</c> 失败)。段数归一化归 <c>JobService</c> 的入库校验管,不在本条射程内。</para>
    /// </summary>
    [Fact]
    public async Task Timeout_scan_job_seed_row_is_ready_and_resolvable_by_the_scheduler()
    {
        using var f = new WorkflowAppFactory();
        using var _ = f.CreateClient();   // 触发宿主启动 → CodeFirst + 种子落库
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var row = await db.Queryable<TenonAdmin.Services.SysJob>()
            .Where(j => j.Code == "wf-timeout-scan")
            .FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(TenonAdmin.Services.JobStatus.Ready, row.Status);

        var resolver = scope.ServiceProvider.GetRequiredService<IJobHandlerResolver>();
        var handler = await resolver.ResolveAsync(row.HandlerName, scope.ServiceProvider);
        Assert.IsType<WfTimeoutJob>(handler);

        Assert.NotNull(TenonAdmin.Services.JobTrigger.ComputeNext(row, DateTime.Now));
    }

    // ── 脚手架 ─────────────────────────────────────────────────────────

    /// <summary>start → node1(<paramref name="mode"/>,<paramref name="userIds"/>,可选 timeout) → null。</summary>
    private static object SingleApprovalModel(long[] userIds, string mode = "any", object? timeout = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = userIds },
            },
            ["mode"] = mode,
        };
        if (timeout is not null)
            props["timeout"] = timeout;

        return new
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
                    props,
                    next = (object?)null,
                },
            },
        };
    }

    /// <summary>start → node1(any,[A],可选 timeout) → node2(any,[B],<c>autoReject</c> + <c>onReject→node1</c>) → null。</summary>
    private static object RejectRouteModel(long aUserId, long bUserId, object? node1Timeout = null)
    {
        var node1Props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = new[] { aUserId } },
            },
            ["mode"] = "any",
        };
        if (node1Timeout is not null)
            node1Props["timeout"] = node1Timeout;

        return new
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
                    props = node1Props,
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
                            @params = new Dictionary<string, object> { ["userIds"] = new[] { bUserId } },
                        },
                        mode = "any",
                        onReject = "toNode",
                        rejectToNodeId = "node1",
                        timeout = new { hours = 1, action = "autoReject" },
                    },
                    next = (object?)null,
                },
                },
            },
        };
    }

    /// <summary>
    /// 手动触发一次超时扫描——不启调度器(<c>skills/create-job.md</c> 第五节的官方姿势:处理器就是个
    /// 普通 Scoped 服务,<see cref="JobExecutionContext"/> 是可 <c>new</c> 的快照)。
    /// </summary>
    /// <returns>本次扫描写进执行记录的日志行(命中/各分流计数,是扫描形状的唯一可观测出口)。</returns>
    private static async Task<List<string>> RunTimeoutJob(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IAdminJob>().OfType<WfTimeoutJob>().Single();
        var now = DateTime.Now;
        var log = new List<string>();
        await job.ExecuteAsync(
            new JobExecutionContext
            {
                JobId = 1,
                JobCode = "wf-timeout-scan",
                JobName = "流程超时扫描",
                FireInstanceId = 1,
                ScheduledTime = now,
                FireTime = now,
                Log = log.Add,
            },
            CancellationToken.None);
        return log;
    }

    /// <summary>把某实例的活跃待办的 <c>DueTime</c> 推到过去,造出「已到期」。</summary>
    private static async Task MakeDue(WorkflowAppFactory f, long instanceId, TimeSpan ago)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var past = DateTime.Now - ago;
        var affected = await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { DueTime = past })
            .Where(t => t.InstanceId == instanceId)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有活跃待办可推到期——测试前置条件坏了。");
    }

    /// <summary>把某实例活跃待办的 <c>CreateTime</c> 推到过去,造出「很久以前建的单子」。</summary>
    private static async Task BackdateTaskCreation(WorkflowAppFactory f, long instanceId, TimeSpan ago)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var past = DateTime.Now - ago;
        var affected = await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { CreateTime = past })
            .Where(t => t.InstanceId == instanceId)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有活跃待办可改建单时刻——测试前置条件坏了。");
    }

    /// <summary>
    /// 把某实例上已写下的 <see cref="WfHistoryEventType.TimeoutFired"/> 事件推到过去,造出「上次提醒是
    /// 多久以前」。<c>CreateTime</c> 先算进局部变量再进 <c>SetColumns</c>——内联 <c>DateTime</c> 表达式会被
    /// SqlSugar 按当前区域设置格式化成字面量拼进 SQL(台账陷阱记录有实测)。
    /// </summary>
    private static async Task BackdateTimeoutEvents(WorkflowAppFactory f, long instanceId, TimeSpan ago)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var past = DateTime.Now - ago;
        var affected = await db.Updateable<WfHistory>()
            .SetColumns(h => new WfHistory { CreateTime = past })
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.TimeoutFired)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有 TimeoutFired 事件可推回——测试前置条件坏了。");
    }

    /// <summary>直接替换实例所用已发布版本的 <c>ModelJson</c>,模拟「建任务之后节点配置被改掉」。</summary>
    private static async Task ReplacePublishedModel(WorkflowAppFactory f, long instanceId, object model)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<TenonAdmin.SqlSugar.IOrgScoped>()
            .Where(i => i.Id == instanceId)
            .FirstAsync();
        Assert.NotNull(instance);
        var json = JsonSerializer.Serialize(model);
        var affected = await db.Updateable<WfDefinitionVersion>()
            .SetColumns(v => new WfDefinitionVersion { ModelJson = json })
            .Where(v => v.Id == instance.DefinitionVersionId)
            .ExecuteCommandAsync();
        Assert.True(affected > 0, "没有已发布版本可篡改——测试前置条件坏了。");
    }

    private static async Task<DateTime?> DueTimeOf(WorkflowAppFactory f, long taskId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var task = await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync();
        Assert.NotNull(task);
        return task.DueTime;
    }

    private static async Task<int> VersionOf(WorkflowAppFactory f, long taskId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var task = await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync();
        Assert.NotNull(task);
        return task.Version;
    }

    /// <summary>实例级乐观锁版本(Task 9);没有 HTTP 出口,只能读库。</summary>
    private static async Task<int> InstanceVersionOf(WorkflowAppFactory f, long instanceId)
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

    /// <summary>token 级乐观锁版本(Task 9);M2b 单 token,故按实例定位。</summary>
    private static async Task<int> TokenVersionOf(WorkflowAppFactory f, long instanceId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var tokens = await db.Queryable<WfToken>().Where(t => t.InstanceId == instanceId).ToListAsync();
        return Assert.Single(tokens).Version;
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

    private static async Task<int> HisTaskCount(WorkflowAppFactory f, long instanceId, WfTaskAction? action = null)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<WfHisTask>()
            .Where(h => h.InstanceId == instanceId)
            .WhereIF(action is not null, h => h.Action == action!.Value)
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

    private static async Task<List<JsonElement>> TodoItemsFor(HttpClient client, long instanceId)
    {
        var todo = await GetEnvelope(client, "/api/v1/workflow/task/todo?Current=1&Size=20");
        return todo.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("instanceId").GetInt64() == instanceId)
            .ToList();
    }

    private static async Task<JsonElement> GetEnvelope(HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadEnvelope();

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
