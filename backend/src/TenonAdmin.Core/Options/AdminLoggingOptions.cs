namespace TenonAdmin.Core;

/// <summary>
/// 日志配置(对应 <c>TenonAdmin:Logging</c> 节)。
/// <para>注意与「日志审计」(<c>sys_op_log</c>/<c>sys_login_log</c>,谁在什么时候改了什么)区分:
/// 这里管的是<b>诊断日志</b>——<c>ILogger</c> 打出来的异常堆栈、启动告警、限流/缓存诊断。
/// 两套东西各走各的,审计日志不会再往文件里写一份。</para>
/// </summary>
public class AdminLoggingOptions
{
    /// <summary>文件日志(默认关闭,见 <see cref="AdminFileLogOptions"/>)</summary>
    public AdminFileLogOptions File { get; set; } = new();
}

/// <summary>
/// 文件日志配置(对应 <c>TenonAdmin:Logging:File</c> 节)。
/// <para>落盘形状:<c>{Path}/{级别}/{yyyyMMdd}.log</c>,如 <c>logs/error/20260714.log</c>。
/// 每条日志<b>只落一个文件</b>(不另写一份全量)。<c>Critical</c> 并入 <c>error/</c> 目录。</para>
/// <para><b>日志级别过滤不在这里配</b>——provider 挂了 <c>[ProviderAlias("File")]</c>,
/// 直接用微软标准的 <c>Logging:File:LogLevel:Default</c>(与 Console provider 同一套),不重复造轮子。</para>
/// <para><b>本 provider 是加法,不排他</b>:开了它,框架默认的 Console 输出照旧。
/// 要 JSON/结构化/远端 sink 的消费者在宿主里自行 <c>AddSerilog()</c> 即可,两者并存。</para>
/// </summary>
public class AdminFileLogOptions
{
    /// <summary>
    /// 是否写盘。<b>默认关</b>:零配置宿主(以及容器/k8s 部署)靠 stdout 采集就够了,凭空长出一个 logs 目录
    /// 反而是意外。自建机、systemd 直跑、要留一份人能 <c>grep</c> 的现场时才打开。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 日志根目录。相对路径<b>按 ContentRoot 解析</b>(与 SQLite 库文件、上传根同一基准,不随进程 CWD 漂移);绝对路径原样使用。
    /// <para><b>红线:不能落在 <c>wwwroot</c> 下。</b>宿主一旦 <c>UseStaticFiles()</c> 托管前端产物,
    /// 静态中间件就会把整个日志目录匿名直出——异常堆栈、请求参数、内部路径全在里面。装配时会检测并直接抛。</para>
    /// </summary>
    public string Path { get; set; } = "logs";

    /// <summary>
    /// 保留天数:每天滚动到新文件时,顺手删掉本级别目录下早于 <c>今天 - RetainDays</c> 的日志。
    /// <para><c>0</c> = 不清理(日志永久堆积,自行接管轮转)。</para>
    /// </summary>
    public int RetainDays { get; set; } = 14;

    /// <summary>
    /// 单文件大小上限(MB):超了续写分卷 <c>20260714-2.log</c>、<c>-3.log</c>……序号单调递增,不回绕覆盖。
    /// <para>这是<b>爆盘保护</b>,不是归档策略:误开 Debug 级别、或被刷 401 的一天里,单个文件能涨到把磁盘撑满,
    /// 届时数据库也一起写不进去。<c>0</c> = 不限(只靠 <see cref="RetainDays"/> 兜底)。</para>
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 100;
}
