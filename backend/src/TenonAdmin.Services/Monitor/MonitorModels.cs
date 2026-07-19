namespace TenonAdmin.Services;

/// <summary>
/// 服务器监控快照(设计 §4 运维)。只报<b>进程与主机基础面</b>——系统级全量指标(APM/链路/告警)
/// 留给消费方的观测栈(OpenTelemetry 等可选包方向),内核不越位。
/// </summary>
public record ServerInfoOutput
{
    /// <summary>主机名</summary>
    public string MachineName { get; init; } = "";
    /// <summary>操作系统描述(如 <c>Microsoft Windows 10.0.20348</c>)</summary>
    public string OsDescription { get; init; } = "";
    /// <summary>运行时框架描述(如 <c>.NET 10.0.0</c>)</summary>
    public string FrameworkDescription { get; init; } = "";
    /// <summary>进程架构(如 <c>X64</c> / <c>Arm64</c>)</summary>
    public string ProcessArchitecture { get; init; } = "";
    /// <summary>逻辑处理器数</summary>
    public int ProcessorCount { get; init; }

    /// <summary>进程已运行秒数</summary>
    public long ProcessUptimeSeconds { get; init; }
    /// <summary>进程 CPU 占用百分比(0–100,对全部逻辑核归一;两次采样估算)</summary>
    public double ProcessCpuPercent { get; init; }
    /// <summary>进程工作集(物理内存占用,字节)</summary>
    public long ProcessWorkingSetBytes { get; init; }
    /// <summary>托管堆已用字节(GC)</summary>
    public long GcHeapBytes { get; init; }
    /// <summary>GC 可用内存上界(字节;进程/容器 cgroup 限额视角)</summary>
    public long TotalAvailableMemoryBytes { get; init; }
    /// <summary>进程线程数</summary>
    public int ThreadCount { get; init; }

    /// <summary>磁盘分区列表(就绪分区)</summary>
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
}

/// <summary>磁盘分区容量。</summary>
public record DiskInfo
{
    /// <summary>分区名/挂载点(如 <c>C:\</c> 或 <c>/</c>)</summary>
    public string Name { get; init; } = "";
    /// <summary>总容量(字节)</summary>
    public long TotalBytes { get; init; }
    /// <summary>可用容量(字节)</summary>
    public long FreeBytes { get; init; }
}
