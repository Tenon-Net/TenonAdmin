using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppCredentialValidator"/> 默认实现(实现契约 §4.1/§4.3)。
/// <para>顺序:格式 → 按 <c>KeyId</c> 唯一索引取凭据 → 定长比较摘要 → 撤销 → 过期 → 应用存在且启用。
/// <b>不做进程内缓存</b>:每次读库,停用/撤销/过期在所有副本上下一次请求即生效,与缓存提供者无关。</para>
/// <para>方法 virtual,消费者可继承覆写单步后前置注册。</para>
/// </summary>
public class OpenAppCredentialValidator(
    ISqlSugarClient db,
    IOpenAppKeyGenerator keys,
    IntegrationOptions options,
    TimeProvider time,
    ILogger<OpenAppCredentialValidator>? logger = null) : IOpenAppCredentialValidator
{
    /// <inheritdoc />
    public virtual async Task<OpenAppCredentialValidation> ValidateAsync(string? presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presentedKey))
            return OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Missing);
        if (!OpenAppApiKey.TryParse(presentedKey, out var keyId, out var secret))
            return OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Malformed);

        var credential = await FindCredentialAsync(keyId, cancellationToken);
        if (credential is null)
            return OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.NotFound, keyId: keyId);

        if (!SecretMatches(secret, credential.SecretHash))
            return Fail(OpenAppCredentialFailure.SecretMismatch, credential);

        var nowUtc = time.GetUtcNow().UtcDateTime;
        if (credential.RevokedAtUtc is not null)
            return Fail(OpenAppCredentialFailure.Revoked, credential);
        if (IsExpired(credential, nowUtc))
            return Fail(OpenAppCredentialFailure.Expired, credential);

        var app = await FindAppAsync(credential.AppId, cancellationToken);
        if (app is null)
            return Fail(OpenAppCredentialFailure.AppNotFound, credential);
        if (!app.Enabled)
            return Fail(OpenAppCredentialFailure.AppDisabled, credential);

        await TouchLastUsedAsync(credential, nowUtc, cancellationToken);
        return OpenAppCredentialValidation.Success(
            new OpenAppIdentity(app.Id, app.Code, credential.Id, credential.KeyId, app.OwnerOrgId, app.StateVersion));
    }

    /// <summary>按公开标识取凭据(唯一索引)。</summary>
    protected virtual Task<IntegrationAppCredential?> FindCredentialAsync(string keyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return db.Queryable<IntegrationAppCredential>().Where(c => c.KeyId == keyId).FirstAsync(cancellationToken)!;
    }

    /// <summary>取应用(全局软删过滤器生效:已删除应用视为不存在)。</summary>
    protected virtual Task<IntegrationApp?> FindAppAsync(long appId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return db.Queryable<IntegrationApp>().Where(a => a.Id == appId).FirstAsync(cancellationToken)!;
    }

    /// <summary>到期时刻本身即视为过期(边界闭合,轮换窗口结束那一刻旧凭据失效)。</summary>
    protected virtual bool IsExpired(IntegrationAppCredential credential, DateTime nowUtc) =>
        credential.ExpiresAtUtc is { } expires && expires <= nowUtc;

    /// <summary>定长比较摘要(两侧都是 64 位 hex),不因前缀相同提前返回。</summary>
    protected virtual bool SecretMatches(string secret, string storedHash)
    {
        var computed = keys.ComputeSecretHash(secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(storedHash ?? ""));
    }

    /// <summary>
    /// 节流回写最近使用时间:条件 UPDATE 保证间隔内同一凭据只写一次(多副本并发也最多一行生效)。
    /// 尽力而为——写失败只记日志,不影响本次校验结果。
    /// </summary>
    protected virtual async Task TouchLastUsedAsync(IntegrationAppCredential credential, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0, options.Credentials.LastUsedWriteIntervalSeconds));
        if (credential.LastUsedAtUtc is { } last && nowUtc - last < interval) return;

        var id = credential.Id;
        var stamp = nowUtc;                 // SetColumns 里的 DateTime 必须先落局部变量(zh-CN 下内联会被格式化进 SQL)
        var threshold = nowUtc - interval;
        try
        {
            await db.Updateable<IntegrationAppCredential>()
                .SetColumns(c => c.LastUsedAtUtc == stamp)
                .Where(c => c.Id == id && (c.LastUsedAtUtc == null || c.LastUsedAtUtc <= threshold))
                .ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "接入凭据最近使用时间回写失败。CredentialId={CredentialId}", id);
        }
    }

    private static OpenAppCredentialValidation Fail(OpenAppCredentialFailure failure, IntegrationAppCredential credential) =>
        OpenAppCredentialValidation.Fail(failure, credential.AppId, credential.Id, credential.KeyId);
}
