using System.Diagnostics;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 未配 <c>WorkerId</c> 时在 <c>sys_worker_lease</c> 上从 0 试到 63：谁 <c>INSERT</c> 成功谁用谁。
/// 用独立短连接，不经过带审计 AOP 的 <see cref="ISqlSugarClient"/>（那个工厂还要发号器）。
/// </summary>
public sealed class DbWorkerIdSlotClaimer(
    AdminDatabaseOptions database,
    AdminJobsOptions? jobs = null,
    TimeProvider? time = null) : IWorkerIdSlotClaimer
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly int _heartbeatSeconds = Math.Max(jobs?.HeartbeatSeconds ?? 10, 2);

    /// <inheritdoc />
    public WorkerIdAssignment Claim(AdminIdOptions? options, WorkerIdLockDirFallback? onFallback = null)
    {
        if (options?.WorkerId is not null || options is null)
            return WorkerIdAssignment.Resolve(options, onFallback);

        var client = OpenBareClient();
        try
        {
            RetryOnLockConflict(() =>
            {
                // #62:SqlSugar 的 InitTables 先跑「系统表权限」探测,该探测吞掉驱动异常后只抛
                // 「Dbfirst and Codefirst requires system table permissions」(无 InnerException),锁冲突因此无法识别。
                // SQLite 上先用不吞异常的同类查询探一次,让 BUSY/LOCKED 原样抛出、进入 #52 的有界重试。
                if (client.CurrentConnectionConfig.DbType == DbType.Sqlite)
                    client.DbMaintenance.GetTableInfoList(isCache: false);
                client.CodeFirst.InitTables<SysWorkerLease>();
                return true;
            }, onExhausted: null);
        }
        catch (Exception ex) when (ShouldWrapTableInitLockConflict(client.CurrentConnectionConfig.DbType, ex))
        {
            throw new InvalidOperationException(
                $"领取 WorkerId 时 sys_worker_lease 所在数据库被其他连接锁住，建表阶段重试 {MaxLockConflictAttempts} 次仍冲突。" +
                "请确认没有长事务占用该库，或显式配置 TenonAdmin:Id:WorkerId。", ex);
        }

        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        var ttl = TimeSpan.FromSeconds(_heartbeatSeconds * 3);
        var token = Guid.NewGuid().ToString("N")[..8];

        for (var n = 0; n <= SnowflakeIdGenerator.MaxWorkerId; n++)
        {
            var slot = n;
            // 多宿主同时抢槽时 MySQL 可能把某条 INSERT 选为死锁牺牲者(1213)。该语句已被整句回滚、不留行,
            // 原样重试同一槽最多 MaxLockConflictAttempts 次;仍冲突就当没抢到、继续下一个槽(跳槽不影响唯一性)。
            if (!RetryOnLockConflict(() => TryOwnSlot(client, slot, machine, pid, ttl, token), onExhausted: false))
                continue;

            var file = WorkerIdLease.TryAcquire(n, options.WorkerIdLockDir, onFallback);
            if (file is null)
            {
                client.Deleteable<SysWorkerLease>()
                    .Where(l => l.WorkerId == n && l.Pid == pid && l.MachineName == machine)
                    .ExecuteCommand();
                continue;
            }

            return WorkerIdAssignment.FromClaimedSlot(n, file, token);
        }

        throw new InvalidOperationException(
            $"机器号 0–{SnowflakeIdGenerator.MaxWorkerId} 在 sys_worker_lease 上已被占满，拒绝发号以避免重复。" +
            "请检查是否有实例未退出，或为各实例显式配置不同的 TenonAdmin:Id:WorkerId。");
    }

    internal const int MaxLockConflictAttempts = 3;

    /// <summary>
    /// 建表阶段锁冲突重试耗尽后,是否改抛带中文说明的 <see cref="InvalidOperationException"/>(#62)。
    /// 只限 SQLite:SQLite 的锁错误原本会被 SqlSugar 包装成误导性的「需要系统表权限」,需要明确说明。
    /// MySQL / PostgreSQL / SQL Server 保持基线行为,原样抛出驱动异常,不改变异常类型。
    /// 取消不在此列(<see cref="IsLockConflict"/> 先排除取消,F01-C)。
    /// </summary>
    public static bool ShouldWrapTableInitLockConflict(DbType dbType, Exception ex) =>
        dbType == DbType.Sqlite && IsLockConflict(ex);

    private static bool RetryOnLockConflict(Func<bool> action, bool? onExhausted)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsLockConflict(ex))
            {
                if (attempt >= MaxLockConflictAttempts)
                {
                    if (onExhausted is { } fallback)
                        return fallback;
                    throw;
                }
                Thread.Sleep(Random.Shared.Next(10, 50) * attempt);
            }
        }
    }

    /// <summary>
    /// 死锁 / 锁等待:优先按驱动结构化错误码判断(沿 InnerException 链):
    /// MySQL 1213/1205,PostgreSQL SQLSTATE 40P01/55P03,SQL Server 1205/1222,SQLite BUSY(5)/LOCKED(6);
    /// 其余 <see cref="System.Data.Common.DbException"/> 看 SQLSTATE 40001/40P01。
    /// 保留 #52 的消息兜底,仅用于驱动包装后丢了错误码的情况。
    /// </summary>
    public static bool IsLockConflict(Exception ex)
    {
        if (IsCancellation(ex))
            return false;
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case MySqlConnector.MySqlException my when my.Number is 1213 or 1205:
                    return true;
                case Npgsql.PostgresException pg when pg.SqlState is "40P01" or "55P03":
                    return true;
                case Microsoft.Data.SqlClient.SqlException ms when ms.Number is 1205 or 1222:
                    return true;
                case Microsoft.Data.Sqlite.SqliteException lite when lite.SqliteErrorCode is 5 or 6:
                    return true;
                case System.Data.Common.DbException db when db.SqlState is "40001" or "40P01":
                    return true;
            }
            if (e.Message.Contains("Deadlock found when trying to get lock", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Lock wait timeout exceeded", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("was deadlocked on lock", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 主键/唯一约束冲突,只认驱动结构化错误码:MySQL 1062,PostgreSQL 23505,SQL Server 2627/2601,
    /// SQLite CONSTRAINT_PRIMARYKEY(1555)/CONSTRAINT_UNIQUE(2067)。
    /// </summary>
    public static bool IsDuplicateKey(Exception ex)
    {
        if (IsCancellation(ex))
            return false;
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case MySqlConnector.MySqlException my when my.Number == 1062:
                case Npgsql.PostgresException pg when pg.SqlState == "23505":
                case Microsoft.Data.SqlClient.SqlException ms when ms.Number is 2627 or 2601:
                case Microsoft.Data.Sqlite.SqliteException lite when lite.SqliteExtendedErrorCode is 1555 or 2067:
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 取消优先(F01-C):InnerException 链上任意一层是 <see cref="OperationCanceledException"/>
    /// 就不按数据库错误分类,让调用方原样抛出——不重试、不跳槽、不当作「槽已被占」。
    /// 必须先整链检查,不能在外层锁错误码 / 消息兜底命中时提前返回 true。
    /// </summary>
    private static bool IsCancellation(Exception ex)
    {
        // 遍历完整结构:InnerException 链 + AggregateException 的全部分支(不只首个)。
        // 用引用去重防环,并限制节点数防止病态深度。
        const int maxNodes = 256;
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        pending.Push(ex);
        while (pending.Count > 0 && seen.Count < maxNodes)
        {
            var e = pending.Pop();
            if (!seen.Add(e))
                continue;
            if (e is OperationCanceledException)
                return true;
            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                    if (inner is not null) pending.Push(inner);
            }
            else if (e.InnerException is { } next)
            {
                pending.Push(next);
            }
        }
        return false;
    }

    private ISqlSugarClient OpenBareClient()
    {
        if (string.IsNullOrWhiteSpace(database.ConnectionString))
            throw new InvalidOperationException("数据库连接串为空，无法在 sys_worker_lease 上领取 WorkerId。");

        if (!Enum.TryParse<DbType>(database.DbType, ignoreCase: true, out var dbType))
            throw new InvalidOperationException($"未知 DbType \"{database.DbType}\"，无法领取 WorkerId。");

        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConfigId = "TenonAdmin.WorkerIdClaim",
            DbType = dbType,
            ConnectionString = database.ConnectionString,
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
        // #62:SqlSugar 默认 CommandTimeOut=300s 会覆盖 Microsoft.Data.Sqlite 的 busy 等待(连接串 Default Timeout),
        // 锁住时单条语句要等 5 分钟。SQLite 改为沿用连接串里的 Default Timeout(未配时驱动默认 30s);其他库不变。
        if (dbType == DbType.Sqlite)
            client.Ado.CommandTimeOut = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(database.ConnectionString).DefaultTimeout;
        return client;
    }

    private bool TryOwnSlot(ISqlSugarClient db, int workerId, string machine, int pid, TimeSpan ttl, string token)
    {
        var now = _time.GetLocalNow().DateTime;
        var expiresAt = now + ttl;
        var nodeName = $"{machine}#{workerId}@{token}";

        var existing = db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == workerId).First();
        if (existing is null)
        {
            try
            {
                // 短连接没有审计 AOP，主键必须手填；WorkerId 0 不能当 PK 0 去撞第二行。
                db.Insertable(new SysWorkerLease
                {
                    Id = workerId + 1,
                    WorkerId = workerId,
                    NodeName = nodeName,
                    MachineName = machine,
                    Pid = pid,
                    LeaseExpiresAt = expiresAt,
                }).ExecuteCommand();
                return true;
            }
            catch (Exception ex) when (IsDuplicateKey(ex))
            {
                // 只有真实的主键/唯一冲突才算「别人先插了」。锁冲突交给外层 RetryOnLockConflict,
                // 取消与其它异常原样向上抛(即使事后槽位行已存在),不在这里吞掉。
                if (db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == workerId).Any())
                    return false;
                throw;
            }
        }

        // 不能用 pid 认「自己」：单测里两个宿主同进程，pid 相同，会把对方的 0 号抢走。
        var liveOther = existing.LeaseExpiresAt > now
            && existing.NodeName != nodeName
            && !OwnerProcessIsGone(existing);
        if (liveOther)
            return false;

        var rows = db.Updateable<SysWorkerLease>()
            .SetColumns(l => new SysWorkerLease
            {
                NodeName = nodeName,
                MachineName = machine,
                Pid = pid,
                LeaseExpiresAt = expiresAt,
            })
            .Where(l => l.WorkerId == workerId && l.NodeName == existing.NodeName)
            .ExecuteCommand();
        return rows > 0;
    }

    private static bool OwnerProcessIsGone(SysWorkerLease lease)
    {
        if (lease.Pid <= 0
            || !string.Equals(OwnerMachine(lease), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var owner = Process.GetProcessById(lease.Pid);
            return owner.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string OwnerMachine(SysWorkerLease lease)
    {
        if (!string.IsNullOrEmpty(lease.MachineName))
            return lease.MachineName;
        var hash = lease.NodeName.IndexOf('#');
        return hash > 0 ? lease.NodeName[..hash] : "";
    }
}
