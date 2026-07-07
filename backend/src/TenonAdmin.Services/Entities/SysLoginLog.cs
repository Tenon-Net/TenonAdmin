using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 登录日志(设计 §4)——由 <c>AuthService</c> 在登录成功/失败路径写入(§14 安全审计:失败也必须留痕)。
/// <para>失败时也记录<b>原始输入账号</b>(哪怕账号不存在),供暴力破解排查;但登录日志<b>绝不落密码</b>。
/// 与 <see cref="SysOpLog"/> 一样只增不改、无软删,"清空"走硬删。</para>
/// </summary>
[SugarTable("sys_login_log", TableDescription = "登录日志")]
public class SysLoginLog : BaseEntity
{
    /// <summary>登录账号(即使账号不存在也记原始输入,便于排查探测)</summary>
    [SugarColumn(Length = 64, ColumnDescription = "登录账号")]
    public string Account { get; set; } = "";

    [SugarColumn(ColumnDescription = "是否成功")]
    public bool Success { get; set; }

    /// <summary>结果码(0 成功;失败为 40001/40005 等具体原因码,便于统计失败分布)</summary>
    [SugarColumn(ColumnDescription = "结果码")]
    public int ResultCode { get; set; }

    /// <summary>登录成功时的用户 Id(失败为 null)</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "用户 Id(成功时)")]
    public long? UserId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "来源 IP")]
    public string? Ip { get; set; }

    [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "User-Agent")]
    public string? UserAgent { get; set; }
}
