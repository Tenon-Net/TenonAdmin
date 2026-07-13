using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 操作日志(设计 §4)——由 <c>[OperationLog]</c> 特性 + 过滤器自动写入,业务代码零感知。
/// 记录"谁、什么时候、从哪、调了哪个接口、传了什么(脱敏)、多久、成没成"。
/// <para>本表<b>只增不改</b>(审计留痕),没有软删语义;"清空"走硬删(见 <c>LogService.ClearOperationAsync</c>)。
/// 继承 <see cref="BaseEntity"/> 只为复用雪花 Id + 自动填 <c>CreateTime</c>(= 操作时间);<c>IsDelete</c> 恒 false。</para>
/// </summary>
[SugarTable("sys_op_log", TableDescription = "操作日志")]
// 同 SysLoginLog:时间倒序是日志的唯一天然查询轴(默认排序 + 时间范围筛 + 按天清理),且 DDL 只有一次机会。
// 刻意不给 Title/Path 建索引——它们走 Contains(前导通配 LIKE '%x%'),索引用不上;Success 基数为 2,优化器会忽略。
[SugarIndex("idx_sys_op_log_create", nameof(BaseEntity.CreateTime), OrderByType.Desc)]
public class SysOpLog : BaseEntity
{
    /// <summary>操作名(来自 <c>[OperationLog("新增用户")]</c> 的标题)</summary>
    [SugarColumn(Length = 128, ColumnDescription = "操作名")]
    public string Title { get; set; } = "";

    [SugarColumn(Length = 16, ColumnDescription = "HTTP 方法")]
    public string HttpMethod { get; set; } = "";

    [SugarColumn(Length = 256, ColumnDescription = "请求路径")]
    public string Path { get; set; } = "";

    /// <summary>请求入参 JSON(密码等敏感字段已脱敏为 <c>***</c>);超大/不可序列化入参记占位串</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true, ColumnDescription = "入参(脱敏后 JSON)")]
    public string? ParamJson { get; set; }

    /// <summary>业务返回码(0 成功,其余见 <c>ErrorCode</c>)</summary>
    [SugarColumn(ColumnDescription = "业务返回码")]
    public int ResultCode { get; set; }

    /// <summary>是否成功(= ResultCode==0;冗余一列便于按成败直接筛选,免得每次算)</summary>
    [SugarColumn(ColumnDescription = "是否成功")]
    public bool Success { get; set; }

    /// <summary>
    /// 异常信息:仅动作抛异常时记(异常消息,截断)。业务码已由 ResultCode 表达,此列答"为什么失败";
    /// 成功/纯业务错误(未抛异常)恒 null。<b>只记异常消息不记响应体</b>——响应体可能含明文密码/令牌(设计 §14)。
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true, ColumnDescription = "异常信息")]
    public string? ExceptionMessage { get; set; }

    /// <summary>接口耗时(毫秒)</summary>
    [SugarColumn(ColumnDescription = "耗时(毫秒)")]
    public long ElapsedMs { get; set; }

    /// <summary>操作人用户 Id(从当前登录态取;名称按 Id 关联 sys_user,不冗余存)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "操作人用户 Id")]
    public long? OperatorId { get; set; }

    /// <summary>操作人姓名(不落库,分页时按 OperatorId 关联 sys_user 补;查询清软删过滤器,离职用户的历史记录仍显示姓名)</summary>
    [SugarColumn(IsIgnore = true)]
    public string? OperatorName { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "来源 IP")]
    public string? Ip { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "User-Agent")]
    public string? UserAgent { get; set; }
}
