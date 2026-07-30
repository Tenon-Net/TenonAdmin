namespace TenonAdmin.Core;

/// <summary>
/// Level3 闲置账号治理:MFA 用户 60 天未成功登录告警、90 天自动停用;超级管理员仅告警不停用。
/// 非 Level3 调用应为空操作。
/// </summary>
public interface IIdleAccountService
{
    /// <summary>扫描并执行告警/停用;返回处理摘要(供任务日志)。</summary>
    Task<IdleAccountScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>闲置账号扫描结果</summary>
/// <param name="Warned">触发 60 天告警的用户数</param>
/// <param name="Disabled">触发 90 天自动停用的用户数(不含超管)</param>
/// <param name="SuperAdminWarnedOnly">仅告警的超管数</param>
public record IdleAccountScanResult(int Warned, int Disabled, int SuperAdminWarnedOnly);
