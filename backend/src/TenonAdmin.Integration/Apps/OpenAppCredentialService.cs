using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppCredentialService"/> 默认实现(实现契约 §4.1)。
/// <para>秘密由 <see cref="IOpenAppKeyGenerator"/> 生成、只存摘要;<see cref="OpenAppCredentialIssued.ApiKey"/> 是秘密出现的唯一位置。
/// 凭据状态变更不需要递增应用版本:校验每次读库取凭据行,撤销/到期自然在所有副本下一次请求生效。</para>
/// <para>发放、轮换、调整到期会扩大或延续应用的访问能力,仅超管可做(<see cref="EnsureSuperAdmin"/>);撤销是止损,只看路由权限。
/// 改凭据只写本次改动的列,且到期调整以「未撤销」为条件——整行回写会把读取之后并发的撤销盖回去。</para>
/// </summary>
public class OpenAppCredentialService(
    IRepository<IntegrationApp> apps,
    IRepository<IntegrationAppCredential> credentials,
    IOpenAppKeyGenerator keys,
    IntegrationOptions options,
    TimeProvider time,
    ICurrentUser? currentUser = null) : IOpenAppCredentialService
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<OpenAppCredentialView>> ListAsync(long appId, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(appId, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var rows = await credentials.AsQueryable()
            .Where(c => c.AppId == appId)
            .OrderBy(c => c.Id, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(c => MapView(c, nowUtc)).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<OpenAppCredentialIssued> CreateAsync(long appId, OpenAppCredentialCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSuperAdmin();
        await RequireAppAsync(appId, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var name = NormalizeName(input.Name);
        var expiresAt = ResolveExpiry(input.ExpiresAt, input.NeverExpires, nowUtc, allowDefault: true);

        var max = options.Credentials.MaxActivePerApp;
        IntegrationErrorCode.ThrowIf(await CountActiveAsync(appId, nowUtc, cancellationToken) >= max,
            IntegrationErrorCode.CredentialLimitExceeded, new Dictionary<string, object?> { ["max"] = max });

        var key = await GenerateUniqueAsync(cancellationToken);
        var row = new IntegrationAppCredential
        {
            AppId = appId,
            KeyId = key.KeyId,
            SecretHash = key.SecretHash,
            Name = name,
            ExpiresAtUtc = expiresAt,
        };
        await credentials.InsertAsync(row);
        return new OpenAppCredentialIssued { Credential = MapView(row, nowUtc), ApiKey = key.ApiKey };
    }

    /// <inheritdoc />
    public virtual async Task<OpenAppCredentialIssued> RotateAsync(long appId, long credentialId, OpenAppCredentialRotateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSuperAdmin();
        await RequireAppAsync(appId, cancellationToken);
        var source = await RequireCredentialAsync(appId, credentialId, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        IntegrationErrorCode.ThrowIf(StatusOf(source, nowUtc) != OpenAppCredentialStatus.Active, IntegrationErrorCode.CredentialNotActive);

        var maxOverlap = options.Credentials.MaxRotationOverlapHours;
        var overlap = input.OverlapHours ?? options.Credentials.DefaultRotationOverlapHours;
        IntegrationErrorCode.ThrowIf(overlap < 0 || overlap > maxOverlap,
            IntegrationErrorCode.RotationOverlapInvalid, new Dictionary<string, object?> { ["max"] = maxOverlap });

        // 轮换允许在上限处临时多出一把(旧凭据会在窗口结束时过期);超过上限仍拒绝
        var max = options.Credentials.MaxActivePerApp;
        IntegrationErrorCode.ThrowIf(await CountActiveAsync(appId, nowUtc, cancellationToken) > max,
            IntegrationErrorCode.CredentialLimitExceeded, new Dictionary<string, object?> { ["max"] = max });

        var name = input.Name is null ? source.Name : NormalizeName(input.Name);
        var expiresAt = ResolveExpiry(input.ExpiresAt, input.NeverExpires, nowUtc, allowDefault: true);
        var cutoff = IntegrationAppState.FloorSeconds(nowUtc.AddHours(overlap));
        var sourceExpiry = source.ExpiresAtUtc is { } current && current <= cutoff ? current : cutoff;

        var key = await GenerateUniqueAsync(cancellationToken);
        var row = new IntegrationAppCredential
        {
            AppId = appId,
            KeyId = key.KeyId,
            SecretHash = key.SecretHash,
            Name = name,
            ExpiresAtUtc = expiresAt,
            RotatedFromId = source.Id,
        };

        var result = await credentials.Db.Ado.UseTranAsync(async () =>
        {
            await credentials.InsertAsync(row);
            // 旧凭据在并存窗口结束时过期(已更早到期则不变);读取之后被撤销的不能再轮换,整笔回滚
            await UpdateExpiryAsync(source.Id, sourceExpiry, cancellationToken);
        });
        if (!result.IsSuccess) throw result.ErrorException;
        return new OpenAppCredentialIssued { Credential = MapView(row, nowUtc), ApiKey = key.ApiKey };
    }

    /// <inheritdoc />
    public virtual async Task RevokeAsync(long appId, long credentialId, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(appId, cancellationToken);
        var row = await RequireCredentialAsync(appId, credentialId, cancellationToken);
        if (row.RevokedAtUtc is not null) return;   // 幂等:重复撤销不报错、不改撤销人

        DateTime? revokedAt = time.GetUtcNow().UtcDateTime;
        var revokedBy = currentUser?.UserId;
        var id = row.Id;
        await credentials.Db.Updateable<IntegrationAppCredential>()
            .SetColumns(c => c.RevokedAtUtc == revokedAt)
            .SetColumns(c => c.RevokedBy == revokedBy)
            .WithAudit(time, currentUser)
            .Where(c => c.Id == id && c.RevokedAtUtc == null)   // 并发撤销时先到者生效,撤销人不被覆盖
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long appId, long credentialId, CancellationToken cancellationToken = default)
    {
        await RequireAppAsync(appId, cancellationToken);
        var row = await RequireCredentialAsync(appId, credentialId, cancellationToken);
        IntegrationErrorCode.ThrowIf(row.RevokedAtUtc is null, IntegrationErrorCode.CredentialNotRevoked);

        var affected = await credentials.Db.Updateable<IntegrationAppCredential>()
            .SetColumns(nameof(ISoftDelete.IsDelete), true)
            .WithAudit(time, currentUser)
            .Where(c => c.Id == credentialId && c.AppId == appId && c.RevokedAtUtc != null && c.IsDelete == false)
            .ExecuteCommandAsync(cancellationToken);
        IntegrationErrorCode.ThrowIf(affected == 0, IntegrationErrorCode.CredentialNotFound);
    }

    /// <inheritdoc />
    public virtual async Task SetExpiryAsync(long appId, long credentialId, OpenAppCredentialExpiryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSuperAdmin();
        await RequireAppAsync(appId, cancellationToken);
        var row = await RequireCredentialAsync(appId, credentialId, cancellationToken);
        IntegrationErrorCode.ThrowIf(row.RevokedAtUtc is not null, IntegrationErrorCode.CredentialNotActive);

        var expiresAt = ResolveExpiry(input.ExpiresAt, input.NeverExpires, time.GetUtcNow().UtcDateTime, allowDefault: false);
        await UpdateExpiryAsync(row.Id, expiresAt, cancellationToken);
    }

    /// <summary>凭据当前状态:撤销优先于过期;到期时刻本身即视为过期(与校验同一边界)。</summary>
    public static OpenAppCredentialStatus StatusOf(IntegrationAppCredential credential, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.RevokedAtUtc is not null) return OpenAppCredentialStatus.Revoked;
        if (credential.ExpiresAtUtc is { } expires && expires <= nowUtc) return OpenAppCredentialStatus.Expired;
        return OpenAppCredentialStatus.Active;
    }

    /// <summary>实体 → 公开视图(不含摘要)。</summary>
    public static OpenAppCredentialView MapView(IntegrationAppCredential credential, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new OpenAppCredentialView
        {
            Id = credential.Id,
            AppId = credential.AppId,
            KeyId = credential.KeyId,
            Name = credential.Name,
            Status = StatusOf(credential, nowUtc),
            ExpiresAt = IntegrationAppState.ToOffset(credential.ExpiresAtUtc),
            RevokedAt = IntegrationAppState.ToOffset(credential.RevokedAtUtc),
            RevokedBy = credential.RevokedBy,
            RotatedFromId = credential.RotatedFromId,
            LastUsedAt = IntegrationAppState.ToOffset(credential.LastUsedAtUtc),
            CreateTime = credential.CreateTime,
        };
    }

    /// <summary>
    /// 解析到期时刻:不过期 → null;显式时刻须晚于当前且不超过最长有效期;都未给出时按默认有效期
    /// (<paramref name="allowDefault"/>=false 时视为非法)。结果取整到秒,各方言读回一致。
    /// </summary>
    protected virtual DateTime? ResolveExpiry(DateTimeOffset? expiresAt, bool neverExpires, DateTime nowUtc, bool allowDefault)
    {
        if (neverExpires) return null;

        var maxDays = options.Credentials.MaxLifetimeDays;
        if (expiresAt is { } requested)
        {
            var utc = IntegrationAppState.FloorSeconds(requested.UtcDateTime);
            IntegrationErrorCode.ThrowIf(utc <= nowUtc || utc > nowUtc.AddDays(maxDays),
                IntegrationErrorCode.CredentialExpiryInvalid, new Dictionary<string, object?> { ["maxDays"] = maxDays });
            return utc;
        }

        IntegrationErrorCode.ThrowIf(!allowDefault, IntegrationErrorCode.CredentialExpiryInvalid,
            new Dictionary<string, object?> { ["maxDays"] = maxDays });
        var days = options.Credentials.DefaultLifetimeDays;
        return days > 0 ? IntegrationAppState.FloorSeconds(nowUtc.AddDays(days)) : null;
    }

    /// <summary>当前有效(未撤销、未到期)的凭据数。</summary>
    protected virtual Task<int> CountActiveAsync(long appId, DateTime nowUtc, CancellationToken cancellationToken) =>
        credentials.AsQueryable()
            .Where(c => c.AppId == appId && c.RevokedAtUtc == null && (c.ExpiresAtUtc == null || c.ExpiresAtUtc > nowUtc))
            .CountAsync(cancellationToken);

    /// <summary>生成公开标识不重复的新凭据(8 字节随机标识碰撞概率极低,仍以唯一索引为准重试)。</summary>
    protected virtual async Task<OpenAppGeneratedKey> GenerateUniqueAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 5; i++)
        {
            var key = keys.Generate();
            var keyId = key.KeyId;
            if (!await credentials.AsQueryable().AnyAsync(c => c.KeyId == keyId, cancellationToken))
                return key;
        }
        throw new InvalidOperationException("连续生成的接入凭据标识均已存在,请检查 IOpenAppKeyGenerator 实现的随机性。");
    }

    /// <summary>
    /// 扩大或延续应用访问能力的操作仅超管可执行(与内核角色授权面同一约束,QA09/QA36);系统与未认证上下文视为可信。
    /// </summary>
    protected virtual void EnsureSuperAdmin() =>
        AdminException.ThrowIf(currentUser is { IsAuthenticated: true, IsSuperAdmin: false }, ErrorCode.SuperAdminRequired);

    /// <summary>取应用;不存在(含已删除)抛 <see cref="IntegrationErrorCode.AppNotFound"/>。</summary>
    protected virtual async Task RequireAppAsync(long appId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntegrationErrorCode.ThrowIf(await apps.GetByIdAsync(appId) is null, IntegrationErrorCode.AppNotFound);
    }

    /// <summary>取属于该应用的凭据;否则抛 <see cref="IntegrationErrorCode.CredentialNotFound"/>。</summary>
    protected virtual async Task<IntegrationAppCredential> RequireCredentialAsync(long appId, long credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await credentials.GetByIdAsync(credentialId);
        IntegrationErrorCode.ThrowIf(row is null || row.AppId != appId, IntegrationErrorCode.CredentialNotFound);
        return row!;
    }

    /// <summary>只写到期列(连同审计列),且只对未撤销的凭据生效:读取之后被撤销的影响 0 行,按不可用拒绝(49016)。</summary>
    private async Task UpdateExpiryAsync(long credentialId, DateTime? expiresAtUtc, CancellationToken cancellationToken)
    {
        var affected = await credentials.Db.Updateable<IntegrationAppCredential>()
            .SetColumns(c => c.ExpiresAtUtc == expiresAtUtc)
            .WithAudit(time, currentUser)
            .Where(c => c.Id == credentialId && c.RevokedAtUtc == null)
            .ExecuteCommandAsync(cancellationToken);
        IntegrationErrorCode.ThrowIf(affected == 0, IntegrationErrorCode.CredentialNotActive);
    }

    private static string? NormalizeName(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        IntegrationErrorCode.ThrowIf(value.Length > 64, IntegrationErrorCode.AppInputInvalid,
            new Dictionary<string, object?> { ["field"] = "name" });
        return value;
    }
}
