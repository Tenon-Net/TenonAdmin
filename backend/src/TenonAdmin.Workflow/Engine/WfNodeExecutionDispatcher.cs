using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// Execution dispatcher(M3a-1 Task 6)——「领取 → 调 handler → 落结果」的唯一装配点,第一次把
/// Task 2(SPI)/3(lease-fence 领取)/4(attempt)/5(outbox)串起来。
/// <para><b>事务边界(三段式,形状写死)</b>:
/// 1) tx1——<see cref="WfNodeExecutionStore.ClaimAsync"/> 包在 <c>UseTranAsync</c> 里,事务内只有那一次条件
/// UPDATE + 读回;领不到返回 <c>null</c>,本拍结束。
/// 2) 无事务——读实例/token/定义版本/模型(只读快照,刻意不进事务)→ 组 <see cref="WfNodeExecutionContext"/> →
/// 调 <see cref="IWorkflowNodeHandler.ExecuteAsync"/>。<b>handler 执行时刻意没有活动数据库事务</b>(AI 基石
/// §4.6/§4.8 硬约束——远程调用不得发生在事务内)。
/// 3) tx2——<see cref="IWorkflowEngine.ExecuteAsync"/> 消化 <see cref="NodeExecutionCompletedCmd"/>,那本来
/// 就是「一条 Cmd 一个 DB 事务」,于是 attempt/变量/历史/outbox/token 推进自动落在同一短事务里,dispatcher
/// 不必自己再嵌一层。</para>
/// <para><b>零 DI 注册、零新接口、零后台扫描循环</b>——与 <see cref="WfNodeExecutionStore"/>/
/// <see cref="WfNodeExecutionAttemptStore"/>/<see cref="WfOutboxStore"/> 同款「零注册,调用方直接 <c>new</c>
/// 或经 <c>ISqlSugarClient</c> 调用」。第一条注册线、Webhook 的 <c>EnterNodeOp</c> 接线、后台扫描 job 都是
/// Task 8 的活——本轮生产侧一个 <see cref="IWorkflowNodeHandler"/> 实现都没有,注册一个解析不到任何实现的
/// 服务等于死接线,也会让「十件套」(<c>WorkflowReplaceabilityTests</c>)的注释失真。类不 <c>sealed</c>、
/// 方法全 <c>virtual</c>,可替换的缝留在调用方(消费者继承覆写单步,而不是复制整个类)。</para>
/// <para><b>没有匹配的 handler 时不抛异常</b>:合成一个 <see cref="WfNodeExecutionResult.TerminalFailure"/>
/// 走正常回写路径,execution 落 <see cref="WfNodeExecutionStatus.Failed"/>。抛异常的话 tx2 从不发生,行停在
/// <see cref="WfNodeExecutionStatus.Running"/> 持租约,租约过期后被重新领取、再抛——无限活锁,且排查时
/// attempt 表一行记录都没有。合成 <c>TerminalFailure</c> 让「装错包/漏注册」变成一行可查的 attempt + 一个
/// 终态。</para>
/// <para><b>handler 抛出的任何异常(含 <see cref="OperationCanceledException"/>)一律不 catch</b>——异常穿透
/// 给调用方,tx2 从未开始,execution 行停在 <see cref="WfNodeExecutionStatus.Running"/> 持租约,租约到期后可
/// 被重新领取(Task 7 的崩溃恢复路径)。取消语义上不等于 <c>TerminalFailure</c>(那是「永不重试」,取消是
/// 「这次没跑完,应该被重新领取」),dispatcher 加一层兜底 catch 会把这两个方向相反的语义悄悄合并,同时把
/// Task 8「把网络异常/超时映射成 RetryableFailure/TerminalFailure」的分类规则架空。</para>
/// <para><see cref="DateTimeOffset"/>(SPI)↔ <see cref="DateTime"/>(持久化列)的转换落点就在本类
/// (<see cref="BuildContextAsync"/>):SqlSugar 读回的 <c>DateTime</c> 是 <c>Kind.Unspecified</c>,必须先
/// <c>DateTime.SpecifyKind(x, DateTimeKind.Utc)</c> 再构造 <see cref="DateTimeOffset"/>,否则在非 UTC 机器
/// 上会按本机时区悄悄偏移(错 8 小时那类 bug)。</para>
/// </summary>
public class WfNodeExecutionDispatcher(
    ISqlSugarClient db,
    IEnumerable<IWorkflowNodeHandler> handlers,
    IWorkflowEngine engine,
    TimeProvider time)
{
    /// <summary>
    /// 跑一拍:领取 →(领不到即返回 <c>null</c>,不是错误)→ 事务外调 handler → 回写。返回回写后的最终
    /// 行状态——由重查一次数据库得到,而不是从内部判定反推,一次 SELECT 换来不依赖上面每条分支都正确的
    /// 诚实结果。
    /// </summary>
    public virtual async Task<WfNodeExecutionStatus?> RunAsync(
        long executionId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = time.GetUtcNow().UtcDateTime;

        // tx1(领取):事务内只有 ClaimAsync 那一次条件 UPDATE + 读回(其类注释明写"必须在事务内才成立")。
        var tran = await db.Ado.UseTranAsync(() =>
            WfNodeExecutionStore.ClaimAsync(db, executionId, owner, nowUtc, leaseDuration, cancellationToken));
        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        if (tran.Data is null)
            return null; // 本拍没领到。

        var claimed = tran.Data;

        // 无事务:只读快照 + 组上下文;handler 调用不得落在任何事务里。
        var context = await BuildContextAsync(claimed, leaseDuration, cancellationToken);

        var handler = ResolveHandler(claimed);
        var startedAtUtc = time.GetUtcNow().UtcDateTime;
        var result = handler is null
            ? WfNodeExecutionResult.TerminalFailure(
                errorCode: WorkflowErrorCode.NodeTypeUnsupported,
                summary: $"未注册 IWorkflowNodeHandler:{claimed.NodeType}")
            : await InvokeHandlerAsync(handler, context, cancellationToken);
        var endedAtUtc = time.GetUtcNow().UtcDateTime;

        // tx2(回写)就是引擎自己的那一个事务——attempt/变量/历史/outbox/token 推进因此自动同一短事务提交,
        // dispatcher 不必再嵌一层。
        await engine.ExecuteAsync(
            new NodeExecutionCompletedCmd
            {
                ExecutionId = claimed.Id,
                Fence = claimed.Fence,
                Result = result,
                HandlerType = handler?.GetType().FullName,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
            },
            cancellationToken);

        return await db.Queryable<WfNodeExecution>()
            .Where(e => e.Id == claimed.Id)
            .Select(e => e.Status)
            .FirstAsync();
    }

    /// <summary>
    /// 按 <see cref="IWorkflowNodeHandler.NodeType"/> 挑实现(同 <c>IAdminJob</c>/<c>DefaultJobHandlerResolver</c>
    /// 范式,不用 keyed DI)。找不到返回 <c>null</c>——调用方合成 <c>TerminalFailure</c>,不抛异常。
    /// </summary>
    protected virtual IWorkflowNodeHandler? ResolveHandler(WfNodeExecution execution) =>
        handlers.FirstOrDefault(h => h.NodeType == execution.NodeType);

    /// <summary>
    /// 只读快照组上下文:instance(<b><c>.ClearFilter&lt;IOrgScoped&gt;()</c></b>——后台 worker 没有 HTTP
    /// 请求上下文,<c>IDataScopeContext</c> 是空的,不清过滤器会让本查询静默返回 0 行,症状是「调度器永远
    /// 说实例不存在」)/ token / 定义版本 / 模型,投影出 handler 只读的 14 个字段。
    /// <para>节点查找用 <see cref="WfModelIndex.Build"/> 自己反序列化出的快照实例,<b>不得共享引擎内部那棵
    /// 活树上的节点对象</b>(语义契约 Task 2 定案)——dispatcher 与引擎各自反序列化一次 <c>ModelJson</c>,
    /// 天然满足这条隔离。</para>
    /// </summary>
    protected virtual async Task<WfNodeExecutionContext> BuildContextAsync(
        WfNodeExecution execution,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        var nowUtc = time.GetUtcNow().UtcDateTime;
        // 没有配置源的截止时刻就用租约到期时刻现算——诚实的截止时刻,而不是编一个假数据。
        var deadlineAtUtc = execution.DeadlineAtUtc ?? nowUtc + leaseDuration;

        return new WfNodeExecutionContext
        {
            ExecutionKey = execution.ExecutionKey,
            InstanceId = execution.InstanceId,
            TokenId = execution.TokenId,
            NodeVisitId = execution.NodeVisitId,
            NodeId = execution.NodeId,
            NodeType = execution.NodeType,
            DefinitionVersionId = execution.DefinitionVersionId,
            OrgId = instance.CreateOrgId,
            StarterUserId = instance.StarterUserId,
            BusinessKey = instance.BusinessKey,
            NodeProps = node.Props,
            VariablesJson = instance.VariablesJson,
            Attempt = execution.AttemptCount, // 领取读回后的值,绝不 +1(三处口径必须对齐)。
            // SqlSugar 读回的 DateTime 是 Kind.Unspecified,必须先 SpecifyKind(Utc) 再转 DateTimeOffset,
            // 否则在非 UTC 机器上会按本机时区悄悄偏移。
            DeadlineAtUtc = new DateTimeOffset(DateTime.SpecifyKind(deadlineAtUtc, DateTimeKind.Utc)),
        };
    }

    /// <summary>调 handler。<b>不 catch 任何异常</b>(含 <see cref="OperationCanceledException"/>)——见类注释。</summary>
    protected virtual Task<WfNodeExecutionResult> InvokeHandlerAsync(
        IWorkflowNodeHandler handler,
        WfNodeExecutionContext context,
        CancellationToken cancellationToken) =>
        handler.ExecuteAsync(context, cancellationToken);
}
