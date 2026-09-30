namespace TenonAdmin.Integration;

/// <summary>应用授权视图:已授予的权限码,以及其中已不对应任何现存端点的码(端点被删或改路由)。</summary>
public record OpenAppGrantView
{
    /// <summary>已授予的规范化路由权限码。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>已授予但清单里已不存在的权限码(授权不再生效,供管理员清理)。</summary>
    public IReadOnlyList<string> Stale { get; init; } = [];
}

/// <summary>修改应用授权入参(整组替换)。</summary>
public record OpenAppGrantInput
{
    /// <summary>授予的权限码集合;必须全部对应现存开放端点。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>应用范围绑定视图。</summary>
public record OpenAppScopeBindingView
{
    public string ScopeKey { get; init; } = "";

    /// <summary>策略显示名;策略已不存在时为 null(绑定不再生效)。</summary>
    public string? ScopeName { get; init; }

    public bool AllValues { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>一条范围绑定入参。</summary>
public record OpenAppScopeBindingInput
{
    public string ScopeKey { get; init; } = "";

    /// <summary>显式授予全部数据(全量须显式声明)。</summary>
    public bool AllValues { get; init; }

    /// <summary>绑定值;<see cref="AllValues"/>=false 时不能为空。</summary>
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>修改应用范围绑定入参(整组替换)。</summary>
public record OpenAppScopeInput
{
    public IReadOnlyList<OpenAppScopeBindingInput> Bindings { get; init; } = [];
}

/// <summary>
/// 运行时授权快照:按 (AppId, StateVersion) 缓存。版本随每次凭据校验从库里读出,授权或范围一变版本即变,
/// 因此任何副本都不会用到旧授权(内存缓存与 Redis 行为一致)。
/// </summary>
/// <param name="AppId">应用 Id</param>
/// <param name="StateVersion">构建时的应用状态版本</param>
/// <param name="Permissions">已授予的权限码</param>
/// <param name="Scopes">范围绑定</param>
/// <param name="RateLimitPerMinute">应用单独设置的每分钟上限;null 用模块默认,0 表示不限</param>
public sealed record OpenAppAuthorizationSnapshot(
    long AppId,
    long StateVersion,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<OpenAppScopeBinding> Scopes,
    int? RateLimitPerMinute = null);

/// <summary>
/// 接入应用授权与范围绑定(实现契约 §4.4–§4.6):后台管理(整组替换 + 原子递增应用版本)与运行时快照。
/// <para>默认实现 <see cref="OpenAppAuthorizationService"/>,<c>TryAddScoped</c> 注册、方法 virtual;前置注册同接口即整体替换。</para>
/// </summary>
public interface IOpenAppAuthorizationService
{
    /// <summary>应用授权视图。</summary>
    Task<OpenAppGrantView> GetGrantsAsync(long appId, CancellationToken cancellationToken = default);

    /// <summary>整组替换应用授权;未知权限码抛 <see cref="IntegrationErrorCode.GrantPermissionUnknown"/>。</summary>
    Task SetGrantsAsync(long appId, OpenAppGrantInput input, CancellationToken cancellationToken = default);

    /// <summary>应用范围绑定视图。</summary>
    Task<IReadOnlyList<OpenAppScopeBindingView>> GetScopesAsync(long appId, CancellationToken cancellationToken = default);

    /// <summary>整组替换范围绑定;未知策略抛 <see cref="IntegrationErrorCode.ScopePolicyUnknown"/>,值非法抛 <see cref="IntegrationErrorCode.ScopeValueInvalid"/>。</summary>
    Task SetScopesAsync(long appId, OpenAppScopeInput input, CancellationToken cancellationToken = default);

    /// <summary>运行时授权快照(按应用版本缓存)。</summary>
    Task<OpenAppAuthorizationSnapshot> GetSnapshotAsync(OpenAppIdentity identity, CancellationToken cancellationToken = default);
}
