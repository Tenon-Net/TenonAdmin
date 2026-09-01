using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 一次 <see cref="IWorkflowEngine.ExecuteAsync"/> 的事务内共享状态。
/// 无跨请求内存;崩溃恢复靠表状态,本对象仅存活于单个 Agenda 循环。
/// </summary>
public sealed class WfExecutionContext
{
    public required ISqlSugarClient Db { get; init; }
    public required WfAgenda Agenda { get; init; }
    public required IApproverResolver ApproverResolver { get; init; }
    public required IWorkflowFormBinder FormBinder { get; init; }
    public required WorkflowOptions Options { get; init; }
    public required TimeProvider TimeProvider { get; init; }
    public required IWfConditionEvaluator ConditionEvaluator { get; init; }
    public required IWorkflowNotifier Notifier { get; init; }

    public required WfInstance Instance { get; set; }
    public required WfToken Token { get; set; }
    public required WfModel Model { get; init; }
    public required WfDefinitionVersion DefinitionVersion { get; init; }

    /// <summary>按节点 Id 持久化的发起人自选审批人。</summary>
    public IReadOnlyDictionary<string, List<long>> SelectedUserIdsByNode { get; init; } =
        new Dictionary<string, List<long>>(StringComparer.Ordinal);

    /// <summary>发起人主属机构。</summary>
    public long? StarterOrgId { get; init; }

    /// <summary>
    /// 本次命令的幂等请求键;<c>null</c> = 没有请求身份(没带 key 的请求,或系统派的
    /// <see cref="TimeoutFireCmd"/>)。落进本次产生的每一条 <see cref="WfHistory.RequestId"/>。
    /// <para><b>刻意声明成 <c>required</c></b>:值必须在**构造 ctx 时**就带上,因为
    /// <see cref="WorkflowEngine.BeginStartAsync"/> 紧接着就写 <c>InstanceStarted</c> —— 换成
    /// "switch 之后再赋值"会让每条命令最有价值的第一行永远为空。 让"将来新加一个
    /// <c>BeginXxxAsync</c> 却忘了带上"变成**编译错误**,而不是一条悄悄丢了身份的历史。</para>
    /// </summary>
    public required string? RequestId { get; init; }

    /// <summary>
    /// 本次命令写进每一条 <see cref="WfHistory.ActorType"/> 的行为者类型(M3a-1)。<b>刻意声明成
    /// <c>required</c></b>:理由与 <see cref="RequestId"/> 那段一致——漏填不该是一条悄悄记成
    /// <see cref="WfHistoryActorType.Unknown"/> 的历史,而应该是编译错误。
    /// </summary>
    public required WfHistoryActorType ActorType { get; init; }

    /// <summary>本次命令写进每一条 <see cref="WfHistory.ActorUserId"/> 的用户 Id;系统/超时命令为 <c>null</c>。</summary>
    public required long? ActorUserId { get; init; }

    /// <summary><see cref="EnterNodeOp"/> 生成 <see cref="WfToken.NodeVisitId"/> 用的雪花发号器(M3a-1)。</summary>
    public required IIdGenerator IdGenerator { get; init; }

    /// <summary>
    /// 发起时按 <c>level</c> 快照的连续多级主管链;<c>null</c>=无快照(老实例或模型无 multiLeader 节点)。
    /// 命中 level 的空数组表示快照过但该级链为空。透传给 <see cref="ApproverResolveContext.LeaderChainByLevel"/>。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<long>>? LeaderChainByLevel { get; init; }

    /// <summary>当前 Agenda 步关联的节点(Enter/Leave 时更新)。</summary>
    public WfNode? CurrentNode { get; set; }

    // ── 结果累加(事务提交后读) ──

    public long? CreatedTaskId { get; set; }
    public List<long> NewAssigneeUserIds { get; } = [];
    public List<long> NewCcUserIds { get; } = [];

    /// <summary>
    /// 待派发的「待办到达」通知(事务提交后由 <see cref="WorkflowEngine"/> 统一派发,不在事务内直接调用
    /// <see cref="Notifier"/>——避免提交失败仍推送、或推送先于提交落盘导致客户端读到脏数据)。
    /// </summary>
    public List<(WfNotifyContext Ctx, IReadOnlyList<long> UserIds)> PendingTaskAssignedNotifications { get; } = [];

    /// <summary>待派发的「实例完结」通知,语义同 <see cref="PendingTaskAssignedNotifications"/>。</summary>
    public WfNotifyContext? PendingInstanceCompletedNotification { get; set; }

    private WfModelIndex? _modelIndex;

    /// <summary>模型树索引:懒建一次并缓存;ctx 是单事务对象,树在整个 Agenda 循环内不变,无失效问题。</summary>
    private WfModelIndex ModelIndex => _modelIndex ??= WfModelIndex.Build(Model);

    /// <summary>按节点 Id 查找(含分支臂内的所有节点)。</summary>
    public WfNode? FindNode(string nodeId) => ModelIndex.Find(nodeId);

    /// <summary>
    /// 汇合目标:节点 <c>Next</c> 非 null 直接用;否则沿外层 branch 向上找第一个 <c>Next</c> 非 null 的
    /// 外层 branch 并返回其 <c>Next</c>;一路到顶仍无 → null(实例完结)。
    /// </summary>
    public WfNode? ResolveMergeTarget(WfNode from) => ModelIndex.ResolveMergeTarget(from);

    public IReadOnlyList<long>? GetSelectedUserIds(string nodeId) =>
        SelectedUserIdsByNode.TryGetValue(nodeId, out var ids) ? ids : null;

    /// <summary>
    /// 实例级乐观锁领取(数据库评审 §4.1):
    /// <c>WHERE Id = @id AND Status = @expectedStatus AND Version = @oldVersion</c> → <c>Version + 1</c>。
    /// 影响行数 ≠ 1 表示另一个事务已经把这个实例推走了 → <see cref="WorkflowErrorCode.InstanceStatusConflict"/>,
    /// 由 <see cref="WorkflowEngine.ExecuteAsync"/> 的「一条 Cmd 一个事务」整体回滚。
    /// <para><b>为什么是「先领取、再写状态」两条语句,而不是把状态挤进本语句</b>:
    /// <c>SetColumns</c> 走的是条件更新路径,<b>不触发</b> <c>SqlSugarSetup</c> 里只认
    /// <c>DataFilterType.UpdateByObject</c> 的审计 AOP(见 <c>SqlSugarRepository.SoftDeleteCoreAsync</c>
    /// 的同类注释)。把状态也写在这里,每个调用点都得手填 <c>UpdateTime</c>/<c>UpdateUserId</c>,而
    /// 「这次是谁做的」在六个落点各不相同。本类<b>没有</b> <c>ICurrentUser</c>,领取语句就算想手填
    /// <c>UpdateUserId</c> 也拿不到调用者 —— 多一条 UPDATE 不是「更高效的形状」,是 ctx 缺审计身份、
    /// 只能把状态写留给走 AOP 的整对象更新。正确性不打折:领取成功即持有该行排他锁直到提交,后一条
    /// 语句处在同一事务的锁保护区内。</para>
    /// <para><b>版本必须写回内存实例</b>(本方法末尾做):一个事务里可能领取多次(进节点 N 次 + 终态
    /// 一次),不写回,后一次 CAS 会对着旧版本号抛出一个<b>假的</b>冲突。</para>
    /// <para>非 <c>virtual</c> 不违反可替换性:本类是事务作用域的写步骤载体(<see cref="AppendHistoryAsync"/>
    /// 同款),可覆写的缝在<b>调用方</b>——<c>CancelInstanceOp.ExecuteAsync</c>、
    /// <c>TakeTransitionOp.CompleteInstanceAsync</c>、<c>CompleteTaskOp.RejectInstanceAsync</c> 都是
    /// <c>virtual</c>。</para>
    /// </summary>
    public async Task ClaimInstanceAsync(
        WfInstanceStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 先算进局部变量再进 SetColumns:内联表达式会被 SqlSugar 按当前区域设置格式化成字面量拼进 SQL
        // (台账陷阱记录有 zh-CN 下 DateTime 表达式炸出「near "下午"」的实测)。
        var current = Instance.Version;
        var next = current + 1;
        var claimed = await Db.Updateable<WfInstance>()
            .SetColumns(i => new WfInstance { Version = next })
            .Where(i => i.Id == Instance.Id && i.Status == expectedStatus && i.Version == current)
            .ExecuteCommandAsync();
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.InstanceStatusConflict,
                new Dictionary<string, object?>
                {
                    ["reason"] = "instanceVersionConflict",
                    ["instanceId"] = Instance.Id,
                });
        }

        Instance.Version = next;
    }

    /// <summary>
    /// token 级乐观锁领取,形状与语义同 <see cref="ClaimInstanceAsync"/>(数据库评审 §4.1)。
    /// <para><b>凡是写 <see cref="WfToken.NodeId"/> 或 <see cref="WfToken.Status"/> 之前都要先领取</b>——
    /// 换节点就是状态推进。<c>ReturnTaskOp</c> / <c>BeginResubmitAsync</c> / <c>EnterNodeOp</c> 三处的期望
    /// 状态与目标状态都是 <see cref="WfTokenStatus.Active"/>(退回与重提之后实例仍 Running,不是完结),
    /// 领取只推进版本、不翻状态。</para>
    /// <para>失败复用 <see cref="WorkflowErrorCode.InstanceStatusConflict"/> + <c>reason</c> 而不新造错误码:
    /// 任务级 CAS 输了统一是 <see cref="WorkflowErrorCode.TaskConflict"/>,那么实例/token 级 CAS 输了就统一
    /// 是 48004,一码多 <c>reason</c> 是本仓既有惯例。</para>
    /// </summary>
    public async Task ClaimTokenAsync(
        WfTokenStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Token.Version;
        var next = current + 1;
        var claimed = await Db.Updateable<WfToken>()
            .SetColumns(t => new WfToken { Version = next })
            .Where(t => t.Id == Token.Id && t.Status == expectedStatus && t.Version == current)
            .ExecuteCommandAsync();
        if (claimed != 1)
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.InstanceStatusConflict,
                new Dictionary<string, object?>
                {
                    ["reason"] = "tokenVersionConflict",
                    ["tokenId"] = Token.Id,
                });
        }

        Token.Version = next;
    }

    /// <summary>
    /// 实例终态写入的<b>唯一落点</b>:翻 <see cref="WfInstance.Status"/> 并盖
    /// <see cref="WfInstance.CompletedTime"/>,同一条 UPDATE 提交(数据库评审 §4.2 要求完结时间与状态原子写入)。
    /// <para>三个终态分支(<c>TakeTransitionOp.CompleteInstanceAsync</c> / <c>CompleteTaskOp</c> 的终止拒绝分支 /
    /// <c>CancelInstanceOp</c>)本来形状完全相同,收成一处是为了<b>下一个终态落点不可能漏填</b>完结时间。</para>
    /// <para><b>刻意不含 <see cref="ClaimInstanceAsync"/></b>:领取是实例级 CAS 的语义,三个调用点各有各的长注释
    /// 说明为什么在那里领取;收进来会把「仲裁」和「写状态」两件事焊死。调用顺序仍是「先领取、再写状态」。</para>
    /// <para>整对象 <c>Updateable</c> 走 <c>UpdateByObject</c>,审计 AOP 照旧填 <c>UpdateTime</c>/<c>UpdateUserId</c>
    /// (为什么状态写不能挤进领取语句,见 <see cref="ClaimInstanceAsync"/> 的注释)。</para>
    /// </summary>
    public async Task WriteInstanceTerminalStatusAsync(
        WfInstanceStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Instance.Status = status;
        // 直接赋值而非 ??=:实例进终态后回不来(BeginResubmitAsync 要求 Status == Running),一生只写一次;
        // 真出现二次写时也该记新的完结时间,而不是悄悄留住旧值。
        Instance.CompletedTime = TimeProvider.GetLocalNow().DateTime;
        await Db.Updateable(Instance)
            .UpdateColumns(i => new { i.Status, i.CompletedTime, i.UpdateTime, i.UpdateUserId })
            .ExecuteCommandAsync();
    }

    /// <summary>append-only 写一条历史事件。</summary>
    public async Task AppendHistoryAsync(
        WfHistoryEventType eventType,
        string? nodeId = null,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = new WfHistory
        {
            InstanceId = Instance.Id,
            EventType = eventType,
            NodeId = nodeId,
            // 20 个 AppendHistoryAsync 调用点都经过这里,所以请求键/行为者/token 身份只在这一行赋值。
            RequestId = RequestId,
            TokenId = Token.Id,
            NodeVisitId = Token.NodeVisitId,
            ActorType = ActorType,
            ActorUserId = ActorUserId,
            // PayloadVersion 由实体初始化器给 1,这里不写。
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, WfModelJson.Options),
        };
        row.Sequence = await WfHistorySequence.NextAsync(Db, Instance.Id);
        await Db.Insertable(row).ExecuteCommandAsync();
    }

    public WfEngineResult ToResult() => new()
    {
        InstanceId = Instance.Id,
        InstanceStatus = Instance.Status,
        CreatedTaskId = CreatedTaskId,
        NewAssigneeUserIds = NewAssigneeUserIds,
        NewCcUserIds = NewCcUserIds,
    };
}
