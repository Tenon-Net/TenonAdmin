namespace TenonAdmin.Core;

/// <summary>
/// Level3 一期预检检查项状态字面量(机器可读 JSON 直接输出 <c>pass|fail|warn</c>)。
/// </summary>
public static class Level3CheckStatus
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Warn = "warn";
}

/// <summary>
/// 单条预检项(机器可读;不含密钥/连接串明文)。
/// </summary>
/// <param name="Id">稳定检查项 Id(如 <c>redis_tls</c>),CI/日志可定位</param>
/// <param name="Name">人类可读短名</param>
/// <param name="Status"><see cref="Level3CheckStatus"/> 常量:pass|fail|warn</param>
/// <param name="Message">现状说明(脱敏)</param>
/// <param name="Remediation">修复建议</param>
/// <param name="Critical">
/// Level3 下是否为启动/readiness 关键项;true 时 fail → 拒绝启动或 /health/ready Unhealthy。
/// </param>
public sealed record Level3PrecheckItem(
    string Id,
    string Name,
    string Status,
    string Message,
    string Remediation,
    bool Critical = false);

/// <summary>
/// 本期未实现的 Level3 强制能力条目(明示能力边界,避免把一期报告读成完整三级基线)。
/// </summary>
/// <param name="Id">稳定标识</param>
/// <param name="Name">短名</param>
/// <param name="Phase">计划期次(2|3)</param>
/// <param name="Description">缺口说明</param>
public sealed record Level3UnimplementedMandate(
    string Id,
    string Name,
    int Phase,
    string Description);

/// <summary>
/// Level3 一期预检结构化结果。可被宿主启动闸门、/health/ready、安全基线 API 与 CI 消费。
/// <para>内核不宣称「已通过等保三级」;<see cref="OverallCompliantForPhase1"/> 仅表示第一期强制配置项是否齐备。</para>
/// </summary>
public sealed class Level3PrecheckResult
{
    /// <summary>能力版本,如 <c>level3-phase1</c></summary>
    public string CapabilityVersion { get; init; } = Level3PrecheckConstants.CapabilityVersion;

    /// <summary>当前安全档(None|Level3)</summary>
    public string Profile { get; init; } = nameof(SecurityProfile.None);

    /// <summary>宿主环境名(Development|Production|…)</summary>
    public string Environment { get; init; } = "";

    /// <summary>检查项列表</summary>
    public IReadOnlyList<Level3PrecheckItem> Checks { get; init; } = [];

    /// <summary>第二/三期 Level3 强制项(本内核版本尚未实现)</summary>
    public IReadOnlyList<Level3UnimplementedMandate> UnimplementedMandates { get; init; } = [];

    /// <summary>
    /// 是否满足第一期配置闭环:Profile=Level3 且无任何 fail 项。
    /// 即使为 true 也不等于完整三级基线(见 <see cref="UnimplementedMandates"/>)。
    /// </summary>
    public bool OverallCompliantForPhase1 { get; init; }

    /// <summary>是否存在 Level3 关键项失败(启动闸门 / readiness 用)</summary>
    public bool HasCriticalFailures => Checks.Any(c => c.Critical && c.Status == Level3CheckStatus.Fail);

    /// <summary>关键失败项 Id 列表(稳定、可定位)</summary>
    public IReadOnlyList<string> CriticalFailureIds =>
        Checks.Where(c => c.Critical && c.Status == Level3CheckStatus.Fail).Select(c => c.Id).ToList();

    /// <summary>是否存在任意 fail 项(含非关键)</summary>
    public bool HasAnyFailure => Checks.Any(c => c.Status == Level3CheckStatus.Fail);
}

/// <summary>预检常量与稳定检查项 Id。</summary>
public static class Level3PrecheckConstants
{
    /// <summary>第一期能力版本标识</summary>
    public const string CapabilityVersion = "level3-phase1";

    public const string CheckProfileLevel3 = "profile_level3";
    public const string CheckRedisProvider = "redis_provider";
    public const string CheckRedisActual = "redis_actual";
    public const string CheckRedisAuth = "redis_auth";
    public const string CheckRedisTls = "redis_tls";
    public const string CheckSecretProtectorKey = "secret_protector_key";
    public const string CheckMfaInitState = "mfa_init_state";
    public const string CheckSessionPolicyFloors = "session_policy_floors";
    public const string CheckCookieCsrfTopology = "cookie_csrf_topology";
    public const string CheckDeployGrantStore = "deploy_grant_store";

    /// <summary>
    /// 第二/三期 Level3 强制项清单(固定;报告始终列出,避免把一期读成完整三级基线)。
    /// </summary>
    public static IReadOnlyList<Level3UnimplementedMandate> UnimplementedPhase23Mandates { get; } =
    [
        new("audit_retention_180d", "审计留存 ≥180 天", 2,
            "操作/登录/异常审计最短 180 天留存、禁止一键清空、仅清理到期数据。"),
        new("audit_outbox_siem", "审计外送 Outbox / SIEM", 2,
            "安全事件事务性 Outbox、可替换 ISecurityEventSink、持续失败使 readiness 不就绪。"),
        new("audit_hash_chain", "审计完整性哈希链", 2,
            "按分区单调序号与前序哈希、每日签名锚点外送。"),
        new("security_headers_https", "安全响应头与 HTTPS 冒烟", 2,
            "HSTS/CSP/防嵌入/nosniff/Referrer-Policy/Permissions-Policy 与生产入口 HTTPS 校验。"),
        new("malware_scan", "上传恶意文件扫描", 2,
            "可替换 IFileSecurityScanner;未配置/失败/超时拒绝入库。"),
        new("sensitive_mask_export", "敏感展示掩码与导出再认证", 2,
            "查询默认掩码、明文权限、敏感导出再认证与可选水印。"),
        new("security_event_sink", "安全事件接收端强制配置", 2,
            "Level3 下必须配置实际 ISecurityEventSink;仅本地日志不算告警。"),
        new("field_crypto", "通用字段加密与密钥轮换", 3,
            "IFieldProtector 认证加密、版本号、渐进重加密与查询哈希索引。"),
        new("clientid_hmac", "第三方 ClientId+HMAC 接入", 3,
            "独立机器身份、HMAC 请求签名、nonce 防重放;ClientSecret 信封加密。"),
        new("sbom_supply_chain", "SBOM 与发布漏洞门禁", 3,
            "SPDX/CycloneDX SBOM、Critical/High 阻断、产物签名/构建证明。"),
        new("crypto_profile_gm", "国密密码档 CryptoProfile=GM", 3,
            "SM2/SM3/SM4 与国密 TLS 路径;与 Level3 独立、需显式启用并校验。"),
    ];
}

/// <summary>
/// Level3 一期预检服务:输出结构化机器可读结果(Profile / Redis 认证·TLS / 数据保护密钥 / MFA 初始化 / 会话策略下限等)。
/// 注册为 Scoped;<c>TryAdd</c> 可替换;方法 <c>virtual</c>。
/// </summary>
public interface ILevel3PrecheckService
{
    /// <summary>执行预检。结果不含密钥、连接串密码、TOTP 种子等敏感值。</summary>
    Task<Level3PrecheckResult> RunAsync(CancellationToken cancellationToken = default);
}
