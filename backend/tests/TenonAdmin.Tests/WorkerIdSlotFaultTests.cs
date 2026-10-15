using Microsoft.Data.Sqlite;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// F01 回归:对真实 <see cref="DbWorkerIdSlotClaimer"/> + 真实数据库做故障注入。
/// 注入点是 SqlSugar 的全局 <see cref="StaticConfig.CompleteInsertableFunc"/>(在 TryOwnSlot 的 INSERT 构建处触发),
/// 只对本测试线程(AsyncLocal)、只对 sys_worker_lease 生效,不改生产代码路径。
/// </summary>
[Collection(WorkerIdSlotFaultCollection.Name)]
public sealed class WorkerIdSlotFaultTests : IDisposable
{
    private sealed class Injection
    {
        public required int Slot { get; init; }
        public required Func<int, Exception?> OnAttempt { get; init; }
        public bool InsertCompetitorFirst { get; init; }
        public int Attempts;
    }

    private static readonly AsyncLocal<Injection?> Current = new();
    private static readonly object HookGate = new();
    private static int _hookUsers;
    private static Action<object>? _previousHook;

    private readonly string _identity = $"wid-fault-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly AdminDatabaseOptions _db;
    private readonly string _lockDir;

    public WorkerIdSlotFaultTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_identity}.db");
        _db = new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(_identity, _dbFile) };
        _lockDir = Path.Combine(Path.GetTempPath(), _identity);
        Directory.CreateDirectory(_lockDir);
        lock (HookGate)
        {
            if (_hookUsers++ == 0)
            {
                _previousHook = StaticConfig.CompleteInsertableFunc;
                StaticConfig.CompleteInsertableFunc = OnInsertable;
            }
        }
    }

    public void Dispose()
    {
        Current.Value = null;
        lock (HookGate)
        {
            if (--_hookUsers == 0) StaticConfig.CompleteInsertableFunc = _previousHook;
        }
        TestDb.Cleanup(_identity, _dbFile);
        if (TestDb.UseMySql) MySqlConnector.MySqlConnection.ClearAllPools();
        try { Directory.Delete(_lockDir, true); } catch { }
    }

    private void OnInsertable(object insertable)
    {
        _previousHook?.Invoke(insertable);
        var inj = Current.Value;
        if (inj is null || !insertable.GetType().IsGenericType
            || insertable.GetType().GetGenericArguments()[0] != typeof(SysWorkerLease))
            return;
        // 只拦目标槽:取 InsertObjs 里的 WorkerId
        var objs = insertable.GetType().GetProperty("InsertObjs")?.GetValue(insertable) as SysWorkerLease[];
        if (objs is not { Length: 1 } || objs[0].WorkerId != inj.Slot) return;
        var attempt = Interlocked.Increment(ref inj.Attempts);
        Current.Value = null; // 防止竞争者写入时递归
        try
        {
            if (inj.InsertCompetitorFirst && attempt == 1) InsertCompetitor(inj.Slot);
        }
        finally { Current.Value = inj; }
        var ex = inj.OnAttempt(attempt);
        if (ex is not null) throw ex;
    }

    private void InsertCompetitor(int slot)
    {
        using var other = new SqlSugarClient(new ConnectionConfig
        {
            DbType = Enum.Parse<global::SqlSugar.DbType>(_db.DbType, true),
            ConnectionString = _db.ConnectionString,
            IsAutoCloseConnection = true,
        });
        other.Insertable(new SysWorkerLease
        {
            Id = slot + 1, WorkerId = slot, NodeName = $"qa-other#{slot}@comp", MachineName = "qa-other-machine",
            Pid = 1, LeaseExpiresAt = DateTime.Now.AddHours(1),
        }).ExecuteCommand();
    }

    private WorkerIdAssignment Claim(Injection inj)
    {
        Current.Value = inj;
        try
        {
            return new DbWorkerIdSlotClaimer(_db, new AdminJobsOptions())
                .Claim(new AdminIdOptions { WorkerIdLockDir = _lockDir });
        }
        finally { Current.Value = null; }
    }

    [Fact]
    public void Real_duplicate_key_race_marks_slot_taken_and_moves_on()
    {
        // 竞争者在本宿主查完空槽之后、INSERT 之前写入同槽 -> 数据库真实主键/唯一冲突
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => null };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(1, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(1, inj.Attempts); // 主键冲突不重试
    }

    [Fact]
    public void Non_transient_error_propagates_even_if_slot_row_exists_afterwards()
    {
        var injected = new InvalidOperationException("QA injected non-transient failure");
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => injected };
        var thrown = Record.Exception(() => { using var a = (IDisposable)Claim(inj); });
        Assert.NotNull(thrown);
        Assert.Same(injected, Unwrap(thrown!, injected));
        Assert.Contains(nameof(OnInsertable), injected.StackTrace);
        Assert.Equal(1, inj.Attempts);
    }

    [Fact]
    public void Cancellation_propagates_without_retry()
    {
        var injected = new OperationCanceledException("QA injected cancel");
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => injected };
        var thrown = Record.Exception(() => { using var a = (IDisposable)Claim(inj); });
        Assert.Same(injected, Unwrap(thrown!, injected));
        Assert.Equal(1, inj.Attempts);
    }

    [Fact]
    public void Lock_conflict_is_retried_then_succeeds_on_same_slot()
    {
        var inj = new Injection { Slot = 0, OnAttempt = n => n == 1 ? new SqliteException("database is locked", 5) : null };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(0, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(2, inj.Attempts);
    }

    [Fact]
    public void Persistent_lock_conflict_uses_at_most_three_attempts_then_next_slot()
    {
        var inj = new Injection { Slot = 0, OnAttempt = _ => new SqliteException("database is locked", 5) };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(1, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(3, inj.Attempts);
    }

    [Fact]
    public void Lock_conflict_while_competitor_holds_slot_still_moves_on()
    {
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => new SqliteException("database is locked", 5) };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(1, ((WorkerIdAssignment)a).WorkerId);
    }

    [Fact]
    public void MySql_deadlock_error_is_retried_up_to_three_attempts_then_next_slot()
    {
        var inj = new Injection { Slot = 0, OnAttempt = _ => WorkerIdSlotErrorClassificationTests.MySqlError(1213, "40001", "Deadlock found when trying to get lock") };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(1, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(3, inj.Attempts);
    }

    private static Exception Unwrap(Exception thrown, Exception expected)
    {
        for (var e = thrown; e is not null; e = e.InnerException)
            if (ReferenceEquals(e, expected)) return e;
        return thrown;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerIdSlotFaultCollection
{
    public const string Name = "WorkerIdSlotFault";
}

public sealed class WorkerIdSlotErrorClassificationTests
{
    internal static MySqlConnector.MySqlException MySqlError(int number, string sqlState, string message)
    {
        var ctor = typeof(MySqlConnector.MySqlException).GetConstructors(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .First(c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                new[] { typeof(MySqlConnector.MySqlErrorCode), typeof(string), typeof(string) }));
        return (MySqlConnector.MySqlException)ctor.Invoke(new object[] { (MySqlConnector.MySqlErrorCode)number, sqlState, message });
    }

    [Fact]
    public void Structured_codes_classify_lock_and_duplicate_errors()
    {
        var deadlock = MySqlError(1213, "40001", "x");
        var lockWait = MySqlError(1205, "HY000", "x");
        var dup = MySqlError(1062, "23000", "x");
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(deadlock));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new InvalidOperationException("wrap", lockWait)));
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(deadlock));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(new Exception("wrap", dup)));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(dup));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new SqliteException("busy", 5)));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(new SqliteException("pk", 19, 1555)));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(new SqliteException("uq", 19, 2067)));
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(new SqliteException("notnull", 19, 1299)));
        // 消息里出现 Duplicate entry 但没有结构化码,不算主键冲突
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(new Exception("Duplicate entry '1' for key 'PRIMARY'")));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(new OperationCanceledException()));
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(new OperationCanceledException()));
    }
}
