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
        RetryOnLockConflict(() => { client.CodeFirst.InitTables<SysWorkerLease>(); return true; }, onExhausted: null);

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

    /// <summary>死锁 / 锁等待超时:MySQL 1213(SQLSTATE 40001)/1205,PostgreSQL 40P01,SQL Server 1205(按消息)。</summary>
    public static bool IsLockConflict(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Data.Common.DbException db
                && db.SqlState is "40001" or "40P01")
                return true;
            if (e.Message.Contains("Deadlock found when trying to get lock", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Lock wait timeout exceeded", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("was deadlocked on lock", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private ISqlSugarClient OpenBareClient()
    {
        if (string.IsNullOrWhiteSpace(database.ConnectionString))
            throw new InvalidOperationException("数据库连接串为空，无法在 sys_worker_lease 上领取 WorkerId。");

        if (!Enum.TryParse<DbType>(database.DbType, ignoreCase: true, out var dbType))
            throw new InvalidOperationException($"未知 DbType \"{database.DbType}\"，无法领取 WorkerId。");

        return new SqlSugarClient(new ConnectionConfig
        {
            ConfigId = "TenonAdmin.WorkerIdClaim",
            DbType = dbType,
            ConnectionString = database.ConnectionString,
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
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
            catch (Exception)
            {
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
