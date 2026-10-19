using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;
using TenonAdmin.Core;
using TenonAdmin.Services;
using Xunit.Abstractions;

namespace TenonAdmin.Tests;

/// <summary>
/// #70:SQL Server 建表阶段遇到锁超时(1222)时,应按错误码识别为锁冲突、有界重试 3 次后
/// 原样抛出 <see cref="SqlException"/>,不包装成 SQLite 的中文异常。
/// 构造方式:删列制造结构漂移 → Claim 必然 ALTER TABLE ADD;持锁连接用 TABLOCKX + HOLDLOCK 不提交;
/// DiagnosticListener 在 Claim 的真实 ALTER 命令连接上设置会话级 LOCK_TIMEOUT 1000,不修改实例设置。
/// 仅 SQL Server 腿运行。
/// </summary>
// 三段 1s 锁等待用于证明重试次数;领取器的进程级 gate 与 Stress/Race 共享,
// 独占测试进程中的执行时段,避免其它用例的 gate 排队污染 30 秒上限。
[Collection(WorkerIdTableInitLockSqlServerCollection.Name)]
public sealed class WorkerIdTableInitLockSqlServerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _identity = $"wid-mssqllk-{Guid.NewGuid():N}"[..24];
    private readonly string _dbFile;
    private readonly string _lockDir;

    public WorkerIdTableInitLockSqlServerTests(ITestOutputHelper output)
    {
        _output = output;
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");
        _lockDir = Path.Combine(Path.GetTempPath(), _identity);
        Directory.CreateDirectory(_lockDir);
    }

    public void Dispose()
    {
        if (TestDb.UseSqlServer) TestDb.Cleanup(_identity, _dbFile);
        try { Directory.Delete(_lockDir, true); } catch { }
    }

    private sealed class Outcome(Exception? error, WorkerIdAssignment? assignment) : IDisposable
    {
        public Exception? Error { get; } = error;
        public void Dispose() => assignment?.Dispose();
    }

    [Fact]
    public async Task Lock_timeout_during_table_init_is_retried_three_times_then_original_sqlserver_error_propagates()
    {
        if (!TestDb.UseSqlServer) return;

        var baseCs = TestDb.ConnectionString(_identity, _dbFile);
        var cs = new SqlConnectionStringBuilder(baseCs) { Pooling = false }.ConnectionString;
        var applicationName = $"TenonWorkerIdLock-{_identity}";
        var claimCs = new SqlConnectionStringBuilder(baseCs)
        {
            Pooling = false,
            ApplicationName = applicationName
        }.ConnectionString;
        WorkerIdAssignment Claim(string connectionString) =>
            new DbWorkerIdSlotClaimer(
                    new AdminDatabaseOptions { DbType = "SqlServer", ConnectionString = connectionString },
                    new AdminJobsOptions())
                .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });

        using (Claim(cs)) { } // 建表
        using (var c = new SqlConnection(cs))
        {
            c.Open();
            Exec(c, "ALTER TABLE dbo.sys_worker_lease DROP COLUMN MachineName;");
        }

        using var holder = new SqlConnection(cs);
        holder.Open();
        Exec(holder,
            "BEGIN TRANSACTION; " +
            "SELECT TOP (1) * FROM dbo.sys_worker_lease WITH (TABLOCKX, HOLDLOCK);");
        var holderSessionId = Convert.ToInt32(Scalar(holder, "SELECT @@SPID;"));

        using var diagnostic = new LockTimeoutDiagnosticObserver(applicationName);
        var waits = new List<(int SessionId, int RequestId, DateTime StartTime, int LockTimeout)>();
        var done = 0;
        var sw = Stopwatch.StartNew();
        await WorkerIdTableInitLockTests.ClaimWhileReleasing(
            claim: () =>
            {
                try { return new Outcome(null, Claim(claimCs)); }
                catch (Exception e) { return new Outcome(e, null); }
                finally { sw.Stop(); Volatile.Write(ref done, 1); }
            },
            release: () =>
            {
                // 每次重试是一个新的 ALTER 请求;按 SQL Server 请求生命周期去重统计 3 段真实锁等待。
                using var monitor = new SqlConnection(cs);
                monitor.Open();
                var seen = new HashSet<(int SessionId, int RequestId, DateTime StartTime)>();
                while (Volatile.Read(ref done) == 0)
                {
                    if (sw.Elapsed >= TimeSpan.FromSeconds(30))
                        throw new TimeoutException("30 秒内 Claim 未结束;将释放持锁事务并使测试失败。");

                    using var command = new SqlCommand(
                        "SELECT r.session_id, r.request_id, r.start_time, s.lock_timeout " +
                        "FROM sys.dm_exec_requests AS r " +
                        "JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id " +
                        "CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t " +
                        "WHERE r.database_id = DB_ID() " +
                        "AND r.blocking_session_id = @holder " +
                        "AND s.program_name = @applicationName " +
                        "AND r.wait_type LIKE N'LCK_M_%' " +
                        "AND t.text LIKE N'%ALTER TABLE%sys_worker_lease%';",
                        monitor);
                    command.Parameters.AddWithValue("@holder", holderSessionId);
                    command.Parameters.AddWithValue("@applicationName", applicationName);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var wait = (
                                SessionId: Convert.ToInt32(reader.GetValue(0)),
                                RequestId: reader.GetInt32(1),
                                StartTime: reader.GetDateTime(2),
                                LockTimeout: reader.GetInt32(3));
                            if (seen.Add((wait.SessionId, wait.RequestId, wait.StartTime))) waits.Add(wait);
                        }
                    }
                    Thread.Sleep(50);
                }
            },
            dispose: () =>
            {
                try { Exec(holder, "ROLLBACK TRANSACTION;"); }
                finally { holder.Dispose(); }
            },
            verify: outcome =>
            {
                _output.WriteLine(
                    "attempts={0}; sessions={1}; session-timeouts={2}; applied={3}; elapsed-ms={4}",
                    waits.Count,
                    string.Join(',', waits.Select(x => x.SessionId)),
                    string.Join(',', waits.Select(x => x.LockTimeout)),
                    diagnostic.AppliedCount,
                    sw.ElapsedMilliseconds);

                var ex = outcome.Error;
                Assert.NotNull(ex);
                var sql = Assert.IsType<SqlException>(ex); // 原始驱动异常,不是 SqlSugarException / 中文 InvalidOperationException
                Assert.Equal(1222, sql.Number);
                Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(ex!));
                Assert.False(DbWorkerIdSlotClaimer.ShouldWrapTableInitLockConflict(global::SqlSugar.DbType.SqlServer, ex!));
                Assert.Equal(3, diagnostic.AppliedCount); // MaxLockConflictAttempts
                Assert.Equal(3, waits.Count);
                Assert.All(waits, wait => Assert.Equal(1000, wait.LockTimeout));
                Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2.5), $"3 次 × 1s 锁等待,实际 {sw.Elapsed}");
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"应有界失败,实际 {sw.Elapsed}");
            });

        using var check = new SqlConnection(cs);
        check.Open();
        Assert.Equal(0, Convert.ToInt32(Scalar(check,
            "SELECT COUNT(*) FROM sys.columns " +
            "WHERE object_id = OBJECT_ID(N'dbo.sys_worker_lease') AND name = N'MachineName';")));
    }

    private static void Exec(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    /// <summary>
    /// SqlClient 在执行目标 ALTER 前发出同步诊断事件;在同一真实连接和事务上设置会话级锁超时。
    /// 订阅是进程级的,因此同时按唯一 ApplicationName 和目标 DDL 过滤。
    /// </summary>
    private sealed class LockTimeoutDiagnosticObserver :
        IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private const string ListenerName = "SqlClientDiagnosticListener";
        private const string BeforeCommand = "Microsoft.Data.SqlClient.WriteCommandBefore";
        private readonly string _applicationName;
        private readonly object _subscriptionsGate = new();
        private readonly ConcurrentBag<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListenersSubscription;
        private bool _disposed;
        private int _appliedCount;

        [ThreadStatic]
        private static bool settingTimeout;

        public LockTimeoutDiagnosticObserver(string applicationName)
        {
            _applicationName = applicationName;
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public int AppliedCount => Volatile.Read(ref _appliedCount);

        public void OnNext(DiagnosticListener listener)
        {
            if (!string.Equals(listener.Name, ListenerName, StringComparison.Ordinal)) return;
            var subscription = listener.Subscribe(this, eventName =>
                string.Equals(eventName, BeforeCommand, StringComparison.Ordinal));
            lock (_subscriptionsGate)
            {
                if (_disposed) subscription.Dispose();
                else _subscriptions.Add(subscription);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (settingTimeout || !string.Equals(value.Key, BeforeCommand, StringComparison.Ordinal)) return;
            var command = value.Value?.GetType()
                .GetProperty("Command", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(value.Value) as SqlCommand;
            if (command?.Connection is null || !IsTargetCommand(command)) return;

            settingTimeout = true;
            try
            {
                using var timeout = new SqlCommand("SET LOCK_TIMEOUT 1000;", command.Connection, command.Transaction);
                timeout.ExecuteNonQuery();
                Interlocked.Increment(ref _appliedCount);
            }
            finally { settingTimeout = false; }
        }

        private bool IsTargetCommand(SqlCommand command)
        {
            if (!command.CommandText.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains("sys_worker_lease", StringComparison.OrdinalIgnoreCase))
                return false;
            var actualApplicationName = new SqlConnectionStringBuilder(command.Connection!.ConnectionString).ApplicationName;
            return string.Equals(actualApplicationName, _applicationName, StringComparison.Ordinal);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptionsGate) _disposed = true;
            _allListenersSubscription.Dispose();
            while (_subscriptions.TryTake(out var subscription)) subscription.Dispose();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerIdTableInitLockSqlServerCollection
{
    public const string Name = "WorkerIdTableInitLockSqlServer";
}
