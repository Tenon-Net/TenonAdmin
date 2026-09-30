using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>接入应用分页查询入参。</summary>
public record IntegrationAppPageInput : PageInputBase
{
    /// <summary>编码或名称关键字(模糊)。</summary>
    public string? Keyword { get; init; }

    /// <summary>按启用状态过滤;空=全部。</summary>
    public bool? Enabled { get; init; }
}

/// <summary>新增接入应用入参。</summary>
public record IntegrationAppCreateInput
{
    /// <summary>应用编码(唯一,2–64 位字母数字及 <c>._-</c>,以字母或数字开头;创建后不可修改)。</summary>
    public string Code { get; init; } = "";

    /// <summary>显示名称(1–64)。</summary>
    public string Name { get; init; } = "";

    /// <summary>说明(≤256)。</summary>
    public string? Description { get; init; }

    /// <summary>归属机构 Id(应用写入业务行的数据范围锚点);可空。</summary>
    public long? OwnerOrgId { get; init; }

    /// <summary>每分钟调用上限;空=模块默认,0=不限。</summary>
    public int? RateLimitPerMinute { get; init; }

    /// <summary>是否启用(默认启用)。</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>修改接入应用入参(编码与启停不在此修改:启停走独立接口,便于授权与审计)。</summary>
public record IntegrationAppUpdateInput
{
    /// <summary>显示名称(1–64)。</summary>
    public string Name { get; init; } = "";

    /// <summary>说明(≤256)。</summary>
    public string? Description { get; init; }

    /// <summary>归属机构 Id;可空。</summary>
    public long? OwnerOrgId { get; init; }

    /// <summary>每分钟调用上限;空=模块默认,0=不限。</summary>
    public int? RateLimitPerMinute { get; init; }
}

/// <summary>接入凭据状态(按当前时刻计算,不落库)。</summary>
public enum OpenAppCredentialStatus
{
    /// <summary>有效</summary>
    Active = 0,

    /// <summary>已过期</summary>
    Expired = 1,

    /// <summary>已撤销</summary>
    Revoked = 2,
}

/// <summary>接入应用列表行。</summary>
public record IntegrationAppListItem
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool Enabled { get; init; }
    public long? OwnerOrgId { get; init; }
    public string? OwnerOrgName { get; init; }
    public int? RateLimitPerMinute { get; init; }

    /// <summary>当前有效凭据数。</summary>
    public int ActiveCredentialCount { get; init; }

    /// <summary>全部凭据中最近一次使用时刻(UTC)。</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTime CreateTime { get; init; }
}

/// <summary>接入应用详情(含凭据视图,不含任何秘密或摘要)。</summary>
public record IntegrationAppDetail : IntegrationAppListItem
{
    public DateTime? UpdateTime { get; init; }

    /// <summary>凭据列表(新在前)。</summary>
    public IReadOnlyList<OpenAppCredentialView> Credentials { get; init; } = [];
}

/// <summary>凭据视图:只含公开信息,永不包含秘密与摘要。</summary>
public record OpenAppCredentialView
{
    public long Id { get; init; }
    public long AppId { get; init; }

    /// <summary>公开凭据标识(完整凭据形如 <c>tna_{KeyId}.****</c>)。</summary>
    public string KeyId { get; init; } = "";

    public string? Name { get; init; }
    public OpenAppCredentialStatus Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public long? RevokedBy { get; init; }
    public long? RotatedFromId { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 发放结果:<see cref="ApiKey"/> 是完整凭据原文,<b>只在本次响应出现一次</b>,服务端不保存、之后无法再次查看。
/// </summary>
public record OpenAppCredentialIssued
{
    public OpenAppCredentialView Credential { get; init; } = new();

    /// <summary>完整凭据原文(调用方放在请求头里);丢失只能轮换或重新发放。</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>防止日志或调试输出意外带出凭据原文。</summary>
    public override string ToString() => $"OpenAppCredentialIssued {{ KeyId = {Credential.KeyId} }}";
}

/// <summary>发放凭据入参。</summary>
public record OpenAppCredentialCreateInput
{
    /// <summary>凭据名称(≤64)。</summary>
    public string? Name { get; init; }

    /// <summary>到期时刻;空且 <see cref="NeverExpires"/>=false 时按默认有效期。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>不过期(忽略 <see cref="ExpiresAt"/>)。</summary>
    public bool NeverExpires { get; init; }
}

/// <summary>轮换凭据入参:签发新凭据,旧凭据在并存窗口结束时过期。</summary>
public record OpenAppCredentialRotateInput
{
    /// <summary>旧凭据的并存窗口(小时);空=默认,0=立即失效。</summary>
    public int? OverlapHours { get; init; }

    /// <summary>新凭据名称;空则沿用旧凭据名称。</summary>
    public string? Name { get; init; }

    /// <summary>新凭据到期时刻;空且 <see cref="NeverExpires"/>=false 时按默认有效期。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>新凭据不过期。</summary>
    public bool NeverExpires { get; init; }
}

/// <summary>调整凭据到期时间入参(缩短或延长;已撤销凭据不可调整)。</summary>
public record OpenAppCredentialExpiryInput
{
    /// <summary>新的到期时刻;<see cref="NeverExpires"/>=true 时忽略。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>改为不过期。</summary>
    public bool NeverExpires { get; init; }
}
