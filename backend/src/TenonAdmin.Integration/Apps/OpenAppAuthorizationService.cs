using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppAuthorizationService"/> 默认实现。授权与范围都是「整组替换」:同一事务内删旧、插新、递增应用版本;
/// 运行时快照按 (AppId, StateVersion) 放进 <see cref="ICacheProvider"/>,版本号来自每次请求的凭据校验(读库),
/// 所以任何副本在变更提交后的下一次请求就不会再用旧授权。
/// <para>修改授权与范围绑定决定应用能调哪些端点、读写哪些数据,仅超管可做(<see cref="EnsureSuperAdmin"/>)。</para>
/// </summary>
public class OpenAppAuthorizationService(
    IRepository<IntegrationApp> apps,
    IRepository<IntegrationAppGrant> grants,
    IRepository<IntegrationAppScope> scopes,
    IOpenApiCatalog catalog,
    OpenApiDataScopePolicyRegistry policies,
    ICacheProvider cache,
    ICurrentUser? currentUser = null) : IOpenAppAuthorizationService
{
    /// <summary>快照缓存时长:版本号保证一致性,TTL 只为回收不再使用的旧版本键。</summary>
    protected static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public virtual async Task<OpenAppGrantView> GetGrantsAsync(long appId, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(appId, cancellationToken);
        var permissions = await LoadPermissionsAsync(appId, cancellationToken);
        return new OpenAppGrantView
        {
            Permissions = permissions,
            Stale = permissions.Where(p => !catalog.Contains(p)).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task SetGrantsAsync(long appId, OpenAppGrantInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSuperAdmin();
        await RequireAppAsync(appId, cancellationToken);

        var permissions = (input.Permissions ?? [])
            .Select(p => p?.Trim() ?? "")
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var permission in permissions)
            IntegrationErrorCode.ThrowIf(!catalog.Contains(permission), IntegrationErrorCode.GrantPermissionUnknown,
                new Dictionary<string, object?> { ["permission"] = permission });

        await InTransactionAsync(async () =>
        {
            await grants.Db.Deleteable<IntegrationAppGrant>().Where(g => g.AppId == appId).ExecuteCommandAsync(cancellationToken);
            if (permissions.Count > 0)
                await grants.InsertRangeAsync(permissions.Select(p => new IntegrationAppGrant { AppId = appId, Permission = p }).ToList());
            await IntegrationAppState.BumpAsync(apps.Db, appId, cancellationToken);
        });
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<OpenAppScopeBindingView>> GetScopesAsync(long appId, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(appId, cancellationToken);
        var bindings = await LoadScopesAsync(appId, cancellationToken);
        return bindings.Select(b => new OpenAppScopeBindingView
        {
            ScopeKey = b.ScopeKey,
            ScopeName = policies.Find(b.ScopeKey)?.Name,
            AllValues = b.AllValues,
            Values = b.Values,
        }).ToList();
    }

    /// <inheritdoc />
    public virtual async Task SetScopesAsync(long appId, OpenAppScopeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSuperAdmin();
        await RequireAppAsync(appId, cancellationToken);

        var rows = new List<IntegrationAppScope>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in input.Bindings ?? [])
        {
            var policy = policies.Find(binding.ScopeKey);
            IntegrationErrorCode.ThrowIf(policy is null, IntegrationErrorCode.ScopePolicyUnknown,
                new Dictionary<string, object?> { ["scope"] = binding.ScopeKey });
            IntegrationErrorCode.ThrowIf(!seen.Add(policy!.Key), IntegrationErrorCode.ScopeValueInvalid,
                new Dictionary<string, object?> { ["scope"] = policy.Key });

            IReadOnlyList<string> values = binding.AllValues
                ? []
                : await policy.NormalizeBindingAsync(binding.Values ?? [], cancellationToken);
            rows.Add(new IntegrationAppScope
            {
                AppId = appId,
                ScopeKey = policy.Key,
                AllValues = binding.AllValues,
                ValuesJson = JsonSerializer.Serialize(values),
            });
        }

        await InTransactionAsync(async () =>
        {
            await scopes.Db.Deleteable<IntegrationAppScope>().Where(s => s.AppId == appId).ExecuteCommandAsync(cancellationToken);
            if (rows.Count > 0)
                await scopes.InsertRangeAsync(rows);
            await IntegrationAppState.BumpAsync(apps.Db, appId, cancellationToken);
        });
    }

    /// <inheritdoc />
    public virtual async Task<OpenAppAuthorizationSnapshot> GetSnapshotAsync(OpenAppIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var key = SnapshotKey(identity.AppId, identity.StateVersion);
        var cached = await cache.GetAsync<OpenAppAuthorizationSnapshot>(key, cancellationToken);
        if (cached is not null) return cached;

        var rateLimit = await apps.AsQueryable().Where(a => a.Id == identity.AppId).Select(a => a.RateLimitPerMinute).FirstAsync(cancellationToken);
        var snapshot = new OpenAppAuthorizationSnapshot(
            identity.AppId,
            identity.StateVersion,
            await LoadPermissionsAsync(identity.AppId, cancellationToken),
            await LoadScopesAsync(identity.AppId, cancellationToken),
            rateLimit);
        await cache.SetAsync(key, snapshot, SnapshotTtl, cancellationToken);
        return snapshot;
    }

    /// <summary>快照缓存键(逻辑键,提供者统一加前缀)。</summary>
    public static string SnapshotKey(long appId, long stateVersion) => $"itg:authz:{appId}:{stateVersion}";

    /// <summary>读应用授权码。</summary>
    protected virtual async Task<IReadOnlyList<string>> LoadPermissionsAsync(long appId, CancellationToken cancellationToken) =>
        await grants.AsQueryable()
            .Where(g => g.AppId == appId)
            .OrderBy(g => g.Permission)
            .Select(g => g.Permission)
            .ToListAsync(cancellationToken);

    /// <summary>读应用范围绑定。</summary>
    protected virtual async Task<IReadOnlyList<OpenAppScopeBinding>> LoadScopesAsync(long appId, CancellationToken cancellationToken)
    {
        var rows = await scopes.AsQueryable().Where(s => s.AppId == appId).OrderBy(s => s.ScopeKey).ToListAsync(cancellationToken);
        return rows.Select(r => new OpenAppScopeBinding(
                r.ScopeKey,
                r.AllValues,
                r.AllValues ? [] : JsonSerializer.Deserialize<List<string>>(r.ValuesJson) ?? []))
            .ToList();
    }

    /// <summary>
    /// 授权与范围绑定只由超管修改(与内核角色授权面同一约束,QA09/QA36):全量范围会解析成内核不受限范围;系统与未认证上下文视为可信。
    /// </summary>
    protected virtual void EnsureSuperAdmin() =>
        AdminException.ThrowIf(currentUser is { IsAuthenticated: true, IsSuperAdmin: false }, ErrorCode.SuperAdminRequired);

    /// <summary>取应用;不存在(含已删除)抛 <see cref="IntegrationErrorCode.AppNotFound"/>。</summary>
    protected virtual async Task RequireAppAsync(long appId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntegrationErrorCode.ThrowIf(await apps.GetByIdAsync(appId) is null, IntegrationErrorCode.AppNotFound);
    }

    /// <summary>在主库事务内执行(嵌套时加入外层事务)。</summary>
    protected async Task InTransactionAsync(Func<Task> action)
    {
        var result = await apps.Db.Ado.UseTranAsync(action);
        if (!result.IsSuccess) throw result.ErrorException;
    }
}
