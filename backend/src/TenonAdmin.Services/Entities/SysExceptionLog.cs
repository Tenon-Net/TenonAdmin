using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 异常日志(设计 §4)——由 <c>ExceptionLogFilter</c> 在未捕获异常(非 <c>AdminException</c> 业务异常)冒泡时自动写入。
/// 记录"哪个接口、什么方法、追踪号、异常类型、消息、堆栈、谁触发的、从哪来"。
/// <para>与 <see cref="SysOpLog"/>/<see cref="SysLoginLog"/> 一样只增不改、无软删语义,"清空"走硬删。
/// 只落异常本身(类型/消息/堆栈),<b>绝不落请求响应体</b>——响应体可能含明文口令/令牌(设计 §14)。
/// 业务失败(<c>AdminException</c>)不进本表——那是可预期的分支,由操作日志的 ResultCode 表达。</para>
/// </summary>
[SugarTable("sys_exception_log", TableDescription = "异常日志")]
// 同 SysOpLog:时间倒序是日志唯一天然查询轴(默认排序 + 时间范围筛 + 按天清理),DDL 只有一次机会。
[SugarIndex("idx_sys_exception_log_create", nameof(BaseEntity.CreateTime), OrderByType.Desc)]
public class SysExceptionLog : BaseEntity
{
    [SugarColumn(Length = 16, ColumnDescription = "HTTP 方法")]
    public string HttpMethod { get; set; } = "";

    [SugarColumn(Length = 256, ColumnDescription = "请求路径")]
    public string Path { get; set; } = "";

    /// <summary>请求追踪号(<c>HttpContext.TraceIdentifier</c>);串起同一请求的诊断日志与本条异常记录</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "追踪号")]
    public string? TraceId { get; set; }

    /// <summary>异常类型全名(如 <c>System.NullReferenceException</c>);便于按类型统计与筛选</summary>
    [SugarColumn(Length = 256, ColumnDescription = "异常类型")]
    public string ExceptionType { get; set; } = "";

    /// <summary>异常消息(截断)。只记消息文本,不含请求/响应体。</summary>
    /// <remarks>SqlServer 禁止裸 text(非 Unicode);见 <c>SysJobLog.ErrorText</c> 同注。</remarks>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "异常消息")]
    public string? Message { get; set; }

    /// <summary>异常堆栈(截断);排障主依据</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true, ColumnDescription = "异常堆栈")]
    public string? StackTrace { get; set; }

    /// <summary>触发人用户 Id(从当前登录态取,匿名端点为 null;名称按 Id 关联 sys_user,不冗余存)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "触发人用户 Id")]
    public long? OperatorId { get; set; }

    /// <summary>触发人姓名(不落库,分页时按 OperatorId 关联 sys_user 补;查询清软删过滤器,离职用户历史记录仍显示姓名)</summary>
    [SugarColumn(IsIgnore = true)]
    public string? OperatorName { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "来源 IP")]
    public string? Ip { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "User-Agent")]
    public string? UserAgent { get; set; }
}
