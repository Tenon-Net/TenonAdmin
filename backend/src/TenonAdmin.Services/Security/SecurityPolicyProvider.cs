using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISecurityPolicyProvider"/> 默认实现:每个值先读 <see cref="SysConfig"/>
/// (<see cref="IConfigService.GetValueByKeyAsync"/>——读穿透缓存、改动即失效),
/// 缺失或解析失败则回退到 Options 默认。配置键常量集中在此,<see cref="ConfigSeed"/> 与前端安全策略 Tab 均以此对齐。
/// </summary>
public class SecurityPolicyProvider(
    IConfigService config,
    AdminSecurityOptions security,
    AdminJwtOptions jwt) : ISecurityPolicyProvider
{
    /// <summary>安全策略配置项分组编码(配置中心「安全策略」Tab 按此分组加载)</summary>
    internal const string GROUP = "security";

    internal const string KEY_MAX_FAIL = "sys.security.loginLock.maxFailCount";
    internal const string KEY_LOCK_MIN = "sys.security.loginLock.lockMinutes";
    internal const string KEY_MIN_LEN = "sys.security.password.minLength";
    internal const string KEY_REQ_UPPER = "sys.security.password.requireUpper";
    internal const string KEY_REQ_LOWER = "sys.security.password.requireLower";
    internal const string KEY_REQ_DIGIT = "sys.security.password.requireDigit";
    internal const string KEY_REQ_SPECIAL = "sys.security.password.requireSpecial";
    internal const string KEY_ACCESS_MIN = "sys.security.session.accessMinutes";
    internal const string KEY_REFRESH_MIN = "sys.security.session.refreshMinutes";

    // 密码策略默认值(种子缺失时的兜底,须与 ConfigSeed 播种默认一致)
    internal const int DEFAULT_MIN_LEN = 8;

    /// <inheritdoc />
    public virtual async Task<(int MaxFailCount, int LockMinutes)> GetLoginLockAsync() =>
        (await IntAsync(KEY_MAX_FAIL, security.LoginLock.MaxFailCount),
         await IntAsync(KEY_LOCK_MIN, security.LoginLock.LockMinutes));

    /// <inheritdoc />
    public virtual async Task<(int AccessMinutes, int RefreshMinutes)> GetSessionTtlAsync() =>
        (await IntAsync(KEY_ACCESS_MIN, jwt.ExpireMinutes),
         await IntAsync(KEY_REFRESH_MIN, jwt.RefreshExpireMinutes));

    /// <inheritdoc />
    public virtual async Task<PasswordPolicy> GetPasswordPolicyAsync() => new(
        await IntAsync(KEY_MIN_LEN, DEFAULT_MIN_LEN),
        await BoolAsync(KEY_REQ_UPPER, true),
        await BoolAsync(KEY_REQ_LOWER, true),
        await BoolAsync(KEY_REQ_DIGIT, true),
        await BoolAsync(KEY_REQ_SPECIAL, false));

    /// <inheritdoc />
    public virtual async Task ValidatePasswordAsync(string password)
    {
        var p = await GetPasswordPolicyAsync();
        var pw = password ?? "";
        var ok = pw.Length >= p.MinLength
            && (!p.RequireUpper || pw.Any(char.IsUpper))
            && (!p.RequireLower || pw.Any(char.IsLower))
            && (!p.RequireDigit || pw.Any(char.IsDigit))
            && (!p.RequireSpecial || pw.Any(c => !char.IsLetterOrDigit(c)));

        AdminException.ThrowIf(!ok, ErrorCode.PasswordTooWeak, new Dictionary<string, object?>
        {
            ["minLength"] = p.MinLength,
            ["requireUpper"] = p.RequireUpper,
            ["requireLower"] = p.RequireLower,
            ["requireDigit"] = p.RequireDigit,
            ["requireSpecial"] = p.RequireSpecial,
        });
    }

    // DB 值优先、解析失败回退默认。ponytail: 逐键读足够——键少且 IConfigService 已读穿透缓存,不预造批量读接口。
    private async Task<int> IntAsync(string key, int fallback) =>
        int.TryParse(await config.GetValueByKeyAsync(key), out var v) ? v : fallback;

    private async Task<bool> BoolAsync(string key, bool fallback) =>
        bool.TryParse(await config.GetValueByKeyAsync(key), out var v) ? v : fallback;
}
