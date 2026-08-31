namespace TenonAdmin.Workflow;

/// <summary>
/// 一次写操作的幂等身份(六个维度 + 算好的 <see cref="IdentityHash"/>)。
/// <para>存在的理由是<b>杜绝「入库值 ≠ 算 hash 的值」</b>:归一化只在 <see cref="Create"/> 里发生一次,
/// 之后服务落库用的就是本对象的属性,不再有第二条 trim / 哨兵路径可以走偏。</para>
/// </summary>
public sealed class WfOperationIdentity
{
    private WfOperationIdentity(
        string scopeKey,
        WfCommandType commandType,
        WfTargetType targetType,
        long targetId,
        long actorUserId,
        string requestKey,
        string identityHash)
    {
        ScopeKey = scopeKey;
        CommandType = commandType;
        TargetType = targetType;
        TargetId = targetId;
        ActorUserId = actorUserId;
        RequestKey = requestKey;
        IdentityHash = identityHash;
    }

    /// <summary>已归一化的机构/租户范围键(无机构为 <see cref="WfIdentityHash.ScopeSentinel"/>)。</summary>
    public string ScopeKey { get; }

    public WfCommandType CommandType { get; }

    public WfTargetType TargetType { get; }

    /// <summary>实例 Id / 待办 Id / 定义版本 Id(<see cref="WfCommandType.Start"/>)。</summary>
    public long TargetId { get; }

    public long ActorUserId { get; }

    /// <summary>已 <c>Trim()</c> 的客户端 request key。</summary>
    public string RequestKey { get; }

    /// <summary>上述六维的 SHA-256 小写 hex;<c>wf_operation_receipt</c> 的唯一键。</summary>
    public string IdentityHash { get; }

    /// <summary>归一化六维并算出 hash。校验规则与异常见 <see cref="WfIdentityHash.Compute"/>。</summary>
    public static WfOperationIdentity Create(
        string? scopeKey,
        WfCommandType commandType,
        WfTargetType targetType,
        long targetId,
        long actorUserId,
        string requestKey) =>
        new(
            WfIdentityHash.NormalizeScopeKey(scopeKey),
            commandType,
            targetType,
            targetId,
            actorUserId,
            WfIdentityHash.NormalizeRequestKey(requestKey),
            WfIdentityHash.Compute(scopeKey, commandType, targetType, targetId, actorUserId, requestKey));
}
