namespace TenonAdmin.Integration;

/// <summary>
/// 第三方接入模块配置(对应 <c>TenonAdmin:Integration</c> 节)。缺省即默认值,零配置可跑;
/// 非法值在 <see cref="IntegrationSetup.AddTenonAdminIntegration"/> 里 fail-fast(见 <see cref="IntegrationOptionsValidation"/>)。
/// </summary>
public class IntegrationOptions
{
    /// <summary>接入凭据生命周期:默认有效期、每应用有效凭据上限、轮换并存窗口。</summary>
    public IntegrationCredentialOptions Credentials { get; set; } = new();

    /// <summary>开放接口:文档版本、按应用限流与认证失败限流。</summary>
    public IntegrationOpenApiOptions OpenApi { get; set; } = new();

    /// <summary>普通出站调用:第三方目标、默认超时与响应体上限。</summary>
    public IntegrationOutboundOptions Outbound { get; set; } = new();

    /// <summary>可靠投递:次数与时间上限、租约、退避、异步受理确认与扫描批量。</summary>
    public IntegrationDeliveryOptions Delivery { get; set; } = new();

    /// <summary>调用记录与投递记录的保留期。</summary>
    public IntegrationRetentionOptions Retention { get; set; } = new();
}

/// <summary>开放接口配置(对应 <c>TenonAdmin:Integration:OpenApi</c>)。</summary>
public class IntegrationOpenApiOptions
{
    /// <summary>
    /// 开放接口版本(每个版本一份独立 OpenAPI 文档 <c>open-{version}</c>,路由 <c>/api/open/{version}/...</c>)。默认只有 <c>v1</c>;
    /// 新增破坏性版本时在此追加(如 <c>["v1","v2"]</c>)。
    /// </summary>
    public string[] Versions { get; set; } = ["v1"];

    /// <summary>应用未单独设置时的每分钟调用上限;0 表示不限。默认 600。</summary>
    public int DefaultRateLimitPerMinute { get; set; } = 600;

    /// <summary>
    /// 每分钟允许的认证失败次数;超过后在查库前直接 429(保护未认证入口)。格式正确的凭据按(客户端 IP, 凭据标识)计数,
    /// 缺失或格式不对的按客户端 IP 计数——刷格式错误的请求不会连累同一 IP 的合法凭据。0 表示不限。默认 30。
    /// 计数走 <c>ICacheProvider</c>:装 Redis 即全集群共享,否则每副本独立计数。
    /// </summary>
    public int AuthFailuresPerMinutePerIp { get; set; } = 30;
}

/// <summary>保留期配置(对应 <c>TenonAdmin:Integration:Retention</c>)。天数为 0 表示永久保留。</summary>
public class IntegrationRetentionOptions
{
    /// <summary>开放调用记录保留天数(按记录时间)。默认 30。</summary>
    public int InboundLogDays { get; set; } = 30;

    /// <summary>出站调用记录保留天数(按记录时间)。默认 30。</summary>
    public int OutboundLogDays { get; set; } = 30;

    /// <summary>
    /// 已完结投递记录的保留天数:只清理成功与已取消的记录(按完成时刻),连同其尝试记录;
    /// 待处理、处理中、待确认、待核对、耗尽与失败的记录<b>永不自动删除</b>。默认 90。
    /// </summary>
    public int DeliveryDays { get; set; } = 90;

    /// <summary>每批删除的最大行数(清理分批进行,避免长事务锁表)。默认 1000。</summary>
    public int BatchSize { get; set; } = 1000;
}

/// <summary>可靠投递配置(对应 <c>TenonAdmin:Integration:Delivery</c>,实现契约 §8)。</summary>
public class IntegrationDeliveryOptions
{
    /// <summary>
    /// 租约相对单次调用的安全余量(秒):租约须长于最长调用超时加这个余量;为投递发起的单次调用超时也被截断到租约减它,
    /// 保证租约不会在调用结束前到期(否则中断恢复会与仍在进行的调用重叠)。
    /// </summary>
    public const int LeaseSafetyMarginSeconds = 30;

    /// <summary>每条投递的发送次数上限(自最近一次预算重置起计,入队时可单独覆盖)。默认 8。</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>自动处理时限(小时):入队后超过即转为耗尽,等人工处理。默认 24。</summary>
    public int MaxAgeHours { get; set; } = 24;

    /// <summary>领取租约(秒);须大于任一出站目标的调用超时 + <see cref="LeaseSafetyMarginSeconds"/> 秒。默认 120。</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>退避基数(秒):第 n 次失败后等待 基数 × 2^(n-1)。默认 30。</summary>
    public int BackoffBaseSeconds { get; set; } = 30;

    /// <summary>退避上限(秒)。默认 3600。</summary>
    public int BackoffMaxSeconds { get; set; } = 3600;

    /// <summary>异步受理后轮询确认的间隔(秒,适配器可查询时)。默认 60。</summary>
    public int ConfirmPollSeconds { get; set; } = 60;

    /// <summary>异步受理等待最终确认的时限(小时),超时转人工核对。默认 72。</summary>
    public int ConfirmDeadlineHours { get; set; } = 72;

    /// <summary>每次扫描最多处理的记录数。默认 20。</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>单条载荷上限(字节,UTF-8)。默认 256 KB。</summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;
}

/// <summary>接入凭据(API Key)生命周期配置(对应 <c>TenonAdmin:Integration:Credentials</c>)。</summary>
public class IntegrationCredentialOptions
{
    /// <summary>未指定到期时间时的默认有效期(天);0 表示不过期。默认 365。</summary>
    public int DefaultLifetimeDays { get; set; } = 365;

    /// <summary>可设置的最长有效期(天),防止误填远期到期时间。默认 3650。</summary>
    public int MaxLifetimeDays { get; set; } = 3650;

    /// <summary>每个接入应用同时有效(未撤销、未过期)的凭据上限;轮换期间允许临时多出一把。默认 5。</summary>
    public int MaxActivePerApp { get; set; } = 5;

    /// <summary>轮换时旧凭据的默认并存窗口(小时);窗口结束旧凭据过期。默认 24。</summary>
    public int DefaultRotationOverlapHours { get; set; } = 24;

    /// <summary>轮换并存窗口上限(小时)。默认 720(30 天)。</summary>
    public int MaxRotationOverlapHours { get; set; } = 720;

    /// <summary>凭据最近使用时间的回写间隔(秒):同一凭据在间隔内只写一次库,避免每请求一次 UPDATE。默认 300。</summary>
    public int LastUsedWriteIntervalSeconds { get; set; } = 300;
}

/// <summary>启动期配置校验:非法值直接抛 <see cref="InvalidOperationException"/>,不带病启动。</summary>
public static class IntegrationOptionsValidation
{
    /// <summary>校验整份配置;任一项非法即抛出,消息指明配置键。</summary>
    public static void Validate(IntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var c = options.Credentials ?? throw new InvalidOperationException("TenonAdmin:Integration:Credentials 不能为空。");
        Require(c.DefaultLifetimeDays >= 0, "Credentials:DefaultLifetimeDays 不能为负数(0 表示不过期)。");
        Require(c.MaxLifetimeDays > 0, "Credentials:MaxLifetimeDays 必须为正数。");
        Require(c.DefaultLifetimeDays <= c.MaxLifetimeDays, "Credentials:DefaultLifetimeDays 不能超过 MaxLifetimeDays。");
        Require(c.MaxActivePerApp > 0, "Credentials:MaxActivePerApp 必须为正数。");
        Require(c.MaxRotationOverlapHours >= 0, "Credentials:MaxRotationOverlapHours 不能为负数。");
        Require(c.DefaultRotationOverlapHours >= 0 && c.DefaultRotationOverlapHours <= c.MaxRotationOverlapHours,
            "Credentials:DefaultRotationOverlapHours 须在 0..MaxRotationOverlapHours 之间。");
        Require(c.LastUsedWriteIntervalSeconds >= 0, "Credentials:LastUsedWriteIntervalSeconds 不能为负数。");

        var o = options.OpenApi ?? throw new InvalidOperationException("TenonAdmin:Integration:OpenApi 不能为空。");
        Require(o.Versions is { Length: > 0 }, "OpenApi:Versions 至少包含一个版本。");
        Require(o.Versions.All(v => v is not null && System.Text.RegularExpressions.Regex.IsMatch(v, "^v[0-9]+$")),
            "OpenApi:Versions 的每一项必须形如 v1、v2。");
        Require(o.Versions.Distinct(StringComparer.OrdinalIgnoreCase).Count() == o.Versions.Length, "OpenApi:Versions 不能重复。");
        Require(o.DefaultRateLimitPerMinute >= 0, "OpenApi:DefaultRateLimitPerMinute 不能为负数(0 表示不限)。");
        Require(o.AuthFailuresPerMinutePerIp >= 0, "OpenApi:AuthFailuresPerMinutePerIp 不能为负数(0 表示不限)。");

        var outbound = options.Outbound ?? throw new InvalidOperationException("TenonAdmin:Integration:Outbound 不能为空。");
        OutboundOptionsValidation.Validate(outbound);

        var d = options.Delivery ?? throw new InvalidOperationException("TenonAdmin:Integration:Delivery 不能为空。");
        Require(d.MaxAttempts is >= 1 and <= 100, "Delivery:MaxAttempts 须在 1..100 之间。");
        Require(d.MaxAgeHours is >= 1 and <= 720, "Delivery:MaxAgeHours 须在 1..720 之间。");
        Require(d.BackoffBaseSeconds is >= 1 and <= 3600, "Delivery:BackoffBaseSeconds 须在 1..3600 之间。");
        Require(d.BackoffMaxSeconds >= d.BackoffBaseSeconds && d.BackoffMaxSeconds <= 86_400, "Delivery:BackoffMaxSeconds 须在 BackoffBaseSeconds..86400 之间。");
        Require(d.ConfirmPollSeconds is >= 5 and <= 3600, "Delivery:ConfirmPollSeconds 须在 5..3600 之间。");
        Require(d.ConfirmDeadlineHours is >= 1 and <= 720, "Delivery:ConfirmDeadlineHours 须在 1..720 之间。");
        Require(d.BatchSize is >= 1 and <= 500, "Delivery:BatchSize 须在 1..500 之间。");
        Require(d.MaxPayloadBytes is >= 1024 and <= 16 * 1024 * 1024, "Delivery:MaxPayloadBytes 须在 1024..16777216 之间。");
        // 租约必须覆盖一次完整调用:租约先到期会让恢复与仍在进行的调用重叠
        var longestTimeout = Math.Max(outbound.DefaultTimeoutSeconds,
            outbound.Targets.Values.Select(t => t.TimeoutSeconds ?? outbound.DefaultTimeoutSeconds).DefaultIfEmpty(0).Max());
        Require(d.LeaseSeconds <= 3600 && d.LeaseSeconds > longestTimeout + IntegrationDeliveryOptions.LeaseSafetyMarginSeconds,
            $"Delivery:LeaseSeconds 须大于最长出站调用超时 + {IntegrationDeliveryOptions.LeaseSafetyMarginSeconds} 秒(当前最长超时 {longestTimeout} 秒),且不超过 3600。");

        var r = options.Retention ?? throw new InvalidOperationException("TenonAdmin:Integration:Retention 不能为空。");
        Require(r.InboundLogDays >= 0, "Retention:InboundLogDays 不能为负数(0 表示永久保留)。");
        Require(r.OutboundLogDays >= 0, "Retention:OutboundLogDays 不能为负数(0 表示永久保留)。");
        Require(r.DeliveryDays >= 0, "Retention:DeliveryDays 不能为负数(0 表示永久保留)。");
        Require(r.BatchSize > 0, "Retention:BatchSize 必须为正数。");
    }

    /// <summary>条件不成立即抛出,消息带 <c>TenonAdmin:Integration:</c> 前缀(出站配置校验共用)。</summary>
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TenonAdmin:Integration:" + message);
    }
}
