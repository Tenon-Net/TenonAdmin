using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary><see cref="ISysUserExternalService"/> 默认实现:绑定表 CRUD + 运营配置读取(读穿透缓存,走 <see cref="IConfigService"/>)。</summary>
public class SysUserExternalService(IRepository<SysUserExternal> repo, IConfigService config) : ISysUserExternalService
{
    // 运营配置键(批次 D 折中:连接走 appsettings,这些开关走 sys_config;{code} = provider 码)
    private static string EnabledKey(string code) => $"sys.externalauth.{code}.enabled";
    private static string ProvisioningKey(string code) => $"sys.externalauth.{code}.provisioning";
    private static string DefaultRoleIdsKey(string code) => $"sys.externalauth.{code}.defaultRoleIds";
    private static string DefaultOrgIdKey(string code) => $"sys.externalauth.{code}.defaultOrgId";

    /// <inheritdoc />
    public virtual Task<SysUserExternal?> FindByExternalAsync(string provider, string subject) =>
        repo.GetFirstAsync(x => x.Provider == provider && x.Subject == subject);

    /// <inheritdoc />
    public virtual Task<List<SysUserExternal>> ListByUserAsync(long userId) =>
        repo.AsQueryable().Where(x => x.UserId == userId).OrderBy(x => x.CreateTime).ToListAsync();

    /// <inheritdoc />
    public virtual async Task BindAsync(long userId, ExternalIdentity identity)
    {
        var existing = await FindByExternalAsync(identity.Provider, identity.Subject);
        if (existing is not null)
        {
            if (existing.UserId == userId) return;                       // 幂等:已绑到本人
            throw new AdminException(ErrorCode.OAuthAlreadyBound);       // 该外部身份归他人
        }
        // 一个用户在一个 provider 下只保留一个绑定(要换绑先解绑)
        if (await repo.AnyAsync(x => x.UserId == userId && x.Provider == identity.Provider))
            throw new AdminException(ErrorCode.OAuthAlreadyBound);

        await repo.InsertAsync(new SysUserExternal
        {
            UserId = userId,
            Provider = identity.Provider,
            Subject = identity.Subject,
            DisplayName = identity.DisplayName,
        });
    }

    /// <inheritdoc />
    public virtual async Task UnbindAsync(long userId, string provider)
    {
        var row = await repo.GetFirstAsync(x => x.UserId == userId && x.Provider == provider);
        if (row is not null) await repo.DeleteAsync(row.Id);            // 软删:唯一键由回收机制 _del_{id} 释放,后续可重绑
    }

    /// <inheritdoc />
    public virtual async Task UnbindAllAsync(long userId)
    {
        var rows = await repo.AsQueryable().Where(x => x.UserId == userId).ToListAsync();
        // 逐行走 repo.DeleteAsync:软删时唯一列(Provider/Subject)追加 _del_{id} 后缀释放唯一位,该外部身份日后可被重绑。
        // 批量 Deleteable().Where() 不触发回收 AOP,会把唯一位一直占住 → 故意逐行(一个用户的绑定数极少)。
        foreach (var row in rows)
            await repo.DeleteAsync(row.Id);
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsEnabledAsync(string provider)
    {
        var v = await config.GetValueByKeyAsync(EnabledKey(provider));
        return v is null || IsTrue(v);                                  // 缺省启用(配了连接就默认可用)
    }

    /// <inheritdoc />
    public virtual async Task<ExternalUnboundPolicy> GetUnboundPolicyAsync(string provider)
    {
        var v = await config.GetValueByKeyAsync(ProvisioningKey(provider));
        return string.Equals(v?.Trim(), "provision", StringComparison.OrdinalIgnoreCase)
            ? ExternalUnboundPolicy.Provision
            : ExternalUnboundPolicy.Reject;                             // 缺省拒绝(Q1 安全默认)
    }

    /// <inheritdoc />
    public virtual async Task<(IReadOnlyCollection<long> RoleIds, long? OrgId)> GetProvisionDefaultsAsync(string provider)
    {
        var roleCsv = await config.GetValueByKeyAsync(DefaultRoleIdsKey(provider));
        var roleIds = (roleCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        var orgRaw = await config.GetValueByKeyAsync(DefaultOrgIdKey(provider));
        long? orgId = long.TryParse(orgRaw, out var o) && o > 0 ? o : null;

        return (roleIds, orgId);
    }

    private static bool IsTrue(string v)
    {
        var t = v.Trim();
        return t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1";
    }
}
