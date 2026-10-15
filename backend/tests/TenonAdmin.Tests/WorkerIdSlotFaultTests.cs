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


    // ---------- F01-C:取消优先于数据库分类(QA round4) ----------

    private static OperationCanceledException CancelWrapping(Exception? inner, out CancellationToken token)
    {
        token = new CancellationToken(true);
        return new OperationCanceledException("QA cancelled", inner, token);
    }

    private void AssertCancelPropagatedUnchanged(Injection inj, Exception injected, OperationCanceledException cancel, CancellationToken token)
    {
        var thrown = Record.Exception(() => { using var a = (IDisposable)Claim(inj); });
        Assert.NotNull(thrown);
        Assert.Same(injected, thrown);          // 未被吞成 false/跳槽,也未被重新包装
        Assert.Equal(token, cancel.CancellationToken);
        Assert.Contains(nameof(OnInsertable), injected.StackTrace); // 原始抛出栈未被重置
        Assert.Equal(1, inj.Attempts);          // 未重试
    }

    [Fact]
    public void Cancel_wrapping_sqlite_busy_propagates_without_retry_or_slot_skip()
    {
        var cancel = CancelWrapping(new SqliteException("busy", 5), out var token);
        var inj = new Injection { Slot = 0, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
    }

    [Fact]
    public void Cancel_wrapping_mysql_deadlock_propagates_without_retry()
    {
        var cancel = CancelWrapping(WorkerIdSlotErrorClassificationTests.MySqlError(1213, "40001", "Deadlock found when trying to get lock"), out var token);
        var inj = new Injection { Slot = 0, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
    }

    [Fact]
    public void Cancel_wrapping_duplicate_key_with_competitor_row_is_not_turned_into_false()
    {
        var cancel = CancelWrapping(new SqliteException("pk", 19, 1555), out var token);
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
    }

    [Fact]
    public void Cancel_wrapping_mysql_duplicate_with_competitor_row_is_not_turned_into_false()
    {
        var cancel = CancelWrapping(WorkerIdSlotErrorClassificationTests.MySqlError(1062, "23000", "Duplicate entry"), out var token);
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
    }

    [Fact]
    public void Plain_wrapper_around_cancel_around_lock_propagates_without_retry()
    {
        var cancel = CancelWrapping(new SqliteException("busy", 5), out var token);
        var outer = new InvalidOperationException("QA wrapper", cancel);
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Plain_wrapper_around_cancel_around_duplicate_with_competitor_propagates()
    {
        var cancel = CancelWrapping(new SqliteException("pk", 19, 1555), out var token);
        var outer = new InvalidOperationException("QA wrapper", cancel);
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Cancel_with_deadlock_fallback_message_propagates_without_retry()
    {
        var token = new CancellationToken(true);
        var cancel = new OperationCanceledException("Deadlock found when trying to get lock; Lock wait timeout exceeded", token);
        var inj = new Injection { Slot = 0, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
    }

    [Fact]
    public void Outer_lock_error_with_inner_cancel_propagates_without_retry()
    {
        var cancel = CancelWrapping(null, out var token);
        var outer = new WorkerIdSlotErrorClassificationTests.QaDbException("serialization failure", "40001", cancel);
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Outer_fallback_lock_message_with_inner_cancel_propagates_without_retry()
    {
        var cancel = CancelWrapping(null, out var token);
        var outer = new Exception("Lock wait timeout exceeded; try restarting transaction", cancel);
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    // ---------- 非取消回归 ----------

    [Fact]
    public void Duplicate_key_but_slot_missing_rethrows_original()
    {
        var dup = new SqliteException("pk", 19, 1555);
        var inj = new Injection { Slot = 0, OnAttempt = _ => dup };
        var thrown = Record.Exception(() => { using var a = (IDisposable)Claim(inj); });
        Assert.Same(dup, thrown);
        Assert.Equal(1, inj.Attempts);
    }

    [Fact]
    public void Mysql_duplicate_with_competitor_row_moves_to_next_slot()
    {
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => WorkerIdSlotErrorClassificationTests.MySqlError(1062, "23000", "Duplicate entry") };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(1, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(1, inj.Attempts);
    }

    [Fact]
    public void Mysql_lock_wait_timeout_then_success_stays_on_slot()
    {
        var inj = new Injection { Slot = 0, OnAttempt = n => n == 1 ? WorkerIdSlotErrorClassificationTests.MySqlError(1205, "HY000", "Lock wait timeout exceeded") : null };
        using var a = (IDisposable)Claim(inj);
        Assert.Equal(0, ((WorkerIdAssignment)a).WorkerId);
        Assert.Equal(2, inj.Attempts);
    }


    // ---------- F01-C 补充:AggregateException 多分支中的取消(QA round5) ----------

    [Fact]
    public void Aggregate_with_cancel_in_non_first_branch_propagates_without_retry()
    {
        var token = new CancellationToken(true);
        var cancel = new OperationCanceledException("QA cancelled", token);
        var inner = new AggregateException(new SqliteException("busy", 5), new AggregateException(new SqliteException("locked", 6), cancel));
        var outer = new AggregateException(new SqliteException("busy", 5), inner);
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Aggregate_duplicate_first_then_cancel_with_competitor_is_not_turned_into_false()
    {
        var token = new CancellationToken(true);
        var cancel = new OperationCanceledException("QA cancelled", token);
        var agg = new AggregateException(new SqliteException("pk", 19, 1555), cancel);
        var inj = new Injection { Slot = 0, InsertCompetitorFirst = true, OnAttempt = _ => agg };
        AssertCancelPropagatedUnchanged(inj, agg, cancel, token);
    }

    [Fact]
    public void Outer_lock_code_with_inner_aggregate_containing_cancel_propagates_without_retry()
    {
        var token = new CancellationToken(true);
        var cancel = new OperationCanceledException("QA cancelled", token);
        var outer = new WorkerIdSlotErrorClassificationTests.QaDbException("serialization failure", "40001",
            new AggregateException(new SqliteException("busy", 5), cancel));
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Outer_deadlock_text_with_inner_aggregate_containing_cancel_propagates_without_retry()
    {
        var token = new CancellationToken(true);
        var cancel = new OperationCanceledException("QA cancelled", token);
        var outer = new Exception("Deadlock found when trying to get lock",
            new AggregateException(WorkerIdSlotErrorClassificationTests.MySqlError(1213, "40001", "x"), cancel));
        var inj = new Injection { Slot = 0, OnAttempt = _ => outer };
        AssertCancelPropagatedUnchanged(inj, outer, cancel, token);
    }

    [Fact]
    public void Task_canceled_wrapping_mysql_deadlock_propagates_without_retry()
    {
        var token = new CancellationToken(true);
        var cancel = new TaskCanceledException("QA task cancelled", WorkerIdSlotErrorClassificationTests.MySqlError(1213, "40001", "Deadlock found when trying to get lock"), token);
        var inj = new Injection { Slot = 0, OnAttempt = _ => cancel };
        AssertCancelPropagatedUnchanged(inj, cancel, cancel, token);
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

    public sealed class QaDbException(string message, string sqlState, Exception? inner) : System.Data.Common.DbException(message, inner)
    {
        public override string SqlState { get; } = sqlState;
    }

    internal static Microsoft.Data.SqlClient.SqlException SqlServerError(int number)
    {
        var asm = typeof(Microsoft.Data.SqlClient.SqlException).Assembly;
        var errType = asm.GetType("Microsoft.Data.SqlClient.SqlError")!;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var ctor = errType.GetConstructors(flags).OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select((p, i) =>
            i == 0 ? (object)number :
            p.ParameterType == typeof(byte) ? (byte)0 :
            p.ParameterType == typeof(string) ? "qa" :
            p.ParameterType == typeof(int) ? 0 :
            p.ParameterType == typeof(uint) ? 0u :
            p.ParameterType == typeof(Exception) ? null! :
            p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!).ToArray();
        var error = ctor.Invoke(args);
        var colType = typeof(Microsoft.Data.SqlClient.SqlErrorCollection);
        var col = colType.GetConstructors(flags).First(c => c.GetParameters().Length == 0).Invoke(null);
        colType.GetMethod("Add", flags)!.Invoke(col, new[] { error });
        var create = typeof(Microsoft.Data.SqlClient.SqlException).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "CreateException").OrderBy(m => m.GetParameters().Length).First();
        var cargs = create.GetParameters().Select((p, i) => i == 0 ? col : p.ParameterType == typeof(string) ? "16.0" : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        return (Microsoft.Data.SqlClient.SqlException)create.Invoke(null, cargs)!;
    }

    private static Npgsql.PostgresException PgError(string sqlState) => new("qa", "ERROR", "ERROR", sqlState);

    [Fact]
    public void Non_cancel_structured_codes_across_drivers()
    {
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(MySqlError(1062, "23000", "x")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(MySqlError(1213, "40001", "x")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(MySqlError(1205, "HY000", "x")));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(PgError("23505")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(PgError("40P01")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(PgError("55P03")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(PgError("40001")));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(PgError("23505")));
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(PgError("40P01")));
        Assert.Equal(2627, SqlServerError(2627).Number);
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(SqlServerError(2627)));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(SqlServerError(2601)));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(SqlServerError(1205)));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(SqlServerError(1222)));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(SqlServerError(2627)));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new SqliteException("locked", 6)));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new SqliteException("busy", 5)));
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(new SqliteException("pk", 19, 1555)));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new Exception("Deadlock found when trying to get lock")));
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new QaDbException("x", "40001", null)));
        // 无取消的聚合仍按首个内层正常分类(QA round5)
        Assert.True(DbWorkerIdSlotClaimer.IsLockConflict(new AggregateException(new SqliteException("busy", 5), new InvalidOperationException("x"))));
        Assert.True(DbWorkerIdSlotClaimer.IsDuplicateKey(new AggregateException(new SqliteException("pk", 19, 1555))));
    }

    public static IEnumerable<object[]> CancelChains()
    {
        var t = new CancellationToken(true);
        Exception C(Exception? inner) => new OperationCanceledException("QA cancelled", inner, t);
        yield return new object[] { "cancel(sqlite busy)", C(new SqliteException("busy", 5)) };
        yield return new object[] { "cancel(sqlite locked)", C(new SqliteException("locked", 6)) };
        yield return new object[] { "cancel(sqlite pk)", C(new SqliteException("pk", 19, 1555)) };
        yield return new object[] { "cancel(mysql 1213)", C(MySqlError(1213, "40001", "x")) };
        yield return new object[] { "cancel(mysql 1205)", C(MySqlError(1205, "HY000", "x")) };
        yield return new object[] { "cancel(mysql 1062)", C(MySqlError(1062, "23000", "x")) };
        yield return new object[] { "cancel(pg 40P01)", C(PgError("40P01")) };
        yield return new object[] { "cancel(pg 23505)", C(PgError("23505")) };
        yield return new object[] { "cancel(mssql 1205)", C(SqlServerError(1205)) };
        yield return new object[] { "cancel(mssql 2627)", C(SqlServerError(2627)) };
        yield return new object[] { "task-cancel(sqlite busy)", new TaskCanceledException("QA", new SqliteException("busy", 5)) };
        yield return new object[] { "wrap(cancel(sqlite busy))", new InvalidOperationException("w", C(new SqliteException("busy", 5))) };
        yield return new object[] { "wrap(cancel(mysql 1062))", new Exception("w", C(MySqlError(1062, "23000", "x"))) };
        yield return new object[] { "cancel msg fallback", new OperationCanceledException("Deadlock found when trying to get lock", t) };
        yield return new object[] { "dbex 40001(cancel)", new QaDbException("x", "40001", C(null)) };
        yield return new object[] { "msg fallback(cancel)", new Exception("Lock wait timeout exceeded", C(null)) };
        yield return new object[] { "agg(busy, cancel)", new AggregateException(new SqliteException("busy", 5), C(null)) };
        yield return new object[] { "agg(pk, cancel)", new AggregateException(new SqliteException("pk", 19, 1555), C(null)) };
        yield return new object[] { "agg(busy, agg(1062, cancel))", new AggregateException(new SqliteException("busy", 5), new AggregateException(MySqlError(1062, "23000", "x"), C(null))) };
        yield return new object[] { "dbex 40001(agg(busy, cancel))", new QaDbException("x", "40001", new AggregateException(new SqliteException("busy", 5), C(null))) };
        yield return new object[] { "msg fallback(agg(1213, cancel))", new Exception("Deadlock found when trying to get lock", new AggregateException(MySqlError(1213, "40001", "x"), C(null))) };
        yield return new object[] { "task-cancel(mysql 1213)", new TaskCanceledException("QA", MySqlError(1213, "40001", "x"), t) };
    }

    [Theory]
    [MemberData(nameof(CancelChains))]
    public void Cancellation_anywhere_in_chain_is_neither_lock_nor_duplicate(string name, Exception ex)
    {
        Assert.False(DbWorkerIdSlotClaimer.IsLockConflict(ex), $"IsLockConflict({name})");
        Assert.False(DbWorkerIdSlotClaimer.IsDuplicateKey(ex), $"IsDuplicateKey({name})");
    }
}
