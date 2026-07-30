using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IIdleAccountService"/> 默认实现:仅 Level3 生效。
/// <list type="bullet">
/// <item>普通启用账户:90 天未成功登录 → 自动停用</item>
/// <item>MFA 账户(TotpEnabled|ForceTotp):60 天告警、90 天停用</item>
/// <item>超级管理员:60/90 天仅告警,永不自动停用</item>
/// </list>
/// </summary>
public class IdleAccountService(
    IRepository<SysUser> users,
    ISessionService sessions,
    ILogService log,
    AdminSecurityOptions security,
    TimeProvider time,
    ILogger<IdleAccountService>? logger = null) : IIdleAccountService
{
    // 与 AuthService 一致:业务时间戳走本地时钟(SqlSugar 审计同口径)
    private DateTime Now => time.GetLocalNow().DateTime;

    /// <inheritdoc />
    public virtual async Task<IdleAccountScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (security.Profile != SecurityProfile.Level3)
            return new IdleAccountScanResult(0, 0, 0);

        var now = Now;
        var warnBefore = now.AddDays(-SecurityPolicyProvider.Level3IdleAccountWarnDays);
        var disableBefore = now.AddDays(-SecurityPolicyProvider.Level3IdleAccountDisableDays);

        // 全部启用账户(含普通用户);已停用无需再处理
        var candidates = await users.AsQueryable()
            .Where(u => u.Enabled)
            .ToListAsync();

        var warned = 0;
        var disabled = 0;
        var superWarned = 0;

        foreach (var u in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 无成功登录记录:视为从未登录——用建号时间兜底,避免 null 跳过治理
            var last = u.LastSuccessfulLoginAt ?? u.CreateTime;
            var isMfa = u.TotpEnabled || u.ForceTotp;

            if (u.IsSuperAdmin)
            {
                // 超管仅告警(60d 与 90d 均不自动停用)
                if (last > warnBefore) continue;
                await RecordWarnAsync(u, last, disable: false);
                superWarned++;
                warned++;
                continue;
            }

            if (isMfa)
            {
                // MFA:60d 告警窗口;90d 停用
                if (last > warnBefore) continue;
                if (last <= disableBefore)
                {
                    await DisableUserAsync(u, last);
                    disabled++;
                }
                else
                {
                    await RecordWarnAsync(u, last, disable: false);
                    warned++;
                }
                continue;
            }

            // 普通用户:仅 90 天自动停用
            if (last <= disableBefore)
            {
                await DisableUserAsync(u, last);
                disabled++;
            }
        }

        logger?.LogInformation(
            "IdleAccount scan: warned={Warned}, disabled={Disabled}, superAdminWarnedOnly={Super}",
            warned, disabled, superWarned);

        return new IdleAccountScanResult(warned, disabled, superWarned);
    }

    /// <summary>停用用户、吊销会话并记审计。</summary>
    protected virtual async Task DisableUserAsync(SysUser user, DateTime lastLogin)
    {
        user.Enabled = false;
        await users.UpdateAsync(user);
        await sessions.RevokeAllForUserAsync(user.Id);
        await RecordWarnAsync(user, lastLogin, disable: true);
    }

    /// <summary>写一条操作审计(尽力而为;ILogService 吞异常)。</summary>
    protected virtual Task RecordWarnAsync(SysUser user, DateTime lastLogin, bool disable) =>
        log.RecordOperationAsync(new OperationLogEntry
        {
            Title = disable ? "IdleAccountDisable" : "IdleAccountWarn",
            HttpMethod = "JOB",
            Path = "/internal/idle-account",
            ParamJson = $"{{\"userId\":{user.Id},\"account\":\"{user.Account}\",\"lastSuccessfulLoginAt\":\"{lastLogin:O}\",\"disabled\":{disable.ToString().ToLowerInvariant()}}}",
            ResultCode = 0,
            ElapsedMs = 0,
        });
}
