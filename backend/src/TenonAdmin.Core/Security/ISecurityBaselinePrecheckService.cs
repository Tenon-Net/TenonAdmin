namespace TenonAdmin.Core;

/// <summary>
/// 可选安全态势预检状态字面量(机器可读 JSON 直接输出 <c>pass|fail|warn</c>)。
/// 历史名 Level3CheckStatus;ADR 0006 后与等保总档脱钩。
/// </summary>
public static class SecurityBaselineCheckStatus
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
/// <param name="Status"><see cref="SecurityBaselineCheckStatus"/> 常量:pass|fail|warn</param>
/// <param name="Message">现状说明(脱敏)</param>
/// <param name="Remediation">修复建议</param>
/// <param name="Critical">
/// 是否为关键诊断项(ADR 0006 后不再 fail-closed 阻断启动;仍用于报告 CriticalFailureIds)。
/// </param>
public sealed record SecurityBaselinePrecheckItem(
    string Id,
    string Name,
    string Status,
    string Message,
    string Remediation,
    bool Critical = false);

/// <summary>
/// 历史路线图中未实现的能力条目(明示能力边界;ADR 0006 后不再承诺按期交付)。
/// </summary>
/// <param name="Id">稳定标识</param>
/// <param name="Name">短名</param>
/// <param name="Phase">历史期次标记(2|3;仅文档语义)</param>
/// <param name="Description">缺口说明</param>
public sealed record SecurityBaselineUnimplementedMandate(
    string Id,
    string Name,
    int Phase,
    string Description);

/// <summary>
/// 可选安全态势预检结果。供诊断 API / CI 消费;ADR 0006 后不作为启动 fail-closed 合同。
/// <para>内核不宣称「已通过等保三级」;<see cref="OverallCompliantForPhase1"/> 仅表示历史一期检查项是否无 fail。</para>
/// </summary>
public sealed class SecurityBaselinePrecheckResult
{
    /// <summary>能力版本标识(JSON 字段兼容历史 <c>level3-phase1</c>)</summary>
    public string CapabilityVersion { get; init; } = SecurityBaselinePrecheckConstants.CapabilityVersion;

    /// <summary>当前安全档(None|Level3;Level3 为废弃总档)</summary>
    public string Profile { get; init; } = nameof(SecurityProfile.None);

    /// <summary>宿主环境名(Development|Production|…)</summary>
    public string Environment { get; init; } = "";

    /// <summary>检查项列表</summary>
    public IReadOnlyList<SecurityBaselinePrecheckItem> Checks { get; init; } = [];

    /// <summary>历史路线图未实现项(明示边界;不构成交付承诺)</summary>
    public IReadOnlyList<SecurityBaselineUnimplementedMandate> UnimplementedMandates { get; init; } = [];

    /// <summary>
    /// 历史一期检查是否无 fail(且 Profile 曾为 Level3 时语义更严)。
    /// 即使为 true 也不等于完整三级基线(见 <see cref="UnimplementedMandates"/>)。
    /// </summary>
    public bool OverallCompliantForPhase1 { get; init; }

    /// <summary>是否存在关键项失败(诊断用;不再阻断启动)</summary>
    public bool HasCriticalFailures => Checks.Any(c => c.Critical && c.Status == SecurityBaselineCheckStatus.Fail);

    /// <summary>关键失败项 Id 列表(稳定、可定位)</summary>
    public IReadOnlyList<string> CriticalFailureIds =>
        Checks.Where(c => c.Critical && c.Status == SecurityBaselineCheckStatus.Fail).Select(c => c.Id).ToList();

    /// <summary>是否存在任意 fail 项(含非关键)</summary>
    public bool HasAnyFailure => Checks.Any(c => c.Status == SecurityBaselineCheckStatus.Fail);
}

/// <summary>预检常量与稳定检查项 Id。</summary>
public static class SecurityBaselinePrecheckConstants
{
    /// <summary>能力版本标识(JSON 兼容;不代表仍交付完整 Level3)</summary>
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

    /// <summary>
    /// 第二/三期 Level3 强制项清单(固定;报告始终列出,避免把一期读成完整三级基线)。
    /// </summary>
    public static IReadOnlyList<SecurityBaselineUnimplementedMandate> UnimplementedPhase23Mandates { get; } =
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
/// 可选安全态势预检:输出结构化机器可读结果(Profile / Redis / 数据保护密钥 / MFA / 会话与 Cookie 拓扑等)。
/// 注册为 Scoped;<c>TryAdd</c> 可替换。历史接口名 <c>ILevel3PrecheckService</c>。
/// </summary>
public interface ISecurityBaselinePrecheckService
{
    /// <summary>执行预检。结果不含密钥、连接串密码、TOTP 种子等敏感值。</summary>
    Task<SecurityBaselinePrecheckResult> RunAsync(CancellationToken cancellationToken = default);
}
