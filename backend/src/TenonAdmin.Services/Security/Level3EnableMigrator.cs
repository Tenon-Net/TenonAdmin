using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ILevel3EnableMigrator"/> 默认实现:首次 Level3 启用时为存量启用用户写入
/// <see cref="SysUser.LastSuccessfulLoginAt"/> = 启用时刻,并经 SysConfig 旗标幂等。
/// </summary>
public class Level3EnableMigrator(
    IRepository<SysUser> users,
    IRepository<SysConfig> configs,
    ILogService log,
    AdminSecurityOptions security,
    TimeProvider time,
    ILogger<Level3EnableMigrator>? logger = null) : ILevel3EnableMigrator
{
    public const string ConfigKey = CacheKeys.Level3EnableMigrationDone;
    public const string ConfigGroup = SecurityPolicyProvider.GROUP;

    private DateTime Now => time.GetLocalNow().DateTime;

    /// <inheritdoc />
    public virtual async Task<int> EnsureMigratedAsync(CancellationToken cancellationToken = default)
    {
        if (!security.IsLegacyLevel3Profile)
            return 0;

        var flag = await configs.GetFirstAsync(c => c.ConfigKey == ConfigKey);
        if (flag is not null && string.Equals(flag.ConfigValue, "true", StringComparison.OrdinalIgnoreCase))
            return 0;

        var now = Now;
        // 仅补齐启用且尚未有成功登录锚点的用户
        var missing = await users.AsQueryable()
            .Where(u => u.Enabled && u.LastSuccessfulLoginAt == null)
            .ToListAsync();

        foreach (var u in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            u.LastSuccessfulLoginAt = now;
            await users.UpdateAsync(u);
        }

        if (flag is null)
        {
            await configs.InsertAsync(new SysConfig
            {
                ConfigKey = ConfigKey,
                ConfigValue = "true",
                Name = "Level3 首次启用迁移完成",
                GroupCode = ConfigGroup,
                Sort = 99,
                Remark = $"于 {now:O} 初始化 LastSuccessfulLoginAt,覆盖 {missing.Count} 名启用用户",
            });
        }
        else
        {
            flag.ConfigValue = "true";
            flag.Remark = $"于 {now:O} 初始化 LastSuccessfulLoginAt,覆盖 {missing.Count} 名启用用户";
            await configs.UpdateAsync(flag);
        }

        await log.RecordOperationAsync(new OperationLogEntry
        {
            Title = "Level3EnableMigration",
            HttpMethod = "JOB",
            Path = "/internal/level3-enable-migration",
            ParamJson = $"{{\"migratedUsers\":{missing.Count},\"enableTime\":\"{now:O}\"}}",
            ResultCode = 0,
            ElapsedMs = 0,
        });

        logger?.LogInformation("Level3 enable migration: set LastSuccessfulLoginAt for {Count} users at {At:O}",
            missing.Count, now);

        return missing.Count;
    }
}
