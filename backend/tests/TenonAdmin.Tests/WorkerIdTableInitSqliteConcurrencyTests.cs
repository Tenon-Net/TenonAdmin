using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using Xunit.Abstractions;

namespace TenonAdmin.Tests;

/// <summary>SqlSugar 的 CodeFirst 锁是进程级的,本用例独占运行以固定两个调用方的锁获取顺序。</summary>
[Collection(WorkerIdTableInitSqliteConcurrencyCollection.Name)]
public sealed class WorkerIdTableInitSqliteConcurrencyTests(ITestOutputHelper output)
{
    [Fact]
    public void Claim_and_bare_init_tables_do_not_invert_sqlite_write_and_codefirst_locks()
    {
        // 始终使用独立 SQLite 库;非 SQLite 矩阵也执行这条 SQLite 锁序回归。
        var root = Path.Combine(Path.GetTempPath(), $"wid-init-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "case.db")};Default Timeout=1;Pooling=False";
        SqlSugarClient OpenClient()
        {
            var client = new SqlSugarClient(new ConnectionConfig
            {
                DbType = global::SqlSugar.DbType.Sqlite,
                ConnectionString = connectionString,
                IsAutoCloseConnection = true,
            });
            client.Ado.CommandTimeOut = 1;
            return client;
        }

        WorkerIdAssignment? assignment = null;
        var abandoned = 0;
        var workerErrors = new ConcurrentQueue<Exception>();
        var correlationId = Guid.NewGuid().ToString("N");
        using var contentionListener = new CodeFirstContentionListener(correlationId);
        var claimThread = new Thread(() =>
        {
            try
            {
                CodeFirstTestEvents.Log.ThreadRole(correlationId, "claim");
                var claimed = new DbWorkerIdSlotClaimer(new AdminDatabaseOptions
                {
                    DbType = "Sqlite", ConnectionString = connectionString,
                }).Claim(new AdminIdOptions { WorkerIdLockDir = Path.Combine(root, "locks") });
                if (Interlocked.CompareExchange(ref assignment, claimed, null) is not null)
                {
                    claimed.Dispose();
                    throw new InvalidOperationException("Claim 返回了重复的 WorkerId 租约");
                }

                // Join 超时后由主线程和工作线程竞争取走迟返租约,保证只释放一次。
                if (Volatile.Read(ref abandoned) != 0)
                    Interlocked.Exchange(ref assignment, null)?.Dispose();
            }
            catch (Exception ex) { workerErrors.Enqueue(ex); }
        }) { IsBackground = true };
        var started = false;
        var retainRoot = false;
        var errors = new List<Exception>();
        try
        {
            using (var control = OpenClient())
                control.CodeFirst.InitTables<SysWorkerLease>();

            // 反射只用于调度,实际执行的仍是 public Claim 和驱动的真实 InitTables。
            // SqlSugar 升级后若内部锁字段变化或运行时锁事件字段变化,必须明确失败。
            var provider = typeof(SqlSugarClient).Assembly.GetType("SqlSugar.CodeFirstProvider", throwOnError: true)!;
            var field = provider.GetField("LockObject", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var codeFirstGate = field.GetValue(null)!;
            var lockIdentity = CalibrateCodeFirstGate(contentionListener, correlationId, codeFirstGate);

            lock (codeFirstGate)
            {
                CodeFirstTestEvents.Log.ThreadRole(correlationId, "claim-owner");
                Assert.True(contentionListener.TryWaitForRole("claim-owner", TimeSpan.FromSeconds(10), out var ownerThreadId),
                    "未收到 Claim 调度线程的运行时线程标记");

                claimThread.Start();
                started = true;
                Assert.True(contentionListener.TryWaitForRole("claim", TimeSpan.FromSeconds(10), out var claimThreadId),
                    "未收到 Claim 线程的运行时线程标记");
                Assert.True(contentionListener.TryWaitForContention(
                        claimThreadId, ownerThreadId, lockIdentity, TimeSpan.FromSeconds(10)),
                    $"Claim 未在真实 CodeFirst 锁上等待,不能执行未固定调度的锁序回归;{contentionListener.Describe()}");
                output.WriteLine("确切 CodeFirst contention matched=true");

                // 当前线程重入内部锁。Claim 此时必须尚未持有 SQLite 写锁,否则这里会超时。
                using var bare = OpenClient();
                bare.CodeFirst.InitTables<SysWorkerLease>();
            }
        }
        catch (Exception ex) { errors.Add(ex); }
        finally
        {
            WorkerIdAssignment? completedAssignment = null;
            if (started)
            {
                if (claimThread.Join(TimeSpan.FromSeconds(30)))
                {
                    while (workerErrors.TryDequeue(out var workerError)) errors.Add(workerError);
                    completedAssignment = Interlocked.Exchange(ref assignment, null);
                }
                else
                {
                    retainRoot = true;
                    Interlocked.Exchange(ref abandoned, 1);
                    completedAssignment = Interlocked.Exchange(ref assignment, null);
                    errors.Add(new TimeoutException(
                        $"Claim 线程在 CodeFirst 锁释放后 30 秒内未结束;SQLite 现场已保留:{root}"));
                }
            }

            try
            {
                if (errors.Count == 0)
                {
                    Assert.NotNull(completedAssignment);
                    Assert.InRange(completedAssignment.WorkerId, 0, SnowflakeIdGenerator.MaxWorkerId);
                }
            }
            catch (Exception ex) { errors.Add(ex); }
            finally
            {
                try { completedAssignment?.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
                // Join 超时时后台线程可能仍在使用数据库,保留现场;正常路径才清理独立目录。
                if (!retainRoot)
                {
                    try { Directory.Delete(root, recursive: true); }
                    catch (Exception ex) { errors.Add(ex); }
                }
            }
        }

        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("SQLite 初始化与 Claim/清理出现多个错误", errors);
    }

    private static ulong CalibrateCodeFirstGate(
        CodeFirstContentionListener listener,
        string correlationId,
        object codeFirstGate)
    {
        var errors = new List<Exception>();
        Exception? probeError = null;
        var probe = new Thread(() =>
        {
            try
            {
                CodeFirstTestEvents.Log.ThreadRole(correlationId, "probe");
                lock (codeFirstGate) { }
            }
            catch (Exception ex) { probeError = ex; }
        }) { IsBackground = true };
        var started = false;
        ulong lockIdentity = 0;
        try
        {
            lock (codeFirstGate)
            {
                CodeFirstTestEvents.Log.ThreadRole(correlationId, "probe-owner");
                Assert.True(listener.TryWaitForRole("probe-owner", TimeSpan.FromSeconds(10), out var ownerThreadId),
                    "未收到 CodeFirst 锁持有线程的运行时线程标记");
                probe.Start();
                started = true;
                Assert.True(listener.TryWaitForRole("probe", TimeSpan.FromSeconds(10), out var probeThreadId),
                    "未收到 CodeFirst 锁校准线程的运行时线程标记");
                Assert.True(listener.TryWaitForContention(
                        probeThreadId, ownerThreadId, expectedLockIdentity: null, TimeSpan.FromSeconds(10), out lockIdentity),
                    $"无法从运行时争用事件校准真实 CodeFirst 锁身份;{listener.Describe()}");
            }
        }
        catch (Exception ex) { errors.Add(ex); }
        finally
        {
            if (started && !probe.Join(TimeSpan.FromSeconds(10)))
                errors.Add(new TimeoutException("CodeFirst 锁校准线程在锁释放后 10 秒内未结束"));
            if (probeError is not null) errors.Add(probeError);
        }

        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("CodeFirst 锁身份校准出现多个错误", errors);
        return lockIdentity;
    }

    [EventSource(Name = "TenonAdmin-Tests-CodeFirstLock")]
    private sealed class CodeFirstTestEvents : EventSource
    {
        public static readonly CodeFirstTestEvents Log = new();

        [Event(1, Level = EventLevel.Informational)]
        public void ThreadRole(string correlationId, string role) => WriteEvent(1, correlationId, role);
    }

    private sealed class CodeFirstContentionListener : EventListener
    {
        private const EventKeywords ContentionKeyword = (EventKeywords)0x4000;
        private readonly string? _correlationId;
        private readonly ConcurrentDictionary<string, long>? _roleThreadIds;
        private readonly ConcurrentQueue<RuntimeContention>? _contentions;

        public CodeFirstContentionListener(string correlationId)
        {
            _correlationId = correlationId;
            _roleThreadIds = new ConcurrentDictionary<string, long>();
            _contentions = new ConcurrentQueue<RuntimeContention>();
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(eventSource, EventLevel.Verbose, ContentionKeyword);
            else if (eventSource.Name == "TenonAdmin-Tests-CodeFirstLock")
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "TenonAdmin-Tests-CodeFirstLock" &&
                eventData.EventId == 1 && eventData.Payload is { Count: >= 2 } &&
                string.Equals(eventData.Payload[0] as string, _correlationId, StringComparison.Ordinal) &&
                eventData.Payload[1] is string role)
            {
                _roleThreadIds?.TryAdd(role, eventData.OSThreadId);
                return;
            }

            if (eventData.EventName?.StartsWith("ContentionStart", StringComparison.Ordinal) != true)
                return;
            if (!TryGetPayload(eventData, "LockID", out var lockId) ||
                !TryGetPayload(eventData, "LockOwnerThreadID", out var ownerThreadId))
                return;

            try
            {
                _contentions?.Enqueue(new RuntimeContention(
                    eventData.OSThreadId,
                    ConvertLockIdentity(lockId!),
                    Convert.ToInt64(ownerThreadId)));
            }
            catch (Exception)
            {
                // 运行时事件格式变化时不在回调线程抛错;等待方会按时限给出明确失败。
            }
        }

        public bool TryWaitForRole(string role, TimeSpan timeout, out long threadId)
        {
            if (_roleThreadIds!.TryGetValue(role, out threadId)) return true;
            SpinWait.SpinUntil(() => _roleThreadIds.ContainsKey(role), timeout);
            return _roleThreadIds.TryGetValue(role, out threadId);
        }

        public bool TryWaitForContention(
            long waiterThreadId,
            long ownerThreadId,
            ulong? expectedLockIdentity,
            TimeSpan timeout,
            out ulong lockIdentity)
        {
            ulong matched = 0;
            var found = WaitUntil(() =>
            {
                foreach (var contention in _contentions!)
                {
                    if (contention.WaiterThreadId != waiterThreadId ||
                        contention.OwnerThreadId != ownerThreadId ||
                        expectedLockIdentity is not null && contention.LockIdentity != expectedLockIdentity.Value)
                        continue;
                    matched = contention.LockIdentity;
                    return true;
                }
                return false;
            }, timeout);
            lockIdentity = matched;
            return found;
        }

        public bool TryWaitForContention(
            long waiterThreadId,
            long ownerThreadId,
            ulong expectedLockIdentity,
            TimeSpan timeout) =>
            TryWaitForContention(waiterThreadId, ownerThreadId, expectedLockIdentity, timeout, out _);

        public string Describe()
        {
            var roles = string.Join(",", _roleThreadIds!.OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
            var contentions = string.Join(",", _contentions!.Take(20)
                .Select(item => $"waiter:{item.WaiterThreadId}/owner:{item.OwnerThreadId}/lock:{item.LockIdentity}"));
            return $"roles=[{roles}];contentions=[{contentions}]";
        }

        private bool WaitUntil(Func<bool> condition, TimeSpan timeout)
            => condition() || SpinWait.SpinUntil(condition, timeout);

        private static bool TryGetPayload(EventWrittenEventArgs eventData, string name, out object? value)
        {
            value = null;
            if (eventData.PayloadNames is null || eventData.Payload is null) return false;
            for (var index = 0; index < eventData.PayloadNames.Count; index++)
            {
                if (!string.Equals(eventData.PayloadNames[index], name, StringComparison.Ordinal)) continue;
                value = eventData.Payload[index];
                return value is not null;
            }
            return false;
        }

        private static ulong ConvertLockIdentity(object value) => value switch
        {
            IntPtr pointer => unchecked((ulong)pointer.ToInt64()),
            UIntPtr pointer => pointer.ToUInt64(),
            _ => Convert.ToUInt64(value),
        };

        private sealed record RuntimeContention(long WaiterThreadId, ulong LockIdentity, long OwnerThreadId);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerIdTableInitSqliteConcurrencyCollection
{
    public const string Name = "WorkerIdTableInitSqliteConcurrency";
}
