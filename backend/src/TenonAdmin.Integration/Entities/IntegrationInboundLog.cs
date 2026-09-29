using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>开放调用结果分类。</summary>
public enum InboundCallOutcome
{
    /// <summary>成功(业务码 0)</summary>
    Succeeded = 0,

    /// <summary>业务失败(信封业务码非 0,HTTP 2xx)</summary>
    BusinessFailed = 1,

    /// <summary>认证失败(401)</summary>
    Unauthorized = 2,

    /// <summary>授权或范围拒绝(403)</summary>
    Forbidden = 3,

    /// <summary>被限流(429)</summary>
    RateLimited = 4,

    /// <summary>请求不合法(400)</summary>
    InvalidRequest = 5,

    /// <summary>服务端错误(5xx / 未处理异常)</summary>
    Error = 6,
}

/// <summary>
/// 开放调用记录(<c>itg_inbound_log</c>,实现契约 §5):接入应用的真实身份、端点、结果、耗时与追踪标识。
/// 与用户操作审计(<c>sys_op_log</c>)分开;<b>不记录请求/响应正文与任何认证头</b>,<see cref="Path"/> 不含查询串,
/// 凭据只以公开标识 <see cref="KeyId"/> 出现。只增不改,按保留期物理清理。
/// </summary>
[SugarTable("itg_inbound_log", TableDescription = "开放调用记录")]
[SugarIndex("idx_itg_inbound_log_time", nameof(CreateTime), OrderByType.Desc)]
[SugarIndex("idx_itg_inbound_log_app", nameof(AppId), OrderByType.Asc, nameof(CreateTime), OrderByType.Desc)]
[SugarIndex("idx_itg_inbound_log_trace", nameof(TraceId), OrderByType.Asc)]
public class IntegrationInboundLog : AuditEntity
{
    /// <summary>接入应用 Id;凭据不可识别时为空。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "接入应用 Id")]
    public long? AppId { get; set; }

    /// <summary>接入应用编码(冗余,应用删除后仍可读)。</summary>
    [SugarColumn(Length = 96, IsNullable = true, ColumnDescription = "接入应用编码")]
    public string? AppCode { get; set; }

    /// <summary>凭据 Id。</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "凭据 Id")]
    public long? CredentialId { get; set; }

    /// <summary>凭据公开标识。</summary>
    [SugarColumn(Length = 32, IsNullable = true, ColumnDescription = "凭据公开标识")]
    public string? KeyId { get; set; }

    [SugarColumn(Length = 16, ColumnDescription = "HTTP 方法")]
    public string HttpMethod { get; set; } = "";

    /// <summary>规范化路由权限码(与授权勾选的码一致);未匹配端点时为空串。</summary>
    [SugarColumn(Length = 256, ColumnDescription = "端点权限码")]
    public string Route { get; set; } = "";

    /// <summary>请求路径(不含查询串)。</summary>
    [SugarColumn(Length = 512, ColumnDescription = "请求路径(不含查询串)")]
    public string Path { get; set; } = "";

    [SugarColumn(ColumnDescription = "HTTP 状态码")]
    public int StatusCode { get; set; }

    /// <summary>统一信封业务码(0 为成功)。</summary>
    [SugarColumn(ColumnDescription = "业务码")]
    public int ResultCode { get; set; }

    [SugarColumn(ColumnDescription = "结果分类")]
    public InboundCallOutcome Outcome { get; set; }

    /// <summary>失败原因(机器可读短码,如 credential_expired / not_granted / scope_not_bound)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "失败原因")]
    public string? FailureReason { get; set; }

    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long ElapsedMs { get; set; }

    /// <summary>追踪标识(响应头 <c>X-Trace-Id</c> 同值)。</summary>
    [SugarColumn(Length = 64, ColumnDescription = "追踪标识")]
    public string TraceId { get; set; } = "";

    /// <summary>调用方提供的请求标识(<c>X-Request-Id</c>,已校验字符集与长度)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "调用方请求标识")]
    public string? ClientRequestId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "客户端 IP")]
    public string? ClientIp { get; set; }
}
