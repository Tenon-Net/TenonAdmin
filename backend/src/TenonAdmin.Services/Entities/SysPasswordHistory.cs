using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 密码历史(设计 §14 登录加固)——记录用户历史口令的哈希,用于"禁止重用最近 N 个口令"策略
/// (开关 <c>sys.security.password.historyCount</c>,默认 0=关)。
/// <para>只增不改、无软删语义;每用户仅保留最新 N 条(<c>PasswordHistoryService.AppendAsync</c> 写入后裁剪)。
/// <b>只存哈希</b>(与 <c>SysUser.Password</c> 同 <c>IPasswordHasher</c>);校验时逐条 <c>Verify(明文, 哈希)</c>——
/// 盐不同,不能比哈希、只能验明文。继承 <see cref="BaseEntity"/> 复用雪花 Id(时间有序,取最新即按 Id 降序)。</para>
/// </summary>
[SugarTable("sys_password_history", TableDescription = "密码历史")]
// 查询轴恒为"某用户的最新 N 条"(Where UserId + OrderBy Id desc),按 UserId 建索引。
[SugarIndex("idx_sys_pwd_hist_user", nameof(UserId), OrderByType.Asc)]
public class SysPasswordHistory : BaseEntity
{
    /// <summary>所属用户 Id</summary>
    [SugarColumn(ColumnDescription = "用户 Id")]
    public long UserId { get; set; }

    /// <summary>历史口令哈希(<see cref="TenonAdmin.Core.IPasswordHasher"/> 产出,含盐)</summary>
    [SugarColumn(Length = 256, ColumnDescription = "口令哈希")]
    public string PasswordHash { get; set; } = "";
}
