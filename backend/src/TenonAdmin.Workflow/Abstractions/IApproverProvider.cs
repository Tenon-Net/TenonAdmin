using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// 审批人解析 SPI:节点存 provider 键 + 参数,运行时解析成具体用户 Id。
/// 内置 8 种经 <c>TryAddEnumerable</c> 注册;消费者可再注册自有键(如 HRBP),
/// 或前置注册同 <see cref="Key"/> 的实现以覆盖内置(解析器 <c>FirstOrDefault</c> 取先注册者)。
/// 整体替换分发逻辑则前置注册 <see cref="IApproverResolver"/>。
/// </summary>
public interface IApproverProvider
{
    /// <summary>Provider 键(写入流程定义 JSON 的 <c>assignee.provider</c>)。</summary>
    string Key { get; }

    /// <summary>
    /// 解析为用户 Id 列表。空列表 = 无人,由引擎按空审批人策略处置。
    /// <see cref="ApproverProviderKeys.MultiLeader"/> 返回的是有序链(引擎展开为串行任务);
    /// 其余一般为无序集合(会签/或签由节点 <c>mode</c> 决定)。
    /// </summary>
    Task<IReadOnlyList<long>> ResolveAsync(ApproverResolveContext context, CancellationToken cancellationToken = default);
}

/// <summary>一次审批人解析的输入上下文(由引擎在进入审批/抄送节点时组装)。</summary>
public sealed class ApproverResolveContext
{
    /// <summary>发起人用户 Id。</summary>
    public required long InitiatorUserId { get; init; }

    /// <summary>发起人主属机构 Id;可空(未分配)。</summary>
    public long? InitiatorOrgId { get; init; }

    /// <summary>节点 <c>assignee.params</c>;形状由各 Provider 自定。</summary>
    public Dictionary<string, JsonElement>? Params { get; init; }

    /// <summary>
    /// 发起时提交的自选审批人 Id(仅 <see cref="ApproverProviderKeys.SelfSelect"/> 使用;
    /// 其它 Provider 忽略)。
    /// </summary>
    public IReadOnlyList<long>? SelectedUserIds { get; init; }

    /// <summary>
    /// 发起时按 <c>level</c> 快照的连续多级主管链(仅 <see cref="ApproverProviderKeys.MultiLeader"/> 使用)。
    /// <c>null</c> = 没有快照(计算快照本身,或老实例未曾快照)→ Provider 应回退实时上溯;
    /// 字典命中当前 <c>level</c> 时,即使对应链为空也必须原样返回,<b>不得</b>回退查库。
    /// 字典缺少当前 <c>level</c> 只可能来自异常存量数据,按安全兼容方向回退实时解析。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<long>>? LeaderChainByLevel { get; init; }
}

/// <summary>
/// 按键分发到 <see cref="IApproverProvider"/>。内置 <see cref="DefaultApproverResolver"/> 经 <c>TryAdd</c> 注册,
/// 消费者前置注册同接口即可整体替换。
/// </summary>
public interface IApproverResolver
{
    /// <summary>按 <paramref name="providerKey"/> 解析审批人;未知键抛 48xxx <c>AdminException</c>。</summary>
    Task<IReadOnlyList<long>> ResolveAsync(
        string providerKey,
        ApproverResolveContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>内置 8 种审批人 Provider 的键(与设计草案 JSON <c>assignee.provider</c> 对齐)。</summary>
public static class ApproverProviderKeys
{
    /// <summary>指定成员。<c>params.userIds</c>(数组)或 <c>params.userId</c>。</summary>
    public const string User = "user";

    /// <summary>直属主管第 N 级。<c>params.level</c>(默认 1);沿 <c>SysUser.DirectorId</c> 上溯 N 步,只取该级一人。</summary>
    public const string Leader = "leader";

    /// <summary>
    /// 连续多级主管。<c>params.level</c>=最多级数;沿 <c>DirectorId</c> 收集 1..N 级有序链,
    /// 引擎展开为动态串行任务(见 CONTEXT「连续多级主管」)。
    /// </summary>
    public const string MultiLeader = "multiLeader";

    /// <summary>角色。<c>params.roleId</c> 或 <c>params.roleIds</c>。</summary>
    public const string Role = "role";

    /// <summary>职位。<c>params.positionId</c>;可选 <c>params.orgId</c> 限机构。</summary>
    public const string Position = "position";

    /// <summary>发起人自选。用户 Id 来自 <see cref="ApproverResolveContext.SelectedUserIds"/>。</summary>
    public const string SelfSelect = "selfSelect";

    /// <summary>发起人本人。无参数。</summary>
    public const string Initiator = "initiator";

    /// <summary>机构负责人。读发起人主属机构的 <c>SysOrg.LeaderUserId</c>。</summary>
    public const string OrgLeader = "orgLeader";
}
