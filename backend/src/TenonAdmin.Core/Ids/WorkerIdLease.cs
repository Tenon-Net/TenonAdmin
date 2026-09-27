using System.Text;

namespace TenonAdmin.Core;

/// <summary>
/// 首选目录建失败时通知组合根（Core 不引 <c>ILogger</c>）。
/// </summary>
/// <param name="preferred">本来要用的机器级目录</param>
/// <param name="fallback">实际落到的后备目录（通常在临时目录下，可能被清理）</param>
public delegate void WorkerIdLockDirFallback(string preferred, string fallback);

/// <summary>
/// 雪花机器号的文件锁租约：启动时依次独占打开 <c>worker-00.lock … worker-63.lock</c>，
/// 第一个锁得住的文件名就是本进程的机器号，句柄一直持有到进程退出。
/// <para>同一台机器上的多个进程（IIS 应用池重叠回收、Web 园、同机多份部署）读同一份配置，
/// 未显式配 <c>TenonAdmin:Id:WorkerId</c> 时都会落到 0，同毫秒各发一个号就撞主键。
/// 抢锁后旧进程占着 0，新进程自动落到 1。</para>
/// <para>文件锁只在同一台机器内互斥。跨机器/跨容器仍要显式配 <c>TenonAdmin:Id:WorkerId</c>，
/// 并靠 <c>sys_worker_lease</c> 拦住同号。</para>
/// <para>释放只关句柄、不删文件：删了之后 Linux 上另一进程可能在同一路径新建 inode，
/// 两个进程各锁各的却都以为占着同一个号。</para>
/// </summary>
public sealed class WorkerIdLease : IDisposable
{
    /// <summary>抢到的机器号（0–63）。</summary>
    public long WorkerId { get; }

    /// <summary>持有的锁文件路径，排查「哪个进程占了哪个号」时看它。</summary>
    public string LockFile { get; }

    private FileStream? _handle;

    private WorkerIdLease(long workerId, string lockFile, FileStream handle)
    {
        WorkerId = workerId;
        LockFile = lockFile;
        _handle = handle;
    }

    /// <summary>
    /// 首选锁目录：Windows 为 <c>%ProgramData%\TenonAdmin\workerid</c>，其它系统为
    /// <c>/var/lock/tenonadmin/workerid</c>。必须是机器级路径——跟着程序目录走，
    /// 同机两份部署会各自抢到 0 号，等于没锁。
    /// </summary>
    public static string PreferredLockDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrWhiteSpace(data))
                    return Path.Combine(data, "TenonAdmin", "workerid");
            }
            else
            {
                return "/var/lock/tenonadmin/workerid";
            }

            return FallbackLockDir;
        }
    }

    /// <summary>
    /// 首选目录写不进去时的后备：<c>{GetTempPath()}/tenonadmin/workerid</c>。
    /// 临时目录可能被清理删掉锁文件，同路径会再长出一个新 inode，同机互斥失效。
    /// </summary>
    public static string FallbackLockDir =>
        Path.Combine(Path.GetTempPath(), "tenonadmin", "workerid");

    /// <summary>抢一个空闲槽位。目录不可用或 64 个槽位全被占满时抛异常，拒绝启动，不发可能重复的号。</summary>
    /// <param name="lockDir">锁目录；空则按首选 → 后备解析。</param>
    /// <param name="onFallback">落到后备目录时回调（组合根用来打 Warning）。</param>
    public static WorkerIdLease Acquire(string? lockDir = null, WorkerIdLockDirFallback? onFallback = null)
    {
        var dir = ResolveLockDir(lockDir, onFallback);

        for (long id = 0; id <= SnowflakeIdGenerator.MaxWorkerId; id++)
        {
            var lease = TryAcquireInDir(id, dir);
            if (lease is not null)
                return lease;
        }

        throw new InvalidOperationException(
            $"机器号 0–{SnowflakeIdGenerator.MaxWorkerId} 已被占满（目录 {dir}），拒绝发号以避免重复。" +
            "请检查本机是否有进程未正常退出，或为各实例显式配置不同的 TenonAdmin:Id:WorkerId。");
    }

    /// <summary>只抢指定槽。被占则返回 null，不扫其它号。</summary>
    public static WorkerIdLease? TryAcquire(long workerId, string? lockDir = null, WorkerIdLockDirFallback? onFallback = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerId, SnowflakeIdGenerator.MaxWorkerId);
        var dir = ResolveLockDir(lockDir, onFallback);
        return TryAcquireInDir(workerId, dir);
    }

    private static WorkerIdLease? TryAcquireInDir(long id, string dir)
    {
        var file = Path.Combine(dir, $"worker-{id:D2}.lock");
        FileStream handle;
        try
        {
            // FileShare.None：Windows 走共享冲突，Unix 走 flock(LOCK_EX)，两边都按打开句柄互斥
            handle = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            handle.SetLength(0);
            handle.Write(Encoding.UTF8.GetBytes(
                $"pid={Environment.ProcessId} machine={Environment.MachineName} since={DateTimeOffset.Now:O}"));
            handle.Flush();
        }
        catch (IOException)
        {
            /* 内容只供排查，写不进不影响互斥 */
        }

        return new WorkerIdLease(id, file, handle);
    }

    /// <summary>解析并确保锁目录可写。显式指定的目录失败不退到后备——那是运维选的路径。</summary>
    public static string ResolveLockDir(string? lockDir, WorkerIdLockDirFallback? onFallback = null)
    {
        if (!string.IsNullOrWhiteSpace(lockDir))
        {
            var specified = Path.GetFullPath(lockDir);
            try
            {
                Directory.CreateDirectory(specified);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"机器号锁目录 {specified} 不可用，无法保证雪花 Id 不重复，拒绝启动。" +
                    "请给运行账户该目录的写权限，或用 TenonAdmin:Id:WorkerIdLockDir 指定一个可写的机器级目录。",
                    ex);
            }

            return specified;
        }

        var preferred = PreferredLockDir;
        if (TryCreateDirectory(preferred))
            return preferred;

        var fallback = FallbackLockDir;
        if (!string.Equals(preferred, fallback, StringComparison.Ordinal))
            onFallback?.Invoke(preferred, fallback);

        try
        {
            Directory.CreateDirectory(fallback);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"机器号锁目录 {preferred} 与后备 {fallback} 都不可用，无法保证雪花 Id 不重复，拒绝启动。" +
                "请给运行账户写权限，或用 TenonAdmin:Id:WorkerIdLockDir 指定一个可写的机器级目录。",
                ex);
        }

        return fallback;
    }

    /// <summary>释放槽位：只关句柄，不删文件。</summary>
    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();

    private static bool TryCreateDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
