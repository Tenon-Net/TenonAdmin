using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISecurityPolicyProvider"/> 默认实现:每个值先读 <see cref="SysConfig"/>
/// (<see cref="IConfigService.GetValueByKeyAsync"/>——读穿透缓存、改动即失效),
/// 缺失或解析失败则回退到 Options 默认。配置键常量集中在此,<see cref="ConfigSeed"/> 与前端安全策略 Tab 均以此对齐。
/// <para>
/// 历史 Profile=Level3 在本读取路径施加不可放宽下限(过渡兼容 ADR 0006)。
/// 产品路径(<c>Totp:Enabled</c> / <c>CookieMode</c>)不自动钳位,只读 SysConfig + Options。
/// </para>
/// </summary>
public class SecurityPolicyProvider(
    IConfigService config,
    AdminSecurityOptions security,
    AdminJwtOptions jwt) : ISecurityPolicyProvider
{
    /// <summary>安全策略配置项分组编码(配置中心「安全策略」Tab 按此分组加载)</summary>
    public const string GROUP = "security";

    public const string KEY_MAX_FAIL = "sys.security.loginLock.maxFailCount";
    public const string KEY_LOCK_MIN = "sys.security.loginLock.lockMinutes";
    public const string KEY_MIN_LEN = "sys.security.password.minLength";
    public const string KEY_REQ_UPPER = "sys.security.password.requireUpper";
    public const string KEY_REQ_LOWER = "sys.security.password.requireLower";
    public const string KEY_REQ_DIGIT = "sys.security.password.requireDigit";
    public const string KEY_REQ_SPECIAL = "sys.security.password.requireSpecial";
    public const string KEY_EXPIRE_DAYS = "sys.security.password.expireDays";
    public const string KEY_HISTORY_COUNT = "sys.security.password.historyCount";
    public const string KEY_ACCESS_MIN = "sys.security.session.accessMinutes";
    public const string KEY_REFRESH_MIN = "sys.security.session.refreshMinutes";

    // 密码策略默认值(种子缺失时的兜底,须与 ConfigSeed 播种默认一致)
    public const int DEFAULT_MIN_LEN = 8;

    // ── Level3 不可放宽下限(等保三级应用安全基线一期)────────────────
    public const int Level3MinPasswordLength = 12;
    public const int Level3MinHistoryCount = 5;
    public const int Level3MaxExpireDays = 90;
    public const int Level3MaxFailCount = 5;
    public const int Level3MinLockMinutes = 15;
    public const int Level3MaxAccessMinutes = 15;
    /// <summary>绝对会话最长 8 小时(分钟);刷新令牌不得突破</summary>
    public const int Level3MaxAbsoluteSessionMinutes = 8 * 60;
    /// <summary>普通用户闲置超时(分钟)</summary>
    public const int Level3IdleMinutesNormal = 30;
    /// <summary>MFA 用户闲置超时(分钟)</summary>
    public const int Level3IdleMinutesMfa = 15;
    /// <summary>普通用户最大并发会话</summary>
    public const int Level3MaxConcurrentNormal = 3;
    /// <summary>MFA 用户默认并发会话</summary>
    public const int Level3MaxConcurrentMfaDefault = 1;
    /// <summary>MFA 用户并发上限(再多需运维显式收紧配置,内核钳到此)</summary>
    public const int Level3MaxConcurrentMfaCap = 2;
    /// <summary>MFA 闲置账号:告警天数</summary>
    public const int Level3IdleAccountWarnDays = 60;
    /// <summary>MFA 闲置账号:自动停用天数(超管仅告警)</summary>
    public const int Level3IdleAccountDisableDays = 90;

    /// <summary>历史 Level3 总档才施加策略地板;产品独立键不钳位。</summary>
    private bool IsLegacyLevel3 => security.IsLegacyLevel3Profile;

    /// <inheritdoc />
    public virtual async Task<(int MaxFailCount, int LockMinutes)> GetLoginLockAsync()
    {
        var maxFail = await IntAsync(KEY_MAX_FAIL, security.LoginLock.MaxFailCount);
        var lockMin = await IntAsync(KEY_LOCK_MIN, security.LoginLock.LockMinutes);
        if (!IsLegacyLevel3) return (maxFail, lockMin);

        // Level3:5 次失败至少锁 15 分钟;关闭锁定(0)时强制打开为 5/15;次数上限 5(可更严=更小,下限 1)
        if (maxFail <= 0)
        {
            maxFail = Level3MaxFailCount;
            lockMin = Math.Max(lockMin, Level3MinLockMinutes);
        }
        else
        {
            maxFail = Math.Clamp(maxFail, 1, Level3MaxFailCount);
            lockMin = Math.Max(lockMin, Level3MinLockMinutes);
        }
        return (maxFail, lockMin);
    }

    /// <inheritdoc />
    public virtual async Task<(int AccessMinutes, int RefreshMinutes)> GetSessionTtlAsync()
    {
        var access = await IntAsync(KEY_ACCESS_MIN, jwt.ExpireMinutes);
        var refresh = await IntAsync(KEY_REFRESH_MIN, jwt.RefreshExpireMinutes);
        if (!IsLegacyLevel3) return (access, refresh);

        // 历史 Level3:access ≤15 分;refresh 不得超过绝对会话窗 8h
        access = Math.Clamp(access, 1, Level3MaxAccessMinutes);
        refresh = Math.Clamp(refresh, access, Level3MaxAbsoluteSessionMinutes);
        return (access, refresh);
    }

    /// <inheritdoc />
    public virtual async Task<PasswordPolicy> GetPasswordPolicyAsync()
    {
        var minLen = await IntAsync(KEY_MIN_LEN, DEFAULT_MIN_LEN);
        var reqUpper = await BoolAsync(KEY_REQ_UPPER, true);
        var reqLower = await BoolAsync(KEY_REQ_LOWER, true);
        var reqDigit = await BoolAsync(KEY_REQ_DIGIT, true);
        var reqSpecial = await BoolAsync(KEY_REQ_SPECIAL, false);

        if (IsLegacyLevel3)
        {
            minLen = Math.Max(minLen, Level3MinPasswordLength);
            var required = (reqUpper ? 1 : 0) + (reqLower ? 1 : 0) + (reqDigit ? 1 : 0) + (reqSpecial ? 1 : 0);
            if (required < 3)
            {
                reqUpper = true;
                reqLower = true;
                reqDigit = true;
            }
        }

        return new PasswordPolicy(minLen, reqUpper, reqLower, reqDigit, reqSpecial);
    }

    /// <inheritdoc />
    public virtual async Task<int> GetPasswordExpireDaysAsync()
    {
        var days = await IntAsync(KEY_EXPIRE_DAYS, 0);
        if (!IsLegacyLevel3) return days;
        if (days <= 0) return Level3MaxExpireDays;
        return Math.Min(days, Level3MaxExpireDays);
    }

    /// <inheritdoc />
    public virtual async Task<int> GetPasswordHistoryCountAsync()
    {
        var n = await IntAsync(KEY_HISTORY_COUNT, 0);
        if (!IsLegacyLevel3) return n;
        return Math.Max(n, Level3MinHistoryCount);
    }

    /// <inheritdoc />
    public virtual async Task ValidatePasswordAsync(string password)
    {
        var p = await GetPasswordPolicyAsync();
        var pw = password ?? "";
        var hasUpper = pw.Any(char.IsUpper);
        var hasLower = pw.Any(char.IsLower);
        var hasDigit = pw.Any(char.IsDigit);
        var hasSpecial = pw.Any(c => !char.IsLetterOrDigit(c));

        var ok = pw.Length >= p.MinLength
            && (!p.RequireUpper || hasUpper)
            && (!p.RequireLower || hasLower)
            && (!p.RequireDigit || hasDigit)
            && (!p.RequireSpecial || hasSpecial);

        if (ok && IsLegacyLevel3)
        {
            var classes = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            ok = classes >= 3;
        }

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
