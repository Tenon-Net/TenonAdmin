namespace TenonAdmin.Core;

/// <summary>
/// 定时任务配置(对应 <c>TenonAdmin:Jobs</c> 节,docs/scheduling-ledger.md §6)。
/// <para>
/// <b>没有第二个总开关</b>:整模块下线走既有 <c>Api:DisabledModules=["Job"]</c>;
/// <see cref="SchedulerEnabled"/> 只回答"本副本要不要参与调度"。
/// </para>
/// </summary>
public class AdminJobsOptions
{
    /// <summary>本副本是否参与选主/调度;false = 纯 API 副本(执行一次/查询/编辑照常)</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>节点名(集群内唯一);空 → <c>{MachineName}#{WorkerId}</c></summary>
    public string? NodeName { get; set; }

    /// <summary>心跳间隔(秒):节点行 upsert + 主节点续租/备节点夺租的节拍;必须为正数(绑定时校验)</summary>
    public int HeartbeatSeconds { get; set; } = 10;

    /// <summary>选主租约时长(秒);必须 > 2×<see cref="HeartbeatSeconds"/>(绑定时校验),容一次 GC 停顿或一次 DB 抖动</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>任务缓存全量重载间隔(秒)——跨副本配置收敛上限(事件总线仅进程内,别的副本改任务靠它兜底);必须为正数</summary>
    public int ReloadSeconds { get; set; } = 30;

    /// <summary>到期时刻迟到超过此秒数才算「错过」,走任务行上的 MisfireStrategy;以内正常触发;必须为正数</summary>
    public int MisfireThresholdSeconds { get; set; } = 60;

    /// <summary>在飞执行上限(线程池保护的兜底;达到后本拍不再领取,下拍再来);必须为正数</summary>
    public int MaxConcurrentRuns { get; set; } = 8;

    /// <summary>跨节点终止旗标(KillRequested)的轮询间隔(秒)</summary>
    public int KillPollSeconds { get; set; } = 5;

    /// <summary>HTTP 任务配置(SSRF 围栏与响应日志)</summary>
    public AdminJobsHttpOptions Http { get; set; } = new();

    /// <summary>SQL 任务配置(默认关)</summary>
    public AdminJobsSqlOptions Sql { get; set; } = new();
}

/// <summary>HTTP 任务配置(docs/scheduling-ledger.md §7.1)</summary>
public class AdminJobsHttpOptions
{
    /// <summary>目标主机白名单;null/空 = 不限(要收紧时用)</summary>
    public string[]? AllowedHosts { get; set; }

    /// <summary>
    /// 目标 IP 黑名单(CIDR)。默认封云元数据段的 IPv4/IPv6 两种形态,<b>不封内网</b>——调度器打内网服务是主用途
    /// (RFC1918 与 ULA <c>fc00::/7</c> 照旧放行)。<c>fe80::/10</c> 是 <c>169.254.0.0/16</c> 的 IPv6 孪生,一并默认封。
    /// 用 NAT64 的环境请自行追加 <c>64:ff9b::/96</c>。
    /// <para>解析后的 IP 在 <c>ConnectCallback</c> 里复检(防 DNS rebinding:校验时是公网、执行时解析成内网的把戏)。
    /// 条目在启动绑定期校验,写错即抛——静默失效等于围栏无声关闭。</para>
    /// </summary>
    public string[] BlockedCidrs { get; set; } = ["169.254.0.0/16", "fd00:ec2::/32", "fe80::/10"];

    /// <summary>HTTP 响应体落执行记录的截断长度(字节);不能为负数(绑定时校验)</summary>
    public int MaxResponseLogBytes { get; set; } = 4096;
}

/// <summary>SQL 任务配置(docs/scheduling-ledger.md §7.2)</summary>
public class AdminJobsSqlOptions
{
    /// <summary>SQL 任务总闸,默认关。<b>开启即承认:任务编辑权限 = DBA 权限</b>。</summary>
    public bool Enabled { get; set; }
}
