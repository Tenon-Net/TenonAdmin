using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 超时扫描:按 <c>wf_task.DueTime</c> 找到期待办,按节点 <see cref="WfTimeout.Action"/> 分流——
/// <see cref="WfTimeoutAction.Remind"/> 只推送不改状态(本类直接处理),其余三种派一条
/// <see cref="TimeoutFireCmd"/> 交引擎在单一事务里领取 + 动作 + 写
/// <see cref="WfHistoryEventType.TimeoutFired"/>。
/// <para><b>多副本安全不在本类</b>:内核调度器的选主 + 每次触发对 <c>sys_job.NextRunTime</c> 的领取 CAS
/// 已经保证「同一时刻至多一个副本在跑本任务」(ADR-0004),故这里不建 worker/lease。设计规划 §14.1
/// 那个 <c>wf_task</c> 级 CAS 解决的是**另一个**竞争:本任务与**人工动作**抢同一件待办。</para>
/// <para><b>失败隔离刻意偏离「异常直接抛」那条任务规矩</b>:一条待办的业务失败(如某节点配错了
/// <see cref="WfTimeout.TransferUserId"/>)若能把整个 Job 打成 Failed,重试 → 再失败 → 连败到阈值转
/// Panic,**全库所有超时策略就此停摆**。单条业务失败是数据/配置问题,不是任务失败;它通过
/// <see cref="JobExecutionContext.Log"/> 的失败计数 + 每条失败一行带 <c>taskId</c> 与错误码的日志可见,
/// 连败到 <see cref="MaxTaskFailures"/> 拍还会被移出扫描窗口。取消与基础设施异常仍必须抛,否则超时旋钮与
/// 停机排水都失效。</para>
/// <para><b>覆写本类的方法不足以换掉行为,必须同时改 <c>sys_job</c> 那一行。</b>
/// <c>TryAddEnumerable</c> 按**实现类型**去重,故消费者注册 <c>MyTimeoutJob : WfTimeoutJob</c> 是**新增**
/// 一个 <see cref="IAdminJob"/> 而非替换;而 <c>DefaultJobHandlerResolver</c> 按 <see cref="IAdminJob.Name"/>
/// (默认 = 类型全名)Ordinal 匹配 <c>sys_job.HandlerName</c>,种子写死的是本基类的全名 → 调度器永远选中
/// 基类,子类的覆写一次都不会执行(编译过、DI 过、日志正常,静默失效)。两条出路二选一:
/// ①把 <c>sys_job</c> 里 <c>Code = wf-timeout-scan</c> 那行的 <c>HandlerName</c> 改成子类全名——该行
/// <c>IsSystem = false</c>,后台「任务调度」页面可直接改,无需发版;②在子类里
/// <c>public override string Name => typeof(WfTimeoutJob).FullName!;</c> 并在
/// <c>AddTenonAdminWorkflow</c> **之前**注册子类(同名两个实例时解析器取先注册的那个)。</para>
/// <para>光注册本类不会让它跑起来——调度器只派发 <c>sys_job</c> 表里 <c>Status = Ready</c> 的行,
/// 那一行由 <see cref="WfTimeoutJobSeed"/> 预置。</para>
/// </summary>
public class WfTimeoutJob(
    ISqlSugarClient db,
    IWorkflowEngine engine,
    IWorkflowNotifier notifier,
    WorkflowOptions options,
    TimeProvider time) : IAdminJob
{
    /// <summary>
    /// 一拍最多**检视**多少行 = <see cref="WorkflowOptions.TimeoutScanBatchSize"/> × 本倍数。
    /// 检视 ≠ 处理:被防刷挡下的提醒行只花一次索引查询,不占处理预算,扫描会继续往后翻页,
    /// 好让新到期的自动通过/拒绝/转办在同一拍排得进队。倍数是这趟翻页的天花板,防止一拍无限翻。
    /// </summary>
    protected virtual int MaxScanRounds => 5;

    /// <summary>
    /// 同一件待办累计失败到本阈值就退出扫描窗口(清 <c>DueTime</c> + 留一条带错误码的事件行)。
    /// 永久失败形态(转办目标事后被停用 / 事后成了本待办 actor)每拍失败一次且**永远**如此,
    /// 而 Job 返回 Success、<c>AlertByNotice</c> 只对 Job 级 Failed 生效 → 不设阈值就是永不告警地空转,
    /// 并且把批量名额一直占着。
    /// </summary>
    protected virtual int MaxTaskFailures => 5;

    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = time.GetLocalNow().DateTime;
        var budget = options.TimeoutScanBatchSize > 0 ? options.TimeoutScanBatchSize : 1;
        var scanCap = budget * Math.Max(1, MaxScanRounds);

        var modelCache = new Dictionary<long, WfModelIndex?>();
        int examined = 0, acted = 0;
        int reminded = 0, passed = 0, rejected = 0, transferred = 0, skipped = 0, failed = 0;
        DateTime? cursorDueTime = null;
        var cursorTaskId = 0L;

        while (acted < budget && examined < scanCap)
        {
            var take = Math.Min(budget, scanCap - examined);
            var page = await ScanDueTasksAsync(now, cursorDueTime, cursorTaskId, take, cancellationToken);
            if (page.Count == 0)
                break;

            var instanceIds = page.Select(t => t.InstanceId).Distinct().ToList();
            var instanceMap = (await db.Queryable<WfInstance>()
                    .ClearFilter<IOrgScoped>()
                    .Where(i => instanceIds.Contains(i.Id))
                    .ToListAsync())
                .ToDictionary(i => i.Id);
            var versionIds = instanceMap.Values.Select(i => i.DefinitionVersionId).Distinct().ToList();
            var versionMap = (await db.Queryable<WfDefinitionVersion>()
                    .Where(v => versionIds.Contains(v.Id))
                    .ToListAsync())
                .ToDictionary(v => v.Id);

            foreach (var task in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;
                cursorDueTime = task.DueTime;
                cursorTaskId = task.Id;

                try
                {
                    instanceMap.TryGetValue(task.InstanceId, out var instance);
                    var timeout = instance is null
                        ? null
                        : ResolveTimeout(instance.DefinitionVersionId, task.NodeId, versionMap, modelCache);

                    var deadReason = ResolveDeadReason(instance, timeout);
                    if (deadReason is not null)
                    {
                        await RetireTaskAsync(task, deadReason, error: null, cancellationToken);
                        skipped++;
                        context.Log?.Invoke(
                            $"超时扫描:待办 {task.Id}(实例 {task.InstanceId} 节点 {task.NodeId})已退出扫描窗口" +
                            $"({deadReason}),DueTime 置空。");
                        continue;
                    }

                    if (timeout!.Action == WfTimeoutAction.Remind)
                    {
                        if (await HandleRemindAsync(task, instance!, timeout, now, cancellationToken))
                        {
                            reminded++;
                            acted++;
                        }
                        else
                        {
                            // 防刷挡下的提醒**不占处理预算**:它是「这一拍推不动」的行,占了名额就是
                            // 队头永久堵塞(Remind 是 WfTimeoutAction 的枚举默认值,这类行天然最多)。
                            skipped++;
                        }
                    }
                    else
                    {
                        await FireAsync(task, timeout, cancellationToken);
                        switch (timeout.Action)
                        {
                            case WfTimeoutAction.AutoPass: passed++; break;
                            case WfTimeoutAction.AutoReject: rejected++; break;
                            default: transferred++; break;
                        }

                        acted++;
                    }
                }
                catch (AdminException ex)
                {
                    // 单条业务失败(CAS 输给人工动作 / 转办目标配错 / 实例已完结)不拖垮整批。
                    failed++;
                    acted++;
                    var retired = await HandleFailureAsync(task, ex, cancellationToken);
                    context.Log?.Invoke(
                        $"超时扫描:待办 {task.Id}(实例 {task.InstanceId} 节点 {task.NodeId})处理失败," +
                        $"错误码 {(int)ex.Code}{(retired ? ",连续失败达阈值,已退出扫描窗口" : "")}。");
                }

                if (acted >= budget)
                    break;
            }

            if (page.Count < take)
                break;
        }

        if (examined == 0)
        {
            context.Log?.Invoke("超时扫描:无到期待办。");
            return;
        }

        context.Log?.Invoke(
            $"超时扫描:命中 {examined},提醒 {reminded},自动通过 {passed},自动拒绝 {rejected}," +
            $"转办 {transferred},跳过 {skipped},失败 {failed}。");
    }

    /// <summary>
    /// 扫一页到期待办:<c>DueTime</c> 非空且不晚于 <paramref name="now"/>,按 <c>(DueTime, Id)</c> 升序
    /// 取 <paramref name="take"/> 条,游标之后的行才算(<c>null</c> 游标 = 从头)。命中 M1 就建好的索引
    /// <c>idx_wf_task_due</c>。
    /// <para><b>为什么是游标翻页而不是一次 <c>Take(BatchSize)</c></b>:升序 + 「这一拍推不动的行原样留在
    /// 窗口里」= 队头永久堵塞。已提醒过的待办不清 <c>DueTime</c>(那是「提醒不改状态」契约的正确推论),
    /// 一次性取 <c>BatchSize</c> 条时它们把名额占满,更新的自动通过/拒绝/转办**永远排不进队**,而 Job 照样
    /// 返回 Success、不告警。调用方因此按「处理预算」而不是「取回行数」计数:推不动的行只被检视、
    /// 不占预算,扫描继续往后翻,天花板是 <see cref="MaxScanRounds"/>。</para>
    /// <para>不 JOIN 实例/token:<c>wf_task</c> 行只在待办活着时存在(完成/撤销/退回都物理删),
    /// 「实例已完结但待办还在」不该发生;真发生了由 <see cref="ResolveDeadReason"/> 判死并退出窗口。</para>
    /// </summary>
    protected virtual async Task<List<WfTask>> ScanDueTasksAsync(
        DateTime now,
        DateTime? afterDueTime,
        long afterTaskId,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = afterDueTime ?? default;
        var hasCursor = afterDueTime is not null;
        return await db.Queryable<WfTask>()
            .Where(t => t.DueTime != null && t.DueTime <= now)
            .WhereIF(hasCursor, t => t.DueTime > cursor || (t.DueTime == cursor && t.Id > afterTaskId))
            .OrderBy(t => t.DueTime, OrderByType.Asc)
            .OrderBy(t => t.Id, OrderByType.Asc)
            .Take(take > 0 ? take : 1)
            .ToListAsync();
    }

    /// <summary>
    /// 这一行是不是「永远推不动」的死行?是则返回原因码(进事件 payload 与日志),否则 <c>null</c>。
    /// <list type="bullet">
    /// <item><c>instanceMissing</c> —— 实例行查不到(不该发生;真发生了这条待办永远无人处理)。</item>
    /// <item><c>instanceNotRunning</c> —— 实例已完结的残留待办。此前这类行会一路走到
    /// <c>BeginTimeoutAsync</c> 抛 <c>InstanceStatusConflict</c> 被计成失败,而 <c>DueTime</c> 原样留下,
    /// 每拍重来一次。</item>
    /// <item><c>timeoutNotConfigured</c> —— 建任务之后节点配置被改掉了(<c>DueTime</c> 是按旧配置算的)。
    /// 节点现在明说「不设超时」,那这行的 <c>DueTime</c> 就是过期数据,清掉它是恢复一致而不是猜用户意图。</item>
    /// </list>
    /// </summary>
    protected virtual string? ResolveDeadReason(WfInstance? instance, WfTimeout? timeout) => instance switch
    {
        null => "instanceMissing",
        { Status: not WfInstanceStatus.Running } => "instanceNotRunning",
        _ when timeout is null || timeout.Hours <= 0 => "timeoutNotConfigured",
        _ => null,
    };

    /// <summary>
    /// 把一件永远推不动的待办移出扫描窗口:先写一条
    /// <see cref="WfHistoryEventType.TimeoutFired"/>(<c>action = "retired"</c> + 原因/错误码),再清
    /// <c>DueTime</c>。
    /// <para><b>顺序与「不要静默清 DueTime」</b>:陷阱记录第 3 条担心的是「失败了就把 <c>DueTime</c> 清掉」
    /// 这种静默吃掉配置错误的做法。这里清之前先落一行可查的事件、清之后再打一行带 <c>taskId</c> 的日志,
    /// 两个出口都在;不清的代价是这行**永久**占着批量名额,把新到期的待办饿死。</para>
    /// <para>清空带 <c>Version</c> 条件:扫描到现在若有人工动作动过这件待办,本次判死的依据(节点配置 /
    /// 实例状态)可能已不成立,让下一拍重新判。</para>
    /// </summary>
    protected virtual async Task RetireTaskAsync(
        WfTask task,
        string reason,
        int? error,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await db.Insertable(new WfHistory
        {
            InstanceId = task.InstanceId,
            EventType = WfHistoryEventType.TimeoutFired,
            NodeId = task.NodeId,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    taskId = task.Id,
                    action = "retired",
                    reason,
                    error,
                    dueTime = task.DueTime,
                },
                WfModelJson.Options),
        }).ExecuteCommandAsync();

        await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { DueTime = null })
            .Where(t => t.Id == task.Id && t.Version == task.Version)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 一条待办处理失败后的留痕与升级。返回 <c>true</c> = 已判定为永久失败并移出扫描窗口。
    /// <para><b>为什么要在事务外补一行事件</b>:引擎是「一条 Cmd 一个事务」,失败会把本该同事务落库的
    /// <c>TimeoutFired</c> 一起回滚 → 永久失败在数据层面**完全不可见**,唯一征兆是日志里一个计数。
    /// 这行 <c>action = "failed"</c> 的事件带 <c>taskId</c> 与错误码,顺带成为失败次数的存储。</para>
    /// <para>次数按「本 <c>(实例, 节点)</c> 上不早于本待办 <c>CreateTime</c> 的 <c>TimeoutFired</c> 行数」
    /// 近似:一件还留在扫描窗口里的待办若已经写过多条超时事件,只可能是反复失败——唯一的例外是顺序会签
    /// 的逐位级联(那条路上每位办理人各留一行),对它而言本阈值等于「级联超过 <see cref="MaxTaskFailures"/>
    /// 位之后再失败一次就放弃」,属可接受的近似。</para>
    /// </summary>
    protected virtual async Task<bool> HandleFailureAsync(
        WfTask task,
        AdminException error,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var code = (int)error.Code;
        await db.Insertable(new WfHistory
        {
            InstanceId = task.InstanceId,
            EventType = WfHistoryEventType.TimeoutFired,
            NodeId = task.NodeId,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    taskId = task.Id,
                    action = "failed",
                    error = code,
                    dueTime = task.DueTime,
                },
                WfModelJson.Options),
        }).ExecuteCommandAsync();

        var attempts = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == task.InstanceId
                        && h.NodeId == task.NodeId
                        && h.EventType == WfHistoryEventType.TimeoutFired
                        && h.CreateTime >= task.CreateTime)
            .CountAsync();
        if (attempts < MaxTaskFailures)
            return false;

        await RetireTaskAsync(task, "failureThreshold", code, cancellationToken);
        return true;
    }

    /// <summary>
    /// 按 <see cref="WfModelIndex"/>(含分支臂内节点)取节点的超时配置;每个未命中的
    /// definitionVersionId 只建一次索引(照 <c>WfTaskService.ResolveNodeNameCached</c> 的先例)。
    /// </summary>
    protected virtual WfTimeout? ResolveTimeout(
        long definitionVersionId,
        string nodeId,
        IReadOnlyDictionary<long, WfDefinitionVersion> versionMap,
        Dictionary<long, WfModelIndex?> modelCache)
    {
        if (!modelCache.TryGetValue(definitionVersionId, out var index))
        {
            var model = versionMap.TryGetValue(definitionVersionId, out var ver)
                ? WfModelJson.Deserialize(ver.ModelJson)
                : null;
            index = model is null ? null : WfModelIndex.Build(model);
            modelCache[definitionVersionId] = index;
        }

        return index?.Find(nodeId)?.Props?.Timeout;
    }

    /// <summary>
    /// 提醒:写一条 <see cref="WfHistoryEventType.TimeoutFired"/> + 对当前 Pending 办理人推一次
    /// <see cref="IWorkflowNotifier.TaskUrgedAsync"/>(<c>fromUserId = null</c> 即系统触发,该语义是
    /// Task 1 就在接口注释里留好的插头)。返回 <c>false</c> = 被防刷间隔挡下。
    /// <para><b>不做 <c>wf_task</c> 版本 CAS</b>(这是对设计规划 §14.1 第 1 条的精确化而非翻转):
    /// 那条 CAS 说的是「领取一件要动手的待办」,而提醒什么状态都不改。若给它也加版本 CAS,办理人正点
    /// 「同意」时本任务的提醒 CAS 可能先提交把 <c>Version</c> 推走,人工 CAS 落空 → 用户**为了一条提醒**
    /// 收到「待办已被他人处理」。竞态输了的后果只是「给一件刚办完的待办发了条提醒」——§14.1 第 2 条自己
    /// 写着「SignalR 只是刷新提示,<c>wf_task</c> 才是事实源」,正是它允许的失败形态。
    /// <b>可观测出口</b>:本方法**不得**改动 <c>wf_task.Version</c>,由
    /// <c>WfTimeoutTests.Timeout_remind_does_not_block_human_action</c> 的版本不变量断言钉住。</para>
    /// </summary>
    protected virtual async Task<bool> HandleRemindAsync(
        WfTask task,
        WfInstance instance,
        WfTimeout timeout,
        DateTime now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await ShouldRemindAsync(task, timeout, now, cancellationToken))
            return false;

        var toUserIds = await db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
            .Select(a => a.UserId)
            .ToListAsync();
        if (toUserIds.Count == 0)
            return false;

        await db.Insertable(new WfHistory
        {
            InstanceId = instance.Id,
            EventType = WfHistoryEventType.TimeoutFired,
            NodeId = task.NodeId,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    taskId = task.Id,
                    action = WfTimeoutAction.Remind.ToString(),
                    toUserIds,
                    dueTime = task.DueTime,
                },
                WfModelJson.Options),
        }).ExecuteCommandAsync();

        try
        {
            await notifier.TaskUrgedAsync(
                new WfNotifyContext
                {
                    InstanceId = instance.Id,
                    DefinitionVersionId = instance.DefinitionVersionId,
                    BusinessKey = instance.BusinessKey,
                    NodeId = task.NodeId,
                    StarterUserId = instance.StarterUserId,
                    Status = instance.Status,
                },
                task.Id,
                fromUserId: null,
                toUserIds,
                cancellationToken);
        }
        catch (Exception)
        {
            // 通知失败不得影响已写入的事件行(与 WfTaskService.UrgeAsync 同款约定)。
        }

        return true;
    }

    /// <summary>
    /// 防刷判据:本 <c>(实例, 节点)</c> 上**本待办建立之后**最近一条
    /// <see cref="WfHistoryEventType.TimeoutFired"/> 的 <c>CreateTime</c> 必须早于 <c>now - 最小间隔</c>。
    /// 用**我们本来就要写的那条事件**当「上次提醒时间」的存储,零新增列,命中现成索引
    /// <c>idx_wf_history_instance (InstanceId, CreateTime)</c>,不解 JSON、不比跨表雪花 Id;一个 token 在
    /// 一个节点上只有一件待办,故 <c>(InstanceId, NodeId)</c> 足以定位。
    /// <para><c>CreateTime &gt;= task.CreateTime</c> 那半句是必须的:向后跳转(拒绝路由 / 退回重提)会让
    /// 同一个节点被重新进入并建**新的**待办,而去重键不带 <c>TaskId</c>;缺这半句时,只要
    /// <see cref="WorkflowOptions.TimeoutRemindMinIntervalHours"/> 配得比节点 <c>Hours</c> 大,
    /// 重入后的第一次提醒就会被上一轮的事件行挡掉。</para>
    /// <para>间隔默认 = 该节点自己的 <see cref="WfTimeout.Hours"/>(下限 1 小时),可由
    /// <see cref="WorkflowOptions.TimeoutRemindMinIntervalHours"/> 覆盖。覆写本方法即可换成别的节奏,
    /// 「只提醒一次」就是它的第一个用例(判据改成「存在任一 TimeoutFired 即返回 false」)——但**覆写子类
    /// 之后必须同时改 <c>sys_job.HandlerName</c>**,否则调度器仍然选中基类,见本类类级说明。</para>
    /// </summary>
    protected virtual async Task<bool> ShouldRemindAsync(
        WfTask task,
        WfTimeout timeout,
        DateTime now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intervalHours = options.TimeoutRemindMinIntervalHours > 0
            ? options.TimeoutRemindMinIntervalHours
            : Math.Max(1, timeout.Hours);

        var last = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == task.InstanceId
                        && h.NodeId == task.NodeId
                        && h.EventType == WfHistoryEventType.TimeoutFired
                        && h.CreateTime >= task.CreateTime)
            .OrderBy(h => h.CreateTime, OrderByType.Desc)
            .Select(h => h.CreateTime)
            .FirstAsync();

        return last == default || last <= now.AddHours(-intervalHours);
    }

    /// <summary>
    /// 派一条 <see cref="TimeoutFireCmd"/>:领取 CAS、<c>TimeoutFired</c> 事件与等价的人工动作全在
    /// 引擎的单一事务里完成。<c>ExpectedVersion</c> 用扫描时读到的版本号——人工动作已经动过这件待办
    /// 就领不到,直接 <c>TaskConflict</c>(由调用方计成一次失败)。
    /// </summary>
    protected virtual async Task FireAsync(
        WfTask task,
        WfTimeout timeout,
        CancellationToken cancellationToken)
    {
        await engine.ExecuteAsync(
            new TimeoutFireCmd
            {
                TaskId = task.Id,
                ExpectedVersion = task.Version,
                Action = timeout.Action,
                TransferUserId = timeout.TransferUserId,
                Comment = ResolveComment(timeout.Action),
            },
            cancellationToken);
    }

    /// <summary>
    /// 落进 <c>wf_his_task.Comment</c> 的系统触发说明。超时动作以当前 Pending 办理人的身份记原生
    /// 动词,不读事件流的视图(如「已办列表」)光看「张三同意了」会误解,故一并写上这句。
    /// <para>与「错误只返数字码」不冲突——那条铁律管的是 <c>ErrorCode</c> 与前端 i18n;
    /// <c>Comment</c> 是业务数据列(用户填的审批意见就存在这里),内核自己也往数据列写中文系统文案
    /// (<c>SysJobLog.MessageText</c>/<c>ErrorText</c>)。要换成 key 覆写本方法即可,无 schema 变更。</para>
    /// </summary>
    protected virtual string ResolveComment(WfTimeoutAction action) => action switch
    {
        WfTimeoutAction.AutoPass => "超时自动通过(系统触发)",
        WfTimeoutAction.AutoReject => "超时自动拒绝(系统触发)",
        WfTimeoutAction.Transfer => "超时自动转办(系统触发)",
        _ => "超时触发(系统)",
    };
}
