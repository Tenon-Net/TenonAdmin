using System.Text.RegularExpressions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IIntegrationAppService"/> 默认实现。影响运行时判定的变更(启停、归属机构、限流、删除)与
/// <see cref="IntegrationAppState.BumpAsync"/> 同事务提交;删除时同事务撤销全部有效凭据。
/// <para>启用、以及新建或修改时指定归属机构,会扩大或恢复应用能触达的数据,仅超管可做(<see cref="EnsureSuperAdmin"/>);
/// 不带归属机构的新建、停用、删除与改名称/说明/限流只看路由权限。写库只写本次改动的列,不整行回写——
/// 否则读取之后并发发生的停用、删除或归属机构变更会被旧值盖回去。</para>
/// </summary>
public partial class IntegrationAppService(
    IRepository<IntegrationApp> apps,
    IRepository<IntegrationAppCredential> credentials,
    TimeProvider time,
    ICurrentUser? currentUser = null) : IIntegrationAppService
{
    /// <summary>应用编码:2–64 位字母数字及 <c>._-</c>,以字母或数字开头。</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$")]
    private static partial Regex CodePattern();

    /// <inheritdoc />
    public virtual async Task<PagedList<IntegrationAppListItem>> PageAsync(IntegrationAppPageInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var keyword = input.Keyword?.Trim();
        var hasKeyword = !string.IsNullOrEmpty(keyword);
        var enabled = input.Enabled ?? false;

        var page = await apps.AsQueryable()
            .WhereIF(hasKeyword, a => a.Code.Contains(keyword!) || a.Name.Contains(keyword!))
            .WhereIF(input.Enabled.HasValue, a => a.Enabled == enabled)
            .OrderBy(a => a.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        var ids = page.Items.Select(a => a.Id).ToList();
        var creds = ids.Count == 0
            ? []
            : await credentials.AsQueryable().Where(c => ids.Contains(c.AppId)).ToListAsync(cancellationToken);
        var credsByApp = creds.ToLookup(c => c.AppId);
        var orgNames = await LoadOrgNamesAsync(page.Items.Select(a => a.OwnerOrgId), cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;

        return new PagedList<IntegrationAppListItem>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items
                .Select(a => MapListItem(a, credsByApp[a.Id].ToList(), orgNames, nowUtc))
                .ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<IntegrationAppDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var app = await RequireAppAsync(id, cancellationToken);
        var creds = await credentials.AsQueryable()
            .Where(c => c.AppId == id)
            .OrderBy(c => c.Id, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        var orgNames = await LoadOrgNamesAsync([app.OwnerOrgId], cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var item = MapListItem(app, creds, orgNames, nowUtc);

        return new IntegrationAppDetail
        {
            Id = item.Id,
            Code = item.Code,
            Name = item.Name,
            Description = item.Description,
            Enabled = item.Enabled,
            OwnerOrgId = item.OwnerOrgId,
            OwnerOrgName = item.OwnerOrgName,
            RateLimitPerMinute = item.RateLimitPerMinute,
            ActiveCredentialCount = item.ActiveCredentialCount,
            LastUsedAt = item.LastUsedAt,
            CreateTime = item.CreateTime,
            UpdateTime = app.UpdateTime,
            Credentials = creds.Select(c => OpenAppCredentialService.MapView(c, nowUtc)).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(IntegrationAppCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        // 新建即启用会恢复应用访问能力;归属机构则是写入数据的范围锚点,两者都只能由超管指定。
        if (input.Enabled || input.OwnerOrgId is not null) EnsureSuperAdmin();
        var code = (input.Code ?? "").Trim();
        IntegrationErrorCode.ThrowIf(!CodePattern().IsMatch(code), IntegrationErrorCode.AppInputInvalid, Field("code"));
        var name = NormalizeName(input.Name);
        var description = NormalizeDescription(input.Description);
        ValidateRateLimit(input.RateLimitPerMinute);
        await EnsureOrgExistsAsync(input.OwnerOrgId, cancellationToken);

        // 编码唯一(库有唯一索引):查重纳入软删行,避免撞约束抛原生 500(与角色编码同一做法)
        IntegrationErrorCode.ThrowIf(
            await apps.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(a => a.Code == code, cancellationToken),
            IntegrationErrorCode.AppCodeExists);

        var app = new IntegrationApp
        {
            Code = code,
            Name = name,
            Description = description,
            Enabled = input.Enabled,
            OwnerOrgId = input.OwnerOrgId,
            RateLimitPerMinute = input.RateLimitPerMinute,
            StateVersion = 1,
        };
        await apps.InsertAsync(app);
        return app.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, IntegrationAppUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var app = await RequireAppAsync(id, cancellationToken);
        // 归属机构是应用写入数据的范围锚点:相对刚读到的行真的改了才写这一列,也只有这时才要求超管。
        // 只改名称/说明/限流的请求不碰这一列,读取之后并发发生的归属机构变更不会被表单里的旧值盖回去
        var ownerChanged = input.OwnerOrgId != app.OwnerOrgId;
        if (ownerChanged) EnsureSuperAdmin();
        var name = NormalizeName(input.Name);
        var description = NormalizeDescription(input.Description);
        ValidateRateLimit(input.RateLimitPerMinute);
        if (ownerChanged) await EnsureOrgExistsAsync(input.OwnerOrgId, cancellationToken);

        app.Name = name;
        app.Description = description;
        app.OwnerOrgId = input.OwnerOrgId;
        app.RateLimitPerMinute = input.RateLimitPerMinute;
        await InTransactionAsync(async () =>
        {
            // 只写本次改动的列(审计列由内核 AOP 填):启停、删除标记与版本号保持库里的值,版本号只由 BumpAsync 原子递增
            var update = apps.Db.Updateable(app);
            update = ownerChanged
                ? update.UpdateColumns(a => new { a.Name, a.Description, a.OwnerOrgId, a.RateLimitPerMinute, a.UpdateTime, a.UpdateUserId })
                : update.UpdateColumns(a => new { a.Name, a.Description, a.RateLimitPerMinute, a.UpdateTime, a.UpdateUserId });
            await update.ExecuteCommandAsync(cancellationToken);
            await IntegrationAppState.BumpAsync(apps.Db, id, cancellationToken);
        });
    }

    /// <inheritdoc />
    public virtual async Task SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        if (enabled) EnsureSuperAdmin();   // 启用恢复应用的访问能力;停用是止损,只看路由权限
        var app = await RequireAppAsync(id, cancellationToken);
        if (app.Enabled == enabled) return;

        await InTransactionAsync(async () =>
        {
            // 只改启停列并跳过已删除的行:读取之后被删除的应用不能因启用而复活
            var affected = await apps.Db.Updateable<IntegrationApp>()
                .SetColumns(a => a.Enabled == enabled)
                .WithAudit(time, currentUser)
                .Where(a => a.Id == id && a.IsDelete == false)
                .ExecuteCommandAsync(cancellationToken);
            IntegrationErrorCode.ThrowIf(affected == 0, IntegrationErrorCode.AppNotFound);
            await IntegrationAppState.BumpAsync(apps.Db, id, cancellationToken);
        });
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(id, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var operatorId = currentUser?.UserId;

        await InTransactionAsync(async () =>
        {
            // 先撤销:删除后即便应用被回收站恢复,旧凭据也不会随之复活
            await credentials.Db.Updateable<IntegrationAppCredential>()
                .SetColumns(c => new IntegrationAppCredential { RevokedAtUtc = nowUtc, RevokedBy = operatorId })
                .Where(c => c.AppId == id && c.RevokedAtUtc == null)
                .ExecuteCommandAsync(cancellationToken);
            await apps.DeleteAsync(id);
            await IntegrationAppState.BumpAsync(apps.Db, id, cancellationToken);
        });
    }

    /// <summary>取应用;不存在(含已删除)抛 <see cref="IntegrationErrorCode.AppNotFound"/>。</summary>
    protected virtual async Task<IntegrationApp> RequireAppAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var app = await apps.GetByIdAsync(id);
        IntegrationErrorCode.ThrowIf(app is null, IntegrationErrorCode.AppNotFound);
        return app!;
    }

    /// <summary>
    /// 扩大或恢复应用访问能力的操作仅超管可执行(与内核角色授权面同一约束,QA09/QA36);系统与未认证上下文视为可信。
    /// </summary>
    protected virtual void EnsureSuperAdmin() =>
        AdminException.ThrowIf(currentUser is { IsAuthenticated: true, IsSuperAdmin: false }, ErrorCode.SuperAdminRequired);

    /// <summary>归属机构必须存在(空表示不设)。</summary>
    protected virtual async Task EnsureOrgExistsAsync(long? orgId, CancellationToken cancellationToken)
    {
        if (orgId is null) return;
        var id = orgId.Value;
        var exists = id > 0 && await apps.Db.Queryable<SysOrg>().AnyAsync(o => o.Id == id, cancellationToken);
        IntegrationErrorCode.ThrowIf(!exists, IntegrationErrorCode.OwnerOrgNotFound);
    }

    /// <summary>在主库事务内执行(嵌套时加入外层事务)。</summary>
    protected async Task InTransactionAsync(Func<Task> action)
    {
        var result = await apps.Db.Ado.UseTranAsync(action);
        if (!result.IsSuccess) throw result.ErrorException;
    }

    private async Task<Dictionary<long, string>> LoadOrgNamesAsync(IEnumerable<long?> orgIds, CancellationToken cancellationToken)
    {
        var ids = orgIds.Where(i => i is > 0).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        var orgs = await apps.Db.Queryable<SysOrg>().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        return orgs.ToDictionary(o => o.Id, o => o.Name);
    }

    private static IntegrationAppListItem MapListItem(
        IntegrationApp app,
        IReadOnlyList<IntegrationAppCredential> creds,
        IReadOnlyDictionary<long, string> orgNames,
        DateTime nowUtc) => new()
    {
        Id = app.Id,
        Code = app.Code,
        Name = app.Name,
        Description = app.Description,
        Enabled = app.Enabled,
        OwnerOrgId = app.OwnerOrgId,
        OwnerOrgName = app.OwnerOrgId is { } org && orgNames.TryGetValue(org, out var orgName) ? orgName : null,
        RateLimitPerMinute = app.RateLimitPerMinute,
        ActiveCredentialCount = creds.Count(c => OpenAppCredentialService.StatusOf(c, nowUtc) == OpenAppCredentialStatus.Active),
        LastUsedAt = IntegrationAppState.ToOffset(creds.Max(c => c.LastUsedAtUtc)),
        CreateTime = app.CreateTime,
    };

    private static string NormalizeName(string? name)
    {
        var value = (name ?? "").Trim();
        IntegrationErrorCode.ThrowIf(value.Length is 0 or > 64, IntegrationErrorCode.AppInputInvalid, Field("name"));
        return value;
    }

    private static string? NormalizeDescription(string? description)
    {
        var value = description?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        IntegrationErrorCode.ThrowIf(value.Length > 256, IntegrationErrorCode.AppInputInvalid, Field("description"));
        return value;
    }

    private static void ValidateRateLimit(int? rateLimit) =>
        IntegrationErrorCode.ThrowIf(rateLimit is < 0 or > 1_000_000, IntegrationErrorCode.AppInputInvalid, Field("rateLimitPerMinute"));

    private static Dictionary<string, object?> Field(string name) => new() { ["field"] = name };
}
