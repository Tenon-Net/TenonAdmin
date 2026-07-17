namespace TenonAdmin.Services;

/// <summary>
/// 密码历史防重用服务(设计 §14)——按 <c>sys.security.password.historyCount</c>(默认 0=关)约束
/// 新口令不得与当前或最近 N 个用过的口令相同。注册为 <c>TryAddScoped</c>,方法 <c>virtual</c> 可覆写
/// (如换更严的相似度判定、接企业口令风控)。
/// <para>开关为 0 时两个方法均为空操作(不校验、不写历史,零表增长)。</para>
/// </summary>
public interface IPasswordHistoryService
{
    /// <summary>
    /// 校验新口令未与当前或最近 N 个历史口令重复;重复则抛 <see cref="TenonAdmin.Core.ErrorCode.PasswordReused"/>。
    /// 开关关闭(N&lt;=0)时直接返回。
    /// </summary>
    Task EnsureNotReusedAsync(long userId, string newPlainPassword);

    /// <summary>
    /// 记录一条口令历史(存哈希),并把该用户的历史裁剪到最新 N 条。开关关闭(N&lt;=0)时直接返回(不写)。
    /// </summary>
    Task AppendAsync(long userId, string passwordHash);
}
