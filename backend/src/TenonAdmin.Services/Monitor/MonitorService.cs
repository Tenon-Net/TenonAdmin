using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IMonitorService"/> 默认实现:纯 BCL(<c>Environment</c>/<c>Process</c>/<c>GC</c>/<c>DriveInfo</c>/
/// <c>RuntimeInformation</c>),零依赖。进程 CPU% 用两次 <c>TotalProcessorTime</c> 采样估算。
/// </summary>
public class MonitorService(TimeProvider time, ILogger<MonitorService> logger) : IMonitorService
{
    /// <summary>CPU 采样间隔;越长越平滑、响应越慢,500ms 是页面刷新可接受的折中。</summary>
    protected virtual TimeSpan CpuSampleWindow => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public virtual async Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        using var proc = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();

        return new ServerInfoOutput
        {
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,

            ProcessUptimeSeconds = (long)(time.GetUtcNow() - proc.StartTime.ToUniversalTime()).TotalSeconds,
            ProcessCpuPercent = await SampleCpuPercentAsync(proc, cancellationToken),
            ProcessWorkingSetBytes = proc.WorkingSet64,
            GcHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            TotalAvailableMemoryBytes = gc.TotalAvailableMemoryBytes,
            ThreadCount = proc.Threads.Count,

            Disks = ReadDisks(),
        };
    }

    /// <summary>两次 <c>TotalProcessorTime</c> 采样估进程 CPU%,对全部逻辑核归一(0–100)。</summary>
    protected virtual async Task<double> SampleCpuPercentAsync(Process proc, CancellationToken cancellationToken)
    {
        var startCpu = proc.TotalProcessorTime;
        var startStamp = time.GetTimestamp();
        await Task.Delay(CpuSampleWindow, time, cancellationToken);
        proc.Refresh();
        var cpuUsed = proc.TotalProcessorTime - startCpu;
        var wall = time.GetElapsedTime(startStamp);
        var denom = wall.TotalMilliseconds * Environment.ProcessorCount;
        if (denom <= 0) return 0;
        return Math.Round(Math.Clamp(cpuUsed.TotalMilliseconds / denom * 100, 0, 100), 2);
    }

    /// <summary>就绪分区的容量。逐分区 try/catch:某个分区取容量失败(权限/网络盘掉线)不掀翻整张快照。</summary>
    protected virtual IReadOnlyList<DiskInfo> ReadDisks()
    {
        var disks = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                disks.Add(new DiskInfo { Name = drive.Name, TotalBytes = drive.TotalSize, FreeBytes = drive.AvailableFreeSpace });
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "读取磁盘 {Name} 容量失败,跳过", drive.Name);
            }
        }
        return disks;
    }
}
