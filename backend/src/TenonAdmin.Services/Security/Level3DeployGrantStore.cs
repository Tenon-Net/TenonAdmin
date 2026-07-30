using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ILevel3DeployGrantStore"/> 默认实现:DB 行 + 条件更新防并发双消费。
/// 绝对到期与 first-seen TTL 的<strong>最终</strong>判定均使用数据库执行时刻的 UTC 表达式,
/// 不得使用应用侧预先捕获的 DateTime 作为最终谓词。
/// 日志只记 kind/hash 前缀,永不记授权明文。
/// </summary>
public class Level3DeployGrantStore(
    IRepository<SysLevel3DeployGrant> grants,
    TimeProvider? time = null,
    ILogger<Level3DeployGrantStore>? logger = null) : ILevel3DeployGrantStore
{
    private DateTime NowUtc => (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public virtual async Task<Level3DeployGrantUsability> CheckUsableAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(grantHash))
            return Level3DeployGrantUsability.Fail("empty kind/hash");

        cancellationToken.ThrowIfCancellationRequested();
        var now = NowUtc;
        var ttl = Math.Max(1, ttlMinutes);

        if (absoluteNotAfter is null)
            return Level3DeployGrantUsability.Fail("absolute NotAfter missing");

        if (absoluteNotAfter.Value.UtcDateTime <= now)
            return Level3DeployGrantUsability.Fail("absolute NotAfter expired");

        var row = await grants.GetFirstAsync(g => g.Kind == kind && g.GrantHash == grantHash);
        if (row is null)
            return Level3DeployGrantUsability.Ok("not yet first-seen; absolute window open");

        if (row.ConsumedAt is not null)
            return Level3DeployGrantUsability.Fail("already consumed");

        var hardStop = row.AbsoluteNotAfterUtc < absoluteNotAfter.Value.UtcDateTime
            ? row.AbsoluteNotAfterUtc
            : absoluteNotAfter.Value.UtcDateTime;
        if (hardStop <= now)
            return Level3DeployGrantUsability.Fail("absolute NotAfter expired (row or config)");

        if (row.FirstSeenAt.AddMinutes(ttl) <= now)
            return Level3DeployGrantUsability.Fail("first-seen TTL expired");

        return Level3DeployGrantUsability.Ok("first-seen within TTL");
    }

    /// <inheritdoc />
    public virtual async Task EnsureWithinTtlAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default)
    {
        var row = await EnsureRowExistsAsync(kind, grantHash, absoluteNotAfter, cancellationToken);
        var now = NowUtc;
        var ttl = Math.Max(1, ttlMinutes);
        // StartBind 等路径的早失败(应用时钟);最终消费以 DB 时刻为准
        if (row.FirstSeenAt.AddMinutes(ttl) <= now)
            throw new AdminException(ErrorCode.BindInviteInvalid);
    }

    /// <summary>
    /// 保证 first-seen 行存在且未消费、配置 NotAfter 未过(应用时钟早失败)。
    /// <b>不</b>用应用时钟判定 first-seen TTL——该判定仅在 Consume 的 DB 谓词中做最终决定。
    /// </summary>
    protected virtual async Task<SysLevel3DeployGrant> EnsureRowExistsAsync(
        string kind,
        string grantHash,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantHash);
        cancellationToken.ThrowIfCancellationRequested();

        var now = NowUtc;
        if (absoluteNotAfter is null || absoluteNotAfter.Value.UtcDateTime <= now)
            throw new AdminException(ErrorCode.BindInviteInvalid);

        var notAfterUtc = absoluteNotAfter.Value.UtcDateTime;
        var row = await grants.GetFirstAsync(g => g.Kind == kind && g.GrantHash == grantHash);
        if (row is null)
        {
            try
            {
                await grants.InsertAsync(new SysLevel3DeployGrant
                {
                    Kind = kind,
                    GrantHash = grantHash,
                    FirstSeenAt = now,
                    AbsoluteNotAfterUtc = notAfterUtc,
                    ConsumedAt = null,
                });
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Level3 deploy grant first-seen race kind={Kind}", kind);
                row = await grants.GetFirstAsync(g => g.Kind == kind && g.GrantHash == grantHash);
                AdminException.ThrowIf(row is null, ErrorCode.BindInviteInvalid);
            }

            row ??= await grants.GetFirstAsync(g => g.Kind == kind && g.GrantHash == grantHash);
            AdminException.ThrowIf(row is null, ErrorCode.BindInviteInvalid);
        }

        if (row!.ConsumedAt is not null)
            throw new AdminException(ErrorCode.BindInviteInvalid);

        if (notAfterUtc <= now)
            throw new AdminException(ErrorCode.BindInviteInvalid);

        return row;
    }

    /// <inheritdoc />
    public virtual async Task ConsumeAsync(
        string kind,
        string grantHash,
        int ttlMinutes,
        DateTimeOffset? absoluteNotAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantHash);
        cancellationToken.ThrowIfCancellationRequested();

        // 仅确保行存在;first-seen TTL / 绝对到期的最终判定均在下方 SQL 以 DB 时刻求值
        await EnsureRowExistsAsync(kind, grantHash, absoluteNotAfter, cancellationToken);

        if (absoluteNotAfter is null)
            throw new AdminException(ErrorCode.BindInviteInvalid);

        var ttl = Math.Max(1, ttlMinutes);
        var configNotAfterUtc = absoluteNotAfter.Value.UtcDateTime;
        var utcNowSql = UtcNowSqlExpression(grants.Db);
        var firstSeenFloorSql = FirstSeenFloorSqlExpression(grants.Db, utcNowSql, "ttlMinutes");

        // 单语句原子消费:
        // - FirstSeenAt > (DB_UTC - TTL)  ← 不在应用侧预读 cutoff
        // - AbsoluteNotAfterUtc > DB_UTC
        // - @configNotAfter > DB_UTC
        var sql = $"""
            UPDATE sys_level3_deploy_grant
            SET ConsumedAt = {utcNowSql}
            WHERE Kind = @kind
              AND GrantHash = @hash
              AND ConsumedAt IS NULL
              AND IsDelete = @isDelete
              AND FirstSeenAt > {firstSeenFloorSql}
              AND AbsoluteNotAfterUtc > {utcNowSql}
              AND @configNotAfter > {utcNowSql}
            """;

        var affected = await grants.Db.Ado.ExecuteCommandAsync(
            sql,
            new
            {
                kind,
                hash = grantHash,
                isDelete = false,
                ttlMinutes = ttl,
                configNotAfter = configNotAfterUtc,
            });

        if (affected != 1)
        {
            logger?.LogWarning(
                "Level3 deploy grant consume lost race or expired kind={Kind} hashPrefix={Hash} affected={N}",
                kind,
                grantHash.Length >= 8 ? grantHash[..8] : grantHash,
                affected);
            throw new AdminException(ErrorCode.BindInviteInvalid);
        }

        logger?.LogInformation(
            "Level3 deploy grant consumed kind={Kind} hashPrefix={Hash}",
            kind,
            grantHash.Length >= 8 ? grantHash[..8] : grantHash);
    }

    /// <summary>各库 UTC「当前时刻」SQL 片段(嵌入表达式,在语句执行时求值)。</summary>
    protected virtual string UtcNowSqlExpression(ISqlSugarClient db) =>
        db.CurrentConnectionConfig.DbType switch
        {
            DbType.SqlServer => "SYSUTCDATETIME()",
            DbType.PostgreSQL => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')",
            DbType.MySql => "UTC_TIMESTAMP(6)",
            DbType.Sqlite => "datetime('now')",
            _ => "CURRENT_TIMESTAMP",
        };

    /// <summary>
    /// first-seen 有效下限:DB_UTC - TTL 分钟(在执行时求值)。
    /// 形如 <c>DATEADD(minute, -@ttlMinutes, SYSUTCDATETIME())</c>。
    /// </summary>
    protected virtual string FirstSeenFloorSqlExpression(
        ISqlSugarClient db, string utcNowSql, string ttlParamName) =>
        db.CurrentConnectionConfig.DbType switch
        {
            DbType.SqlServer => $"DATEADD(minute, -@{ttlParamName}, {utcNowSql})",
            DbType.PostgreSQL => $"(({utcNowSql}) - (@{ttlParamName} * INTERVAL '1 minute'))",
            DbType.MySql => $"DATE_SUB({utcNowSql}, INTERVAL @{ttlParamName} MINUTE)",
            // SQLite: datetime('now', '-N minutes')
            DbType.Sqlite => $"datetime('now', '-' || CAST(@{ttlParamName} AS TEXT) || ' minutes')",
            _ => $"DATEADD(minute, -@{ttlParamName}, {utcNowSql})",
        };
}
