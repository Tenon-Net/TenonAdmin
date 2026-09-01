using System.Globalization;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置引擎:一条 Cmd → 一个 DB 事务 → Agenda 循环直至空。
/// 方法拆成 <c>virtual</c> 小步,消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
/// <remarks>
/// M2a 有意的源码级破坏性变更:主构造函数新增 <paramref name="conditionEvaluator"/> 参数(分支求值 SPI,
/// 供 <see cref="EnterNodeOp"/> 选臂用)。M2b 同理追加 <paramref name="notifier"/> 参数(通知 SPI,
/// 供各 Op 建任务 / 实例完结 / 转办后调用)。前置 <c>TryAdd</c> 整体替换 <see cref="IWorkflowEngine"/>
/// 的消费者不受影响(<see cref="IWorkflowEngine"/> 契约本身没动);<b>继承</b> <see cref="WorkflowEngine"/>
/// 的消费者需要在自己的 <c>base(...)</c> 调用里补上这些参数。不为兼容加 <c>[Obsolete]</c> 双构造函数。
/// M2c 第三次同样的追加:<paramref name="receipts"/>(写操作幂等回执 SPI,供 <see cref="ExecuteAsync"/>
/// 在事务开头查/占位、成功后回填),以及 <paramref name="logger"/>(通知失败此前完全无声,见
/// <see cref="DispatchPendingNotificationsAsync"/>)。M3a-1 第四次同样的追加:
/// <paramref name="idGenerator"/>(<see cref="EnterNodeOp"/> 生成 <see cref="WfToken.NodeVisitId"/> 用的
/// 雪花发号器,内核既有 <see cref="IIdGenerator"/>,不新造发号机制)。
/// </remarks>
public class WorkflowEngine(
    IRepository<WfInstance> instances,
    IApproverResolver approverResolver,
    IWorkflowFormBinder formBinder,
    WorkflowOptions options,
    TimeProvider timeProvider,
    IWfConditionEvaluator conditionEvaluator,
    IWorkflowNotifier notifier,
    IWfOperationReceiptService receipts,
    ILogger<WorkflowEngine> logger,
    IIdGenerator idGenerator) : IWorkflowEngine
{
    /// <inheritdoc />
    public virtual async Task<WfEngineResult> ExecuteAsync(
        IWfCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var db = instances.Db;
        WfExecutionContext? ctx = null;
        var identity = TryCreateIdentity(command);
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            // 幂等短路必须在 switch **之前**:任何 BeginXxxAsync 一跑就已经改了状态(领待办、插实例),
            // 那时再短路等于推进了两次却只回一次结果。identity 为 null = 本次不做幂等(没带 key,或
            // 是系统自己派的 TimeoutFireCmd),走原路。
            if (identity is not null)
            {
                var hit = await receipts.TryBeginAsync(identity, cancellationToken);
                if (hit is not null)
                    return DeserializeResult(hit);
            }

            ctx = command switch
            {
                StartInstanceCmd start => await BeginStartAsync(db, start, cancellationToken),
                CompleteTaskCmd complete => await BeginCompleteAsync(db, complete, cancellationToken),
                TransferTaskCmd transfer => await BeginTransferAsync(db, transfer, cancellationToken),
                DelegateTaskCmd delegateCmd => await BeginDelegateAsync(db, delegateCmd, cancellationToken),
                CancelInstanceCmd cancel => await BeginCancelAsync(db, cancel, cancellationToken),
                ReturnTaskCmd ret => await BeginReturnAsync(db, ret, cancellationToken),
                ResubmitInstanceCmd resubmit => await BeginResubmitAsync(db, resubmit, cancellationToken),
                TimeoutFireCmd timeout => await BeginTimeoutAsync(db, timeout, cancellationToken),
                NodeExecutionCompletedCmd done => await BeginNodeExecutionCompletedAsync(db, done, cancellationToken),
                _ => throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["command"] = command.GetType().Name }),
            };

            await RunAgendaAsync(ctx, cancellationToken);
            var result = ctx.ToResult();

            // 回填占位行,与领域状态同一事务提交。业务失败走不到这里 —— 异常让整个事务回滚,
            // 占位行随之消失,重试可以干干净净地重来(这正是「业务失败不落回执」的实现方式)。
            if (identity is not null)
                await receipts.CommitAsync(identity, 0, SerializeResult(result), cancellationToken);

            return result;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);

        // 事务已提交:此时才派发排队的通知(不能在事务内发——提交失败时不该已经推过,
        // 且真实 SignalR 网关会让客户端在提交落盘前就收到推送去查询,读到脏数据)。
        // 命中回执短路时 ctx 保持 null,于是本守卫**顺带**挡掉了「重试把通知再推一遍」——
        // 第一次已经推过了,不必为此另写分支。
        if (ctx is not null)
            await DispatchPendingNotificationsAsync(ctx, cancellationToken);

        return tran.Data;
    }

    /// <summary>
    /// 解析这条命令的幂等身份;<c>null</c> = <b>本次不做幂等</b>(命令没带 <c>RequestId</c>,或者
    /// 是 <see cref="TimeoutFireCmd"/> 这种系统自己扫出来的动作——它不继承 <see cref="WfWriteCmd"/>,
    /// 没有「用户这一次点击」的身份可言,所以这里不必写它的特例分支)。
    /// <para><b>ScopeKey 只有 <see cref="WfCommandType.Start"/> 取机构</b>:发起的 <c>TargetId</c> 是
    /// <b>定义版本 Id</b>,同一份定义被多个机构共用,机构维度在那里是承重的;其余命令的 <c>TargetId</c>
    /// 是实例/待办的雪花 Id,全局唯一、机构已隐含,再去 load 一次实例只会拖慢短路却不增加区分度。</para>
    /// </summary>
    protected virtual WfOperationIdentity? TryCreateIdentity(IWfCommand command)
    {
        if (command is not WfWriteCmd { RequestId: not null } write)
            return null;

        var key = write.RequestId;
        return write switch
        {
            StartInstanceCmd start => WfOperationIdentity.Create(
                start.StarterOrgId?.ToString(CultureInfo.InvariantCulture),
                WfCommandType.Start, WfTargetType.DefinitionVersion,
                start.DefinitionVersionId, start.StarterUserId, key),

            // 同意与拒绝共用 CompleteTaskCmd,但**必须**按 Action 分成两个 CommandType:否则
            // 「同一个 key 先同意、再拒绝」会被当成同一次动作的重试,直接把同意的结果回给拒绝。
            CompleteTaskCmd complete => WfOperationIdentity.Create(
                null,
                complete.Action switch
                {
                    WfTaskAction.Approve => WfCommandType.Approve,
                    WfTaskAction.Reject => WfCommandType.Reject,
                    _ => throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                        new Dictionary<string, object?> { ["action"] = complete.Action }),
                },
                WfTargetType.Task, complete.TaskId, complete.UserId, key),

            TransferTaskCmd transfer => WfOperationIdentity.Create(
                null, WfCommandType.Transfer, WfTargetType.Task,
                transfer.TaskId, transfer.UserId, key),

            DelegateTaskCmd delegateCmd => WfOperationIdentity.Create(
                null, WfCommandType.Delegate, WfTargetType.Task,
                delegateCmd.TaskId, delegateCmd.UserId, key),

            ReturnTaskCmd ret => WfOperationIdentity.Create(
                null, WfCommandType.Return, WfTargetType.Task,
                ret.TaskId, ret.UserId, key),

            CancelInstanceCmd cancel => WfOperationIdentity.Create(
                null, WfCommandType.Cancel, WfTargetType.Instance,
                cancel.InstanceId, cancel.CallerUserId, key),

            ResubmitInstanceCmd resubmit => WfOperationIdentity.Create(
                null, WfCommandType.Resubmit, WfTargetType.Instance,
                resubmit.InstanceId, resubmit.CallerUserId, key),

            _ => throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                new Dictionary<string, object?> { ["command"] = write.GetType().Name }),
        };
    }

    /// <summary>把首次执行的结果序列化进回执。用 <see cref="WfModelJson.Options"/>,不另起一份配置。</summary>
    protected virtual string SerializeResult(WfEngineResult result) =>
        System.Text.Json.JsonSerializer.Serialize(result, WfModelJson.Options);

    /// <summary>
    /// 命中回执时把首次结果读回来。
    /// <para><see cref="WfOperationReceipt.ResultJson"/> 为空属于**损坏状态**而不是正常分支:占位行只
    /// 活在事务里,而 <c>CommitAsync</c> 的「0 行即抛」保证了「提交了却没回填」这条路走不通。这里宁可抛
    /// 也不兜底成空结果 —— 空结果的 <c>InstanceId = 0</c> 会被调用方当成一次成功。</para>
    /// </summary>
    protected virtual WfEngineResult DeserializeResult(WfOperationReceipt receipt)
    {
        var restored = string.IsNullOrEmpty(receipt.ResultJson)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<WfEngineResult>(
                receipt.ResultJson, WfModelJson.Options);

        return restored ?? throw WorkflowErrorCode.Exception(
            WorkflowErrorCode.OperationFailed,
            new Dictionary<string, object?>
            {
                ["reason"] = "receiptResultMissing",
                ["identityHash"] = receipt.IdentityHash,
            });
    }

    /// <summary>
    /// 事务提交成功后统一派发 <see cref="WfExecutionContext.PendingTaskAssignedNotifications"/> /
    /// <see cref="WfExecutionContext.PendingInstanceCompletedNotification"/>。通知失败仍要 try/catch——
    /// 此时事务已提交,不会回滚,但不能让通知异常炸掉这次 HTTP 响应。
    /// </summary>
    protected virtual async Task DispatchPendingNotificationsAsync(
        WfExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        foreach (var (notifyCtx, userIds) in ctx.PendingTaskAssignedNotifications)
        {
            try
            {
                await ctx.Notifier.TaskAssignedAsync(notifyCtx, userIds, cancellationToken);
            }
            catch (Exception ex)
            {
                // 事务已提交,不能让通知异常炸掉这次响应 —— 但也不能像从前那样一声不吭。
                // 异常走 exception 形参而非拼进消息串:拼串会丢掉堆栈与 inner exception,
                // 而那正是排障要看的东西。
                logger.LogWarning(
                    ex,
                    "工作流待办到达通知失败。InstanceId={InstanceId} NodeId={NodeId} UserCount={UserCount}",
                    notifyCtx.InstanceId,
                    notifyCtx.NodeId,
                    userIds.Count);
            }
        }

        if (ctx.PendingInstanceCompletedNotification is { } completedCtx)
        {
            try
            {
                await ctx.Notifier.InstanceCompletedAsync(completedCtx, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "工作流实例完结通知失败。InstanceId={InstanceId} StarterUserId={StarterUserId} Status={Status}",
                    completedCtx.InstanceId,
                    completedCtx.StarterUserId,
                    completedCtx.Status);
            }
        }
    }

    /// <summary>Agenda 出队循环;某 Op 不再 plan 则自然停(等人审批)。</summary>
    protected virtual async Task RunAgendaAsync(WfExecutionContext ctx, CancellationToken cancellationToken)
    {
        while (ctx.Agenda.TryTake(out var op))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await op.ExecuteAsync(ctx, cancellationToken);
        }
    }

    /// <summary>发起:校验挂载点 → 插 instance/token → 历史 → 入队 EnterNode(start)。</summary>
    protected virtual async Task<WfExecutionContext> BeginStartAsync(
        ISqlSugarClient db,
        StartInstanceCmd cmd,
        CancellationToken cancellationToken)
    {
        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == cmd.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);
        if (model.Root.Type != WfNodeType.Start)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "rootNotStart" });
        if (string.IsNullOrWhiteSpace(model.Root.Id))
            model.Root.Id = "start";

        await formBinder.ValidateOnStartAsync(
            new WfFormBindContext
            {
                InstanceId = 0,
                DefinitionVersionId = cmd.DefinitionVersionId,
                BusinessKey = cmd.BusinessKey,
                VariablesJson = cmd.VariablesJson,
                Status = WfInstanceStatus.Running,
                StarterUserId = cmd.StarterUserId,
            },
            cancellationToken);

        var leaderLevels = ResolveLeaderLevels(model);
        var leaderChainByLevel = await SnapshotLeaderChainsAsync(
            cmd.StarterUserId, cmd.StarterOrgId, leaderLevels, cancellationToken);

        var instance = new WfInstance
        {
            DefinitionVersionId = cmd.DefinitionVersionId,
            BusinessKey = cmd.BusinessKey,
            StarterUserId = cmd.StarterUserId,
            Status = WfInstanceStatus.Running,
            VariablesJson = cmd.VariablesJson,
            SelectedUserIdsJson = cmd.SelectedUserIdsByNode is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(cmd.SelectedUserIdsByNode, WfModelJson.Options),
            LeaderChainJson = leaderChainByLevel is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(leaderChainByLevel, WfModelJson.Options),
        };
        await db.Insertable(instance).ExecuteCommandAsync();

        var token = new WfToken
        {
            InstanceId = instance.Id,
            NodeId = model.Root.Id,
            Status = WfTokenStatus.Active,
        };
        await db.Insertable(token).ExecuteCommandAsync();

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.StarterUserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = cmd.SelectedUserIdsByNode
                                    ?? new Dictionary<string, List<long>>(StringComparer.Ordinal),
            StarterOrgId = cmd.StarterOrgId,
            LeaderChainByLevel = leaderChainByLevel,
        };

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceStarted,
            model.Root.Id,
            new { starterUserId = cmd.StarterUserId, businessKey = cmd.BusinessKey },
            cancellationToken);

        agenda.Plan(new EnterNodeOp(model.Root));
        return ctx;
    }

    /// <summary>完成待办:加载运行态 → 入队 CompleteTaskOp。</summary>
    protected virtual async Task<WfExecutionContext> BeginCompleteAsync(
        ISqlSugarClient db,
        CompleteTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.UserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new CompleteTaskOp(task, cmd.UserId, cmd.Action, cmd.Comment));
        return ctx;
    }

    /// <summary>转办:加载运行态 → 入队 TransferTaskOp(不推进 token)。</summary>
    protected virtual async Task<WfExecutionContext> BeginTransferAsync(
        ISqlSugarClient db,
        TransferTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.UserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new TransferTaskOp(task, cmd.UserId, cmd.ToUserId, cmd.Comment));
        return ctx;
    }

    /// <summary>
    /// 委托(一次性):加载运行态 → 入队 DelegateTaskOp(不推进 token)。准入与转办逐字同款——
    /// 待办是否还在、实例是否 Running、token 是否活跃;「只有当前 Pending 办理人有权委托」由
    /// <see cref="DelegateTaskOp"/> 的 actor CAS 认领兜住(认领不到即 <c>TaskConflict</c>)。
    /// 有意不与 <see cref="BeginTransferAsync"/> 合并成一个泛型方法:<c>BeginXxxAsync</c> 是消费者
    /// 覆写单个动词准入逻辑的入口,合并会让「只想改委托的准入」变成「必须连转办一起复制」。
    /// </summary>
    protected virtual async Task<WfExecutionContext> BeginDelegateAsync(
        ISqlSugarClient db,
        DelegateTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.UserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new DelegateTaskOp(task, cmd.UserId, cmd.ToUserId, cmd.Comment));
        return ctx;
    }

    /// <summary>撤销:仅发起人、仅无人已批的 Running 实例 → 入队 CancelInstanceOp。</summary>
    protected virtual async Task<WfExecutionContext> BeginCancelAsync(
        ISqlSugarClient db, CancelInstanceCmd cmd, CancellationToken cancellationToken)
    {
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == cmd.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);
        if (instance.StarterUserId != cmd.CallerUserId)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CancelNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notStarter" });
        }

        if (await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.InstanceId == instance.Id && h.Action == WfTaskAction.Approve))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CancelNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "alreadyApproved" });
        }

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instance.Id && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.CallerUserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new CancelInstanceOp());
        return ctx;
    }

    /// <summary>退回:加载运行态 → 入队 ReturnTaskOp(关闭当前待办、token 回退,Agenda 留空等重提)。</summary>
    protected virtual async Task<WfExecutionContext> BeginReturnAsync(
        ISqlSugarClient db,
        ReturnTaskCmd cmd,
        CancellationToken cancellationToken)
    {
        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.UserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        agenda.Plan(new ReturnTaskOp(task, cmd.UserId, cmd.TargetNodeId, cmd.Comment));
        return ctx;
    }

    /// <summary>
    /// 超时触发:准入(同转办)→ 按 §14.1 领取到期待办 → 解析当前 Pending 办理人 → 写
    /// <see cref="WfHistoryEventType.TimeoutFired"/> → 按签核模式入队等价的人工动作 Op。
    /// <para><b>身份只能是当前 Pending 办理人。</b><see cref="CompleteTaskOp"/> 的 actor 认领是
    /// <c>WHERE TaskId=@id AND UserId=@caller AND Status=Pending</c> 且影响行数必须为 1,传系统账号
    /// 必然认领不到;要换身份就得松掉人工路径的「仅本人可办」这条承重校验。所以
    /// <c>wf_his_task.Action</c> 记的是**原生动词**(<c>Approve</c>/<c>Reject</c>/<c>Transfer</c>),
    /// 真相由同事务的 <c>TimeoutFired</c> 事件与 <see cref="TimeoutFireCmd.Comment"/> 说明。</para>
    /// <para><b>刻意不给超时造新的 <see cref="WfTaskAction"/> 值。</b>新值会同时静默破坏两处语义:
    /// <see cref="BeginCancelAsync"/> 的撤销准入只认 <see cref="WfTaskAction.Approve"/>(超时自动通过过的
    /// 实例会变成还能撤销),<c>EnterNodeOp.ResolveAdjacentApprovedUserIdsAsync</c> 的基线白名单只认
    /// <c>Approve|Reject|Return</c>(超时自动通过的节点不进去重基线)。而枚举值一旦发版落进消费者库
    /// 就不可回退。要区分人/机器动作的正确补法是将来加一个可空列。</para>
    /// </summary>
    protected virtual async Task<WfExecutionContext> BeginTimeoutAsync(
        ISqlSugarClient db,
        TimeoutFireCmd cmd,
        CancellationToken cancellationToken)
    {
        if (cmd.Action == WfTimeoutAction.Remind)
        {
            // 提醒不改任何状态,故不进引擎(也不做版本 CAS,否则办理人会为了一条提醒收到 48007)。
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                new Dictionary<string, object?> { ["action"] = cmd.Action.ToString() });
        }

        var task = await db.Queryable<WfTask>().Where(t => t.Id == cmd.TaskId).FirstAsync();
        if (task is null)
        {
            var completed = await db.Queryable<WfHisTask>()
                .AnyAsync(h => h.TaskId == cmd.TaskId);
            throw WorkflowErrorCode.Exception(
                completed ? WorkflowErrorCode.TaskConflict : WorkflowErrorCode.TaskNotFound);
        }

        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == task.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == task.TokenId && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            // 超时是系统扫出来的,没有"用户这一次点击"的身份 —— null 是语义,不是遗漏。
            RequestId = null,
            ActorType = WfHistoryActorType.Timeout,
            ActorUserId = null,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        await ClaimDueTaskAsync(ctx, task, cmd.ExpectedVersion, cancellationToken);

        var pending = await ResolvePendingActorsAsync(ctx, task, cancellationToken);
        if (pending.Count == 0)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = task.Id });
        }

        await PlanTimeoutOpsAsync(ctx, task, cmd, pending, cancellationToken);
        return ctx;
    }

    /// <summary>
    /// 领取一件到期待办(设计规划 §14.1):<c>taskId + Version + DueTime &lt;= now</c> 条件更新,
    /// 影响行数 ≠ 1 表示人工动作(或另一个执行者)已经胜出 → <see cref="WorkflowErrorCode.TaskConflict"/>。
    /// <para><b>领取后必须把新 <see cref="WfTask.Version"/> 写回内存实例</b>:随后入队的
    /// <see cref="CompleteTaskOp"/> / <see cref="ReassignTaskOpBase"/> 各自还要做一次任务级 CAS,
    /// 不写回它们就会对着旧版本号抛一个**假的** <c>TaskConflict</c>。一个事务里 <c>Version</c> 前进两次
    /// 是正常的。</para>
    /// </summary>
    protected virtual async Task ClaimDueTaskAsync(
        WfExecutionContext ctx,
        WfTask task,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = ctx.TimeProvider.GetLocalNow().DateTime;
        var claimed = await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = expectedVersion + 1 })
            .Where(t => t.Id == task.Id && t.Version == expectedVersion
                        && t.DueTime != null && t.DueTime <= now)
            .ExecuteCommandAsync();
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TaskConflict,
                new Dictionary<string, object?> { ["taskId"] = task.Id });
        }
        task.Version = expectedVersion + 1;
    }

    /// <summary>
    /// 本待办当前的 Pending 审批人,按 <c>Sort</c>、<c>Id</c> 升序(顺序会签里只会有一位)。
    /// </summary>
    protected virtual async Task<IReadOnlyList<long>> ResolvePendingActorsAsync(
        WfExecutionContext ctx,
        WfTask task,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ctx.Db.Queryable<WfTaskActor>()
            .Where(a => a.TaskId == task.Id && a.Status == WfActorStatus.Pending
                        && a.ActorType == WfActorType.Approver)
            .OrderBy(a => a.Sort, OrderByType.Asc)
            .OrderBy(a => a.Id, OrderByType.Asc)
            .Select(a => a.UserId)
            .ToListAsync();
    }

    /// <summary>
    /// 写 <see cref="WfHistoryEventType.TimeoutFired"/>,再按签核模式入队等价的人工动作 Op。
    /// <para><b>只有会签(<see cref="WfSignMode.All"/>)的自动通过才对每个 Pending 各入队一个</b>,
    /// 其余一律恰好一个,这不是简化而是必须:</para>
    /// <list type="bullet">
    /// <item><see cref="WfSignMode.Any"/>——<c>CompleteTaskOp.TryPassAsync</c> 的或签分支直接通过,
    /// 第一个 Op 就把 <c>wf_task</c>/<c>wf_task_actor</c> **物理删除**;若对多个 Pending 各入队一个,
    /// 第二个 Op 的任务级 CAS 影响行数为 0 → <c>TaskConflict</c> → **整个事务回滚**,现象是
    /// 「超时什么都没干」,极难从日志看出原因。</item>
    /// <item><see cref="WfSignMode.All"/>——会签分支看「还有没有 Pending」,只批一个不会通过,节点原地
    /// 不动、下次扫描再来,超时对会签节点等于失效。多个 Op 串在同一 Agenda 里安全:它们改的是**同一个**
    /// <see cref="WfTask"/> 实例,后一个的 CAS 因此对得上。</item>
    /// <item><see cref="WfSignMode.Sequential"/>——只有一位 Pending(其余 <c>Waiting</c>)。批掉他会晋级
    /// 下一位,任务行仍在、<c>DueTime</c> 仍是过去 → 下一拍继续自动通过下一位,逐轮级联直到节点通过。
    /// 这是可接受且可解释的行为(「这个节点整体超时了」),不是缺陷。</item>
    /// <item>自动拒绝一律恰好一个——一票否决,<c>CompleteTaskOp</c> 的拒绝分支自带
    /// <c>skipRemaining</c>。</item>
    /// </list>
    /// <para><b>⚠ 语义空白(已知,未定案):会签(<see cref="WfSignMode.All"/>)下的超时自动转办只改派
    /// <c>actors[0]</c>,却把整行的 <c>DueTime</c> 清掉</b> —— 剩下的 Pending 办理人从此不受任何超时约束。
    /// 「转办」这个动作本身是任务级的(一次只换一个人),而 <c>DueTime</c> 是任务级的(一行只有一个),
    /// 两者在会签上对不齐;清 <c>DueTime</c> 又是不清就无限重触发的必需品(见下面 Transfer 分支的注释)。
    /// 三种可能的定案(改派全部 Pending / 只清到最后一位办理完 / 会签节点禁用 <c>Transfer</c>)都不是本轮
    /// 能顺手定的产品判断,已记 <c>## Findings</c> 挂 Task 10 补用例与定案。</para>
    /// </summary>
    protected virtual async Task PlanTimeoutOpsAsync(
        WfExecutionContext ctx,
        WfTask task,
        TimeoutFireCmd cmd,
        IReadOnlyList<long> pendingUserIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actForAll = cmd.Action == WfTimeoutAction.AutoPass && task.SignMode == WfSignMode.All;
        var actors = actForAll ? pendingUserIds : pendingUserIds.Take(1).ToList();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.TimeoutFired,
            task.NodeId,
            new
            {
                taskId = task.Id,
                action = cmd.Action.ToString(),
                actedAsUserIds = actors,
                dueTime = task.DueTime,
                transferUserId = cmd.TransferUserId,
            },
            cancellationToken);

        switch (cmd.Action)
        {
            case WfTimeoutAction.AutoPass:
                foreach (var userId in actors)
                    ctx.Agenda.Plan(new CompleteTaskOp(task, userId, WfTaskAction.Approve, cmd.Comment));
                return;

            case WfTimeoutAction.AutoReject:
                ctx.Agenda.Plan(new CompleteTaskOp(task, actors[0], WfTaskAction.Reject, cmd.Comment));
                return;

            case WfTimeoutAction.Transfer:
                // 转办不删待办、不推进 token,DueTime 还留在过去 → 下一拍再扫到 → 目标已是 actor →
                // alreadyActor 抛 48010 → 每拍失败一次直到有人办掉。必须在**本事务**里清掉它
                // (放到 Job 里事后补,崩在中间就回到无限重触发)。
                await ClearDueTimeAsync(ctx, task, cancellationToken);
                ctx.Agenda.Plan(new TransferTaskOp(
                    task, actors[0], cmd.TransferUserId ?? 0, cmd.Comment));
                return;

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["action"] = cmd.Action.ToString() });
        }
    }

    /// <summary>
    /// 清掉 <see cref="WfTask.DueTime"/>(一次性升级:超时转办之后没有第二个升级目标)。
    /// 只在超时转办这条路上用——自动通过/拒绝会关掉或推进待办,顺序会签的级联是有意保留的。
    /// </summary>
    protected virtual async Task ClearDueTimeAsync(
        WfExecutionContext ctx,
        WfTask task,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ctx.Db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { DueTime = null })
            .Where(t => t.Id == task.Id)
            .ExecuteCommandAsync();
        task.DueTime = null;
    }

    protected virtual IReadOnlyDictionary<string, List<long>> DeserializeSelectedUsers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, List<long>>(StringComparer.Ordinal);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<long>>>(
                       json, WfModelJson.Options)
                   ?? new Dictionary<string, List<long>>(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "selectedUsersInvalid" });
        }
    }

    /// <summary>
    /// 反序列化 <see cref="WfInstance.LeaderChainJson"/>。<c>null</c>/空白 json(老实例或无 multiLeader 节点)
    /// 保持返回 <c>null</c>(=没有快照);非空 json 读取为按 level 键控的链快照。
    /// 与 <see cref="DeserializeSelectedUsers"/> 不同——那里 null 会折叠成空字典,这里保留老实例语义。
    /// </summary>
    protected virtual IReadOnlyDictionary<int, IReadOnlyList<long>>? DeserializeLeaderChainsByLevel(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var chains = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, List<long>>>(
                json, WfModelJson.Options);
            return chains?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<long>)pair.Value);
        }
        catch (System.Text.Json.JsonException)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "leaderChainInvalid" });
        }
    }

    /// <summary>
    /// 扫描整棵模型树(含分支臂内,经 <see cref="WfModelIndex"/>)找出所有 <c>multiLeader</c> 节点,
    /// 将 <c>level</c> 归一化到至少 1 后去重,并保留该 level 首个节点的真实参数包。
    /// 内置 provider 只读取 level;消费者自定义 provider 若让同 level 节点的其它参数不同,
    /// 当前按 level 键控的快照由首个节点参数胜出。若需逐节点快照,应另行扩展 SPI 传入 NodeId。
    /// </summary>
    protected virtual IReadOnlyDictionary<int, Dictionary<string, System.Text.Json.JsonElement>?> ResolveLeaderLevels(
        WfModel model)
    {
        var levels = new Dictionary<int, Dictionary<string, System.Text.Json.JsonElement>?>();
        foreach (var node in WfModelIndex.Build(model).Nodes)
        {
            var assignee = node.Props?.Assignee;
            if (assignee is null ||
                !string.Equals(assignee.Provider, ApproverProviderKeys.MultiLeader, StringComparison.Ordinal))
                continue;

            var level = Math.Max(1, ApproverParamReader.GetInt(assignee.Params, "level", 1));
            levels.TryAdd(level, assignee.Params);
        }

        return levels;
    }

    /// <summary>
    /// 发起时按每个 distinct level 分别算快照;模型无 multiLeader 节点则返回 <c>null</c>(不存)。
    /// 每次保留节点真实参数并覆盖归一化后的 level;<c>LeaderChainByLevel = null</c> 避免自引用,
    /// provider 自然走实时上溯。各级结果原样存(启用过滤后,运行期不再二次过滤)。
    /// </summary>
    protected virtual async Task<IReadOnlyDictionary<int, IReadOnlyList<long>>?> SnapshotLeaderChainsAsync(
        long starterUserId,
        long? starterOrgId,
        IReadOnlyDictionary<int, Dictionary<string, System.Text.Json.JsonElement>?> leaderLevels,
        CancellationToken cancellationToken)
    {
        if (leaderLevels.Count == 0)
            return null;

        var chains = new Dictionary<int, IReadOnlyList<long>>();
        foreach (var (level, nodeParams) in leaderLevels.OrderBy(pair => pair.Key))
        {
            var levelParams = nodeParams is null
                ? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, System.Text.Json.JsonElement>(nodeParams, StringComparer.Ordinal);
            levelParams["level"] = System.Text.Json.JsonSerializer.SerializeToElement(level);

            chains[level] = await approverResolver.ResolveAsync(
                ApproverProviderKeys.MultiLeader,
                new ApproverResolveContext
                {
                    InitiatorUserId = starterUserId,
                    InitiatorOrgId = starterOrgId,
                    Params = levelParams,
                    LeaderChainByLevel = null,
                },
                cancellationToken);
        }

        return chains;
    }

    /// <summary>
    /// 重提:仅发起人、仅退回后无活跃待办的 Running 实例可重提 → 重算 multiLeader 快照 → token/实例回到
    /// start → 入队 EnterNode(start),从头重走一遍(连已批过的节点也重新审)。复用同一实例行,不新建实例。
    /// </summary>
    protected virtual async Task<WfExecutionContext> BeginResubmitAsync(
        ISqlSugarClient db,
        ResubmitInstanceCmd cmd,
        CancellationToken cancellationToken)
    {
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == cmd.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);
        if (instance.Status != WfInstanceStatus.Running)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict);
        if (instance.StarterUserId != cmd.CallerUserId)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ResubmitNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "notStarter" });
        }

        var token = await db.Queryable<WfToken>()
            .Where(t => t.InstanceId == instance.Id && t.Status == WfTokenStatus.Active)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        if (await db.Queryable<WfTask>().AnyAsync(t => t.TokenId == token.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ResubmitNotAllowed,
                new Dictionary<string, object?> { ["reason"] = "hasActiveTask" });
        }

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == instance.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        await formBinder.ValidateOnStartAsync(
            new WfFormBindContext
            {
                InstanceId = instance.Id,
                DefinitionVersionId = instance.DefinitionVersionId,
                BusinessKey = instance.BusinessKey,
                VariablesJson = cmd.VariablesJson ?? instance.VariablesJson,
                Status = WfInstanceStatus.Running,
                StarterUserId = instance.StarterUserId,
            },
            cancellationToken);

        if (cmd.VariablesJson is not null)
            instance.VariablesJson = cmd.VariablesJson;
        if (cmd.SelectedUserIdsByNode is not null)
        {
            instance.SelectedUserIdsJson = System.Text.Json.JsonSerializer.Serialize(
                cmd.SelectedUserIdsByNode, WfModelJson.Options);
        }

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var leaderLevels = ResolveLeaderLevels(model);
        var leaderChainByLevel = await SnapshotLeaderChainsAsync(
            instance.StarterUserId, starterOrgId, leaderLevels, cancellationToken);
        instance.LeaderChainJson = leaderChainByLevel is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(leaderChainByLevel, WfModelJson.Options);

        // ctx 有意构造在两条 UPDATE **之前**:重提此前全程无 CAS 锚点(两处 Updateable(entity) 都是无条件,
        // 上面的「无活跃任务」校验只是读),双击重提会让两个事务都通过校验、都 Plan(EnterNodeOp(root)) →
        // 同一节点两套 wf_task/actor + 两条 InstanceResubmitted + 两次通知,批掉一个还会留孤儿。
        // 锚点落在 token 而不是实例:重提不改实例状态(Running → Running),没有可锚的状态变化;而 token
        // 的 NodeId 归零**就是**这次重提的状态推进,锚在它上面既是真锚点也符合 §4.1 的原文形状。
        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            RequestId = cmd.RequestId,
            ActorType = WfHistoryActorType.Human,
            ActorUserId = cmd.CallerUserId,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = cmd.SelectedUserIdsByNode
                                    ?? DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = leaderChainByLevel,
        };

        // 本事务的第一个写操作。输的那一边抛 48004(reason=tokenVersionConflict)→ 整事务回滚。
        await ctx.ClaimTokenAsync(WfTokenStatus.Active, cancellationToken);

        await db.Updateable(instance)
            .UpdateColumns(i => new { i.VariablesJson, i.LeaderChainJson, i.SelectedUserIdsJson, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();

        token.NodeId = model.Root.Id;
        await db.Updateable(token)
            .UpdateColumns(t => new { t.NodeId, t.UpdateTime, t.UpdateUserId })
            .ExecuteCommandAsync();

        await ctx.AppendHistoryAsync(
            WfHistoryEventType.InstanceResubmitted,
            model.Root.Id,
            new { starterUserId = cmd.CallerUserId },
            cancellationToken);

        agenda.Plan(new EnterNodeOp(model.Root));
        return ctx;
    }

    // ── M3a-1 Task 6:Execution dispatcher 回写(NodeExecutionCompletedCmd) ──────────────────

    /// <summary><see cref="ResolveRetryDelay"/> 无量值可用时的退避基数(秒),按 <c>AttemptCount</c> 指数翻倍。</summary>
    protected const int RetryBaseSeconds = 30;

    /// <summary>
    /// handler 提供的 <see cref="WfNodeExecutionResult.RetryAfter"/> 允许的上限——它来自消费者代码
    /// (trust boundary),必须钳制:<see cref="TimeSpan.Zero"/> 会热循环,过大的值会让
    /// <c>nowUtc + delay</c> 逼近 <see cref="DateTime.MaxValue"/>。
    /// </summary>
    protected static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// <see cref="BeginNodeExecutionCompletedAsync"/> 的判定结果——<see cref="ResolveExecutionOutcome"/>
    /// 纯函数算出、<see cref="ClaimExecutionWritebackAsync"/> 落库、<see cref="BuildExecutionOutboxPayload"/>
    /// 取用。<see cref="IsTerminal"/> 决定是否入队 outbox(§4.6:只在 execution 进终态时入队)。
    /// </summary>
    protected readonly record struct WfExecutionOutcome(
        WfNodeExecutionStatus Status,
        DateTime? NextRetryAtUtc,
        DateTime? CompletedTimeUtc,
        int? ErrorCode,
        string? Summary,
        bool IsTerminal);

    /// <summary>
    /// <see cref="NodeExecutionCompletedCmd"/> 的回写:载入 execution/instance/token/version/model(只读)→
    /// <see cref="ResolveExecutionOutcome"/> 算出判定 → <see cref="ClaimExecutionWritebackAsync"/>(本 Task
    /// 全仓唯一一处 <c>Updateable&lt;WfNodeExecution&gt;</c>,fence CAS,**必须在 attempt 写入之前**——顺序颠倒
    /// 会让老 owner 用被新 worker 推高过的 <c>AttemptCount</c> 插 attempt,撞 <c>uk_wf_node_exec_attempt_no</c>,
    /// 症状伪装成唯一键 bug 而非 fence 过期)→ <see cref="WfNodeExecutionAttemptStore.AppendAsync"/> →
    /// 终态才入队 outbox → 按结果 <c>Plan</c> 对应 Op,交给 <see cref="ExecuteAsync"/> 的
    /// <see cref="RunAgendaAsync"/> 在**同一事务**里跑完。
    /// <para>本方法不写任何 <c>wf_history</c> 事件(语义契约 D7):自动节点的生命周期不写自己的历史,
    /// <c>Succeeded</c> 路径的 <c>NodeLeave</c>/<c>NodeEnter</c> 由 <see cref="TakeTransitionOp"/>/
    /// <see cref="EnterNodeOp"/> 产出,<c>ManualFallback</c> 路径的 <c>TaskCreated</c> 由
    /// <see cref="EnterNodeOp.CreateTaskAsync"/> 产出,失败/重试路径的审计事实源是
    /// <c>wf_node_execution_attempt</c>。</para>
    /// </summary>
    protected virtual async Task<WfExecutionContext> BeginNodeExecutionCompletedAsync(
        ISqlSugarClient db,
        NodeExecutionCompletedCmd cmd,
        CancellationToken cancellationToken)
    {
        var execution = await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == cmd.ExecutionId)
            .FirstAsync();
        if (execution is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                new Dictionary<string, object?> { ["reason"] = "executionNotFound", ["executionId"] = cmd.ExecutionId });
        }

        // 后台 worker 没有 IDataScopeContext,IOrgScoped 全局过滤器会让本查询静默返回 0 行
        // (照抄 BeginTimeoutAsync 的姿势,同款理由)。
        var instance = await db.Queryable<WfInstance>()
            .ClearFilter<IOrgScoped>()
            .Where(i => i.Id == execution.InstanceId)
            .FirstAsync();
        if (instance is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceNotFound);

        var token = await db.Queryable<WfToken>()
            .Where(t => t.Id == execution.TokenId)
            .FirstAsync();
        if (token is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.TokenNotFound);

        var version = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.Id == execution.DefinitionVersionId)
            .FirstAsync();
        if (version is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionVersionNotFound);

        var model = WfModelJson.Deserialize(version.ModelJson)
                    ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);

        var node = WfModelIndex.Build(model).Find(execution.NodeId);
        if (node is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "executionNodeMissing", ["nodeId"] = execution.NodeId });
        }

        long? starterOrgId = null;
        var starter = await db.Queryable<TenonAdmin.Services.SysUser>()
            .Where(u => u.Id == instance.StarterUserId)
            .FirstAsync();
        if (starter is not null)
            starterOrgId = starter.OrgId;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var outcome = ResolveExecutionOutcome(execution, instance, token, cmd.Result, nowUtc);

        // 本事务的第一个写操作,且必须在 AppendAsync 之前(见类注释)。
        await ClaimExecutionWritebackAsync(db, execution, cmd, outcome, cancellationToken);

        var attempt = await WfNodeExecutionAttemptStore.AppendAsync(
            db, execution, cmd.Result, cmd.StartedAtUtc, cmd.EndedAtUtc, cancellationToken);

        var agenda = new WfAgenda();
        var ctx = new WfExecutionContext
        {
            Db = db,
            Agenda = agenda,
            ApproverResolver = approverResolver,
            FormBinder = formBinder,
            Options = options,
            TimeProvider = timeProvider,
            ConditionEvaluator = conditionEvaluator,
            Notifier = notifier,
            // worker 派发的动作没有"用户这一次点击"的身份可言——null 是语义,不是遗漏(同 BeginTimeoutAsync)。
            RequestId = null,
            ActorType = WfHistoryActorType.Worker,
            ActorUserId = null,
            IdGenerator = idGenerator,
            Instance = instance,
            Token = token,
            Model = model,
            DefinitionVersion = version,
            SelectedUserIdsByNode = DeserializeSelectedUsers(instance.SelectedUserIdsJson),
            StarterOrgId = starterOrgId,
            LeaderChainByLevel = DeserializeLeaderChainsByLevel(instance.LeaderChainJson),
        };

        // 终态才入队(§4.6):RetryScheduled 不是终态,不入队——MessageKey 天花板是「一个 (execution,type)
        // 一条消息」,一次 execution 最多进一次终态,"终态 ⇒ 恰好一条"是唯一自洽的规则。
        if (outcome.IsTerminal)
        {
            var payload = BuildExecutionOutboxPayload(execution, cmd, attempt, outcome);
            await WfOutboxStore.EnqueueAsync(
                db, execution, WfOutboxStore.MessageTypeNodeExecutionCompleted, payload, nowUtc, cancellationToken);
        }

        switch (outcome.Status)
        {
            case WfNodeExecutionStatus.Succeeded:
                // Succeeded 复用 TakeTransitionOp(token 离开当前节点 → 求汇合 → 进下一节点或完结实例)——
                // 不用 EnterNodeOp(那是"进入",会重新生成 NodeVisitId、重写 NodeEnter 历史)。
                agenda.Plan(new TakeTransitionOp(node));
                break;

            case WfNodeExecutionStatus.ManualFallback:
                agenda.Plan(new WfManualFallbackOp(node));
                break;

            case WfNodeExecutionStatus.RetryScheduled:
            case WfNodeExecutionStatus.Failed:
            case WfNodeExecutionStatus.Cancelled:
                // 不再前进——RetryScheduled 等下次领取;Failed/Cancelled 是终态,token 原地停住。
                break;

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["status"] = outcome.Status.ToString() });
        }

        return ctx;
    }

    /// <summary>
    /// 本 Task 全仓唯一一处 <c>Updateable&lt;WfNodeExecution&gt;</c>:双谓词 CAS
    /// (<c>Fence == fence &amp;&amp; Status == Running</c>)——<c>Fence</c> 挡老 owner 租约过期后醒来回写,
    /// <c>Status == Running</c> 挡同一 fence 的结果被回放两次。影响行数 ≠ 1 → 48004
    /// (<c>reason=executionFenceConflict</c>)→ 整事务回滚,attempt/outbox/token 一行都不落。
    /// <para><c>RetryScheduled</c> 是唯一一次"终态之外的回写":必须同时写非空 <c>NextRetryAtUtc</c> 并把
    /// <c>LeaseOwner</c>/<c>LeaseExpiresAtUtc</c> 置 null——<c>(RetryScheduled, NextRetryAtUtc = null)</c>
    /// 的行按领取谓词永远领不回来(台账 Task 3 review P1-1 已实测)。</para>
    /// <para><c>SetColumns</c> 里所有 <c>DateTime</c> 与要置空的 <c>null</c> 先落局部变量——zh-CN 下 SqlSugar
    /// 会把内联表达式按区域格式化成字面量拼进 SQL,炸出 <c>near "下午"</c>。</para>
    /// </summary>
    protected virtual async Task ClaimExecutionWritebackAsync(
        ISqlSugarClient db,
        WfNodeExecution execution,
        NodeExecutionCompletedCmd cmd,
        WfExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executionId = execution.Id;
        var fence = cmd.Fence;
        var status = outcome.Status;
        var handlerType = cmd.HandlerType;

        int affected;
        if (status == WfNodeExecutionStatus.RetryScheduled)
        {
            var nextRetryAtUtc = outcome.NextRetryAtUtc;
            string? noOwner = null;
            DateTime? noLease = null;
            affected = await db.Updateable<WfNodeExecution>()
                .SetColumns(e => new WfNodeExecution
                {
                    Status = status,
                    NextRetryAtUtc = nextRetryAtUtc,
                    LeaseOwner = noOwner,
                    LeaseExpiresAtUtc = noLease,
                    HandlerType = handlerType,
                })
                .Where(e => e.Id == executionId && e.Fence == fence && e.Status == WfNodeExecutionStatus.Running)
                .ExecuteCommandAsync();
        }
        else
        {
            var completedTimeUtc = outcome.CompletedTimeUtc;
            var errorCode = outcome.ErrorCode;
            var summary = outcome.Summary;
            affected = await db.Updateable<WfNodeExecution>()
                .SetColumns(e => new WfNodeExecution
                {
                    Status = status,
                    CompletedTimeUtc = completedTimeUtc,
                    ErrorCode = errorCode,
                    Summary = summary,
                    HandlerType = handlerType,
                })
                .Where(e => e.Id == executionId && e.Fence == fence && e.Status == WfNodeExecutionStatus.Running)
                .ExecuteCommandAsync();
        }

        if (affected != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.InstanceStatusConflict,
                new Dictionary<string, object?>
                {
                    ["reason"] = "executionFenceConflict",
                    ["executionId"] = executionId,
                    ["fence"] = fence,
                });
        }
    }

    /// <summary>
    /// 纯函数,按 handler 结果算出落库判定。<b>前置判定优先</b>:实例已非 <see cref="WfInstanceStatus.Running"/>
    /// 或 token 已非 <see cref="WfTokenStatus.Active"/>(外部撤销/终止)→ 无论 handler 说了什么,一律
    /// <see cref="WfNodeExecutionStatus.Cancelled"/>——handler 压根不知道实例已被撤销,这条判定就是 fence/CAS
    /// 之外的第二道"丢弃迟到结果"防线。
    /// <para>重试预算判定:<c>execution.AttemptCount &gt;= Math.Max(execution.MaxAttempts, 1)</c>——
    /// <c>AttemptCount</c> 是领取后读回的值(1 基),<c>MaxAttempts &lt;= 0</c> 按 1 处理(=不重试,按字面
    /// 当"无限"是跑飞的配方)。</para>
    /// </summary>
    protected virtual WfExecutionOutcome ResolveExecutionOutcome(
        WfNodeExecution execution,
        WfInstance instance,
        WfToken token,
        WfNodeExecutionResult result,
        DateTime nowUtc)
    {
        if (instance.Status != WfInstanceStatus.Running || token.Status != WfTokenStatus.Active)
            return new WfExecutionOutcome(WfNodeExecutionStatus.Cancelled, null, nowUtc, null, null, true);

        switch (result.Type)
        {
            case WfNodeExecutionResultType.Succeeded:
                return new WfExecutionOutcome(WfNodeExecutionStatus.Succeeded, null, nowUtc, null, null, true);

            case WfNodeExecutionResultType.RetryableFailure:
                var budgetExhausted = execution.AttemptCount >= Math.Max(execution.MaxAttempts, 1);
                if (budgetExhausted)
                {
                    return new WfExecutionOutcome(
                        WfNodeExecutionStatus.Failed, null, nowUtc,
                        result.ErrorCode, WfNodeExecutionAttemptStore.Truncate(result.Summary), true);
                }

                var delay = ResolveRetryDelay(execution, result);
                return new WfExecutionOutcome(
                    WfNodeExecutionStatus.RetryScheduled, nowUtc + delay, null, null, null, false);

            case WfNodeExecutionResultType.ManualFallback:
                return new WfExecutionOutcome(
                    WfNodeExecutionStatus.ManualFallback, null, nowUtc,
                    result.ErrorCode, WfNodeExecutionAttemptStore.Truncate(result.Summary), true);

            case WfNodeExecutionResultType.TerminalFailure:
                return new WfExecutionOutcome(
                    WfNodeExecutionStatus.Failed, null, nowUtc,
                    result.ErrorCode, WfNodeExecutionAttemptStore.Truncate(result.Summary), true);

            default:
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed,
                    new Dictionary<string, object?> { ["resultType"] = result.Type.ToString() });
        }
    }

    /// <summary>
    /// 重试退避:<see cref="WfNodeExecutionResult.RetryAfter"/> 在 <c>(0, 24h]</c> 内则用它;否则(含 <c>null</c>、
    /// <c>&lt;= 0</c>、<c>&gt; 24h</c>)按 <c>30s &lt;&lt; min(AttemptCount - 1, 5)</c> 指数退避,封顶约 16 分钟。
    /// <para>上下界钳制是必须实现的、不是优化:<see cref="WfNodeExecutionResult.RetryAfter"/> 由 handler(消费者
    /// 代码)提供,是 trust boundary。<see cref="TimeSpan.Zero"/> → <c>NextRetryAtUtc &lt;= now</c> → 热循环;
    /// 过大的值 → <c>nowUtc + delay</c> 逼近 <see cref="DateTime.MaxValue"/>,四库列写入行为各不相同。</para>
    /// </summary>
    protected virtual TimeSpan ResolveRetryDelay(WfNodeExecution execution, WfNodeExecutionResult result)
    {
        if (result.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero && retryAfter <= MaxRetryAfter)
            return retryAfter;

        var shift = Math.Min(execution.AttemptCount - 1, 5);
        return TimeSpan.FromSeconds(RetryBaseSeconds << shift);
    }

    /// <summary>
    /// outbox payload(§4.6 D6):不含 <see cref="WfNodeExecutionResult.OutputJson"/> 正文——handler 输出是
    /// PII/密钥泄漏面最大的一处,outbox 又是要投给进程外消费方的,正文进去等于把脱敏责任推给每个消费者;
    /// 消费方要正文,用 <c>executionKey</c> 回查。走 <see cref="WfModelJson.Options"/>,不另起一份配置。
    /// </summary>
    protected virtual string BuildExecutionOutboxPayload(
        WfNodeExecution execution,
        NodeExecutionCompletedCmd cmd,
        WfNodeExecutionAttempt attempt,
        WfExecutionOutcome outcome)
    {
        return System.Text.Json.JsonSerializer.Serialize(
            new
            {
                executionKey = execution.ExecutionKey,
                executionId = execution.Id,
                instanceId = execution.InstanceId,
                tokenId = execution.TokenId,
                nodeVisitId = execution.NodeVisitId,
                nodeId = execution.NodeId,
                nodeType = execution.NodeType,
                definitionVersionId = execution.DefinitionVersionId,
                status = outcome.Status,
                attemptNo = attempt.AttemptNo,
                fence = cmd.Fence,
                errorCode = outcome.ErrorCode,
                summary = outcome.Summary,
                outputHash = attempt.OutputHash,
                completedAtUtc = outcome.CompletedTimeUtc,
            },
            WfModelJson.Options);
    }
}
