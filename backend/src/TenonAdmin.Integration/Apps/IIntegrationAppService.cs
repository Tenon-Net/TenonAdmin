using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 接入应用管理(实现契约 §4、§12)。启停、删除与影响运行时的字段变更都会原子递增 <see cref="IntegrationApp.StateVersion"/>。
/// <para>默认实现 <see cref="IntegrationAppService"/>,<c>TryAddScoped</c> 注册、方法 virtual;前置注册同接口即整体替换。</para>
/// </summary>
public interface IIntegrationAppService
{
    /// <summary>分页查询(含有效凭据数与最近使用时间)。</summary>
    Task<PagedList<IntegrationAppListItem>> PageAsync(IntegrationAppPageInput input, CancellationToken cancellationToken = default);

    /// <summary>详情(含凭据视图,不含秘密)。不存在抛 <see cref="IntegrationErrorCode.AppNotFound"/>。</summary>
    Task<IntegrationAppDetail> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>新增,返回应用 Id。</summary>
    Task<long> AddAsync(IntegrationAppCreateInput input, CancellationToken cancellationToken = default);

    /// <summary>修改名称、说明、归属机构与限流。</summary>
    Task UpdateAsync(long id, IntegrationAppUpdateInput input, CancellationToken cancellationToken = default);

    /// <summary>启用或停用;停用后全部凭据立即无效(所有副本下一次请求生效)。</summary>
    Task SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>删除(软删)并撤销其全部有效凭据。</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 接入凭据生命周期(实现契约 §4.1):发放、轮换(新旧有限并存)、撤销、删除、调整到期。秘密只在发放/轮换结果里出现一次。
/// <para>默认实现 <see cref="OpenAppCredentialService"/>,<c>TryAddScoped</c> 注册、方法 virtual;前置注册同接口即整体替换。</para>
/// </summary>
public interface IOpenAppCredentialService
{
    /// <summary>列出应用的全部凭据(新在前,不含秘密)。</summary>
    Task<IReadOnlyList<OpenAppCredentialView>> ListAsync(long appId, CancellationToken cancellationToken = default);

    /// <summary>发放新凭据;超出有效凭据上限抛 <see cref="IntegrationErrorCode.CredentialLimitExceeded"/>。</summary>
    Task<OpenAppCredentialIssued> CreateAsync(long appId, OpenAppCredentialCreateInput input, CancellationToken cancellationToken = default);

    /// <summary>轮换:签发新凭据,并把旧凭据到期时间收紧到「现在 + 并存窗口」(已更早到期则不变)。</summary>
    Task<OpenAppCredentialIssued> RotateAsync(long appId, long credentialId, OpenAppCredentialRotateInput input, CancellationToken cancellationToken = default);

    /// <summary>撤销(立即、永久);已撤销的再次撤销为空操作。</summary>
    Task RevokeAsync(long appId, long credentialId, CancellationToken cancellationToken = default);

    /// <summary>软删已撤销凭据;未撤销凭据不可删除,审计行保留。</summary>
    Task DeleteAsync(long appId, long credentialId, CancellationToken cancellationToken = default);

    /// <summary>调整到期时间(缩短、延长或改为不过期);已撤销凭据不可调整。</summary>
    Task SetExpiryAsync(long appId, long credentialId, OpenAppCredentialExpiryInput input, CancellationToken cancellationToken = default);
}
