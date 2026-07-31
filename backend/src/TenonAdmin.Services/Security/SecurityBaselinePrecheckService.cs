using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISecurityBaselinePrecheckService"/> 默认实现:读部署 Options + 有效策略 + 可选用户库状态,
/// 输出第一期预检报告并列出第二/三期未实现强制项。
/// </summary>
public class SecurityBaselinePrecheckService(
    ISecurityProfileAccessor profile,
    AdminSecurityOptions security,
    AdminCacheOptions cache,
    ISecurityPolicyProvider policy,
    IHostEnvironment env,
    IRepository<SysUser>? users = null,
    ICacheProvider? cacheProvider = null,
    ILogger<SecurityBaselinePrecheckService>? logger = null,
    AdminApiOptions? api = null) : ISecurityBaselinePrecheckService
{
    /// <inheritdoc />
    public virtual async Task<SecurityBaselinePrecheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<SecurityBaselinePrecheckItem>
        {
            CheckProfile(),
            CheckRedisProvider(),
            CheckActualCacheProvider(),
            CheckRedisAuth(),
            CheckRedisTls(),
            CheckSecretProtectorKey(),
            await CheckMfaInitStateAsync(cancellationToken),
            await CheckSessionPolicyFloorsAsync(cancellationToken),
            CheckCookieCsrfTopology(),
        };

        var isLevel3 = profile.IsLevel3;
        // 一期合规:显式 Level3 且本报告无 fail(warn 可接受,如超管尚未绑定 TOTP)
        var overall = isLevel3 && checks.All(c => c.Status != SecurityBaselineCheckStatus.Fail);

        var result = new SecurityBaselinePrecheckResult
        {
            CapabilityVersion = SecurityBaselinePrecheckConstants.CapabilityVersion,
            Profile = profile.Profile.ToString(),
            Environment = env.EnvironmentName,
            Checks = checks,
            UnimplementedMandates = SecurityBaselinePrecheckConstants.UnimplementedPhase23Mandates,
            OverallCompliantForPhase1 = overall,
        };

        if (result.HasCriticalFailures)
        {
            logger?.LogError(
                "Level3 预检关键项失败: {Ids}",
                string.Join(", ", result.CriticalFailureIds));
        }

        return result;
    }

    /// <summary>Profile 检查:Level3 通过;生产未启用 → warn;其它环境未启用 → warn(不阻断)。</summary>
    protected virtual SecurityBaselinePrecheckItem CheckProfile()
    {
        // ADR 0006:不再以 Profile=Level3 为合规目标;独立可选键 Totp / Session:CookieMode。
        if (profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckProfileLevel3,
                "Security Profile",
                SecurityBaselineCheckStatus.Warn,
                "仍配置历史 Profile=Level3(已废弃产品路径);功能过渡兼容中。",
                "迁移到 TenonAdmin:Security:Totp:Enabled 与 Session:CookieMode 等独立键,并去掉 Profile=Level3。",
                critical: false);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckProfileLevel3,
            "Security Profile",
            SecurityBaselineCheckStatus.Pass,
            $"Profile={profile.Profile};可选安全由 Totp/CookieMode 等独立开关控制(ADR 0006)。",
            "需要时显式启用 Totp:Enabled 或 Session:CookieMode;勿再依赖 Level3 总档。",
            critical: false);
    }

    /// <summary>Level3 强制 Redis;非 Level3 仅信息性检查。</summary>
    protected virtual SecurityBaselinePrecheckItem CheckRedisProvider()
    {
        var isRedis = string.Equals(cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase);
        var critical = profile.IsLevel3;

        if (isRedis)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisProvider,
                "Redis Provider",
                SecurityBaselineCheckStatus.Pass,
                "Cache:Provider=Redis。",
                "无需处理。",
                critical);
        }

        if (!profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisProvider,
                "Redis Provider",
                SecurityBaselineCheckStatus.Pass,
                $"非 Level3:当前 Provider={cache.Provider}(允许 Memory)。",
                "启用 Level3 前改为 Provider=Redis 并安装 TenonAdmin.Caching.Redis。",
                critical: false);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckRedisProvider,
            "Redis Provider",
            SecurityBaselineCheckStatus.Fail,
            $"Level3 禁止进程内缓存:当前 Provider={cache.Provider}。",
            "配置 TenonAdmin:Cache:Provider=Redis,安装 TenonAdmin.Caching.Redis,并在 AddTenonAdmin 之前调用 AddTenonAdminRedisCache。",
            critical: true);
    }

    /// <summary>
    /// 通过 <see cref="ISecureCacheCapabilities"/> 校验真实分布式缓存能力(不依赖类名含 Redis)。
    /// </summary>
    protected virtual SecurityBaselinePrecheckItem CheckActualCacheProvider()
    {
        var critical = profile.IsLevel3;
        var caps = cacheProvider as ISecureCacheCapabilities;

        if (!profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisActual,
                "Cache Implementation",
                SecurityBaselineCheckStatus.Pass,
                caps is null
                    ? "非 Level3:当前缓存未声明 ISecureCacheCapabilities(允许 Memory)。"
                    : "非 Level3:已注册安全缓存能力声明。",
                "启用 Level3 前须注册实现 ISecureCacheCapabilities 的 Redis 缓存。",
                critical: false);
        }

        if (caps is null || !caps.IsDistributed)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisActual,
                "Cache Implementation",
                SecurityBaselineCheckStatus.Fail,
                "Level3 要求实现 ISecureCacheCapabilities 的分布式缓存(通常为 TenonAdmin.Caching.Redis);当前为进程内或未声明能力。",
                "安装 TenonAdmin.Caching.Redis,并在 AddTenonAdmin() 之前调用 AddTenonAdminRedisCache。",
                critical: true);
        }

        if (!caps.HasAuthenticationConfigured || !caps.HasTlsConfigured)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisActual,
                "Cache Implementation",
                SecurityBaselineCheckStatus.Fail,
                $"分布式缓存能力声明不完整:auth={caps.HasAuthenticationConfigured}, tls={caps.HasTlsConfigured}。",
                "连接串含 password= 与 ssl=true,或设置 Cache:RequireTls=true。",
                critical: true);
        }

        // 探针:连不通时 fail-closed(测试可用假实现返回 Ok)
        try
        {
            var probe = caps.ProbeAsync().GetAwaiter().GetResult();
            if (!probe.Ok)
            {
                return Item(
                    SecurityBaselinePrecheckConstants.CheckRedisActual,
                    "Cache Implementation",
                    SecurityBaselineCheckStatus.Fail,
                    $"分布式缓存探针失败: {probe.Message}",
                    "确认 Redis 可达、TLS/认证正确,并在启动前修复连接。",
                    critical: true);
            }

            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisActual,
                "Cache Implementation",
                SecurityBaselineCheckStatus.Pass,
                $"分布式缓存能力声明与探针通过({probe.Message})。",
                "无需处理。",
                critical);
        }
        catch (Exception ex)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisActual,
                "Cache Implementation",
                SecurityBaselineCheckStatus.Fail,
                $"分布式缓存探针异常: {ex.GetType().Name}",
                "确认 Redis 可达与 TLS/认证配置。",
                critical: true);
        }
    }

    protected virtual SecurityBaselinePrecheckItem CheckRedisAuth()
    {
        var critical = profile.IsLevel3;
        var isRedis = string.Equals(cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase);

        if (!isRedis && !profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisAuth,
                "Redis Authentication",
                SecurityBaselineCheckStatus.Pass,
                "非 Redis / 非 Level3:跳过认证检查。",
                "启用 Level3 时连接串须含 password=。",
                critical: false);
        }

        if (!isRedis && profile.IsLevel3)
        {
            // redis_provider 已 fail;此处仍给可定位项
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisAuth,
                "Redis Authentication",
                SecurityBaselineCheckStatus.Fail,
                "Level3 要求 Redis 且连接串含认证密码;当前未使用 Redis。",
                "配置 Provider=Redis 且 RedisConnectionString 含 password=<非空>。",
                critical: true);
        }

        var (hasAuth, _) = RedisConnectionSecurity.Inspect(cache.RedisConnectionString, cache.RequireTls);
        var summary = RedisConnectionSecurity.Summarize(cache.RedisConnectionString);

        if (hasAuth)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisAuth,
                "Redis Authentication",
                SecurityBaselineCheckStatus.Pass,
                $"连接串已配置密码(摘要:{summary})。",
                "无需处理。",
                critical);
        }

        var status = profile.IsLevel3 ? SecurityBaselineCheckStatus.Fail : SecurityBaselineCheckStatus.Warn;
        return Item(
            SecurityBaselinePrecheckConstants.CheckRedisAuth,
            "Redis Authentication",
            status,
            $"Redis 连接串未检测到密码(摘要:{summary})。",
            "在 TenonAdmin:Cache:RedisConnectionString 中加入 password=<强密码>;生产禁止无认证 Redis。",
            critical);
    }

    protected virtual SecurityBaselinePrecheckItem CheckRedisTls()
    {
        var critical = profile.IsLevel3;
        var isRedis = string.Equals(cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase);

        if (!isRedis && !profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisTls,
                "Redis TLS",
                SecurityBaselineCheckStatus.Pass,
                "非 Redis / 非 Level3:跳过 TLS 检查。",
                "启用 Level3 时连接串须 ssl=true 或 Cache:RequireTls=true。",
                critical: false);
        }

        if (!isRedis && profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisTls,
                "Redis TLS",
                SecurityBaselineCheckStatus.Fail,
                "Level3 要求 Redis TLS;当前未使用 Redis。",
                "配置 Provider=Redis,连接串加 ssl=true(或 RequireTls=true)。",
                critical: true);
        }

        var (_, hasTls) = RedisConnectionSecurity.Inspect(cache.RedisConnectionString, cache.RequireTls);
        var summary = RedisConnectionSecurity.Summarize(cache.RedisConnectionString);

        if (hasTls)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckRedisTls,
                "Redis TLS",
                SecurityBaselineCheckStatus.Pass,
                cache.RequireTls
                    ? $"已声明 TLS(RequireTls=true 和/或 连接串;摘要:{summary})。"
                    : $"连接串已声明 TLS(摘要:{summary})。",
                "无需处理。",
                critical);
        }

        var status = profile.IsLevel3 ? SecurityBaselineCheckStatus.Fail : SecurityBaselineCheckStatus.Warn;
        return Item(
            SecurityBaselinePrecheckConstants.CheckRedisTls,
            "Redis TLS",
            status,
            $"未检测到 Redis TLS(摘要:{summary};RequireTls={cache.RequireTls})。",
            "连接串加入 ssl=true,或设置 TenonAdmin:Cache:RequireTls=true(部署声明强制 TLS)。",
            critical);
    }

    /// <summary>Level3 必须显式配置 DataProtection:Key;不输出密钥内容。</summary>
    protected virtual SecurityBaselinePrecheckItem CheckSecretProtectorKey()
    {
        var critical = profile.IsLevel3;
        var key = security.DataProtection?.Key;
        var configured = !string.IsNullOrWhiteSpace(key);

        if (configured)
        {
            // 与 LocalDataProtectionKeyProvider 同一下限:Base64 且解码后 ≥32 字节
            const int minKeyBytes = 32;
            try
            {
                var bytes = Convert.FromBase64String(key!.Trim());
                if (bytes.Length < minKeyBytes)
                {
                    return Item(
                        SecurityBaselinePrecheckConstants.CheckSecretProtectorKey,
                        "Secret Protector Key",
                        SecurityBaselineCheckStatus.Fail,
                        $"DataProtection:Key 过短({bytes.Length} 字节 < {minKeyBytes} 字节);启动后首次保护会失败。",
                        "配置至少 32 字节随机密钥的 Base64 到 TenonAdmin:Security:DataProtection:Key。",
                        critical: true);
                }

                return Item(
                    SecurityBaselinePrecheckConstants.CheckSecretProtectorKey,
                    "Secret Protector Key",
                    SecurityBaselineCheckStatus.Pass,
                    $"已配置数据保护主密钥({bytes.Length} bytes, KeyVersion={security.DataProtection?.KeyVersion ?? 1})。",
                    "无需处理。密钥勿写入日志或预检导出明文。",
                    critical);
            }
            catch (FormatException)
            {
                return Item(
                    SecurityBaselinePrecheckConstants.CheckSecretProtectorKey,
                    "Secret Protector Key",
                    SecurityBaselineCheckStatus.Fail,
                    "DataProtection:Key 不是合法 Base64。",
                    "配置至少 32 字节随机密钥的 Base64 到 TenonAdmin:Security:DataProtection:Key。",
                    critical: true);
            }
        }

        if (!profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckSecretProtectorKey,
                "Secret Protector Key",
                SecurityBaselineCheckStatus.Warn,
                "未配置 DataProtection:Key;开发环境可自动生成临时密钥。",
                "生产 / Level3 必须显式配置 TenonAdmin:Security:DataProtection:Key(Base64,≥32 字节)。",
                critical: false);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckSecretProtectorKey,
            "Secret Protector Key",
            SecurityBaselineCheckStatus.Fail,
            "Level3 必须显式配置数据保护主密钥;禁止使用开发自动密钥。",
            "设置 TenonAdmin:Security:DataProtection:Key 为 ≥32 字节随机密钥的 Base64(可接 KMS 替换 IDataProtectionKeyProvider)。",
            critical: true);
    }

    /// <summary>
    /// MFA 初始化态势(诊断用):是否有超管已绑定 TOTP。
    /// ADR 0006 后无 InitGrant 仪式——未绑定仅 warn,用户可自助绑定。
    /// </summary>
    protected virtual async Task<SecurityBaselinePrecheckItem> CheckMfaInitStateAsync(CancellationToken ct)
    {
        if (!profile.IsLevel3 && !security.IsTotpFeatureEnabled)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckMfaInitState,
                "MFA Init State",
                SecurityBaselineCheckStatus.Pass,
                "未启用 TOTP/历史 Level3:不强制第二因子初始化。",
                "需要时配置 TenonAdmin:Security:Totp:Enabled 并自助绑定。",
                critical: false);
        }

        if (users is null)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckMfaInitState,
                "MFA Init State",
                SecurityBaselineCheckStatus.Warn,
                "无法访问用户仓储,跳过超管 TOTP 绑定态势检查。",
                "确保预检在完整宿主 DI 中运行。",
                critical: false);
        }

        var anyBound = await users.AnyAsync(u => u.IsSuperAdmin && u.TotpEnabled);
        if (anyBound)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckMfaInitState,
                "MFA Init State",
                SecurityBaselineCheckStatus.Pass,
                "至少一名超级管理员已完成 TOTP 绑定。",
                "无需处理。",
                critical: false);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckMfaInitState,
            "MFA Init State",
            SecurityBaselineCheckStatus.Warn,
            "尚无已绑定 TOTP 的超级管理员;可通过登录页或个人安全自助绑定(不再使用部署期 InitGrant)。",
            "打开 Totp:Enabled 后用账号密码完成 /mfa/bind。",
            critical: false);
    }

    /// <summary>
    /// Cookie/CSRF 拓扑:仅在 <see cref="AdminSecurityOptions.IsCookieSessionEnabled"/> 时检查。
    /// CORS 跨源时必须配置 CookieDomain + AllowCredentials;空 CORS = 同源反代(推荐)。
    /// </summary>
    protected virtual SecurityBaselinePrecheckItem CheckCookieCsrfTopology()
    {
        if (!security.IsCookieSessionEnabled)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology,
                "Cookie/CSRF Topology",
                SecurityBaselineCheckStatus.Pass,
                "未启用 Cookie 会话(Session:CookieMode / 历史 Level3);body refresh 模式无需 CSRF 拓扑。",
                "需要时设 TenonAdmin:Security:Session:CookieMode=true;跨源再配 CookieDomain + CORS 凭证。",
                critical: false);
        }

        var origins = api?.Cors.AllowedOrigins ?? [];
        var domain = security.ResolveCookieDomain();
        var hasCrossOrigin = origins.Length > 0;

        if (!hasCrossOrigin)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology,
                "Cookie/CSRF Topology",
                SecurityBaselineCheckStatus.Pass,
                "Cookie 会话同源模型:未配置 CORS AllowedOrigins,Cookie host-only + 双提交 CSRF。",
                "前后端请经同一 origin 反代(推荐 Caddy/nginx 同域);跨源须显式配置。",
                critical: false);
        }

        if (string.IsNullOrEmpty(domain))
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology,
                "Cookie/CSRF Topology",
                SecurityBaselineCheckStatus.Fail,
                "已启用 Cookie 会话且配置了 CORS AllowedOrigins,但未设置 CookieDomain:SPA 无法读到 API host-only csrf Cookie。",
                "二选一:① 改为同源反代并清空 AllowedOrigins;② 设置 Session:CookieDomain(或历史 Level3:CookieDomain)为共享父域并启用 CORS AllowCredentials。",
                critical: true);
        }

        if (api?.Cors.AllowCredentials != true)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology,
                "Cookie/CSRF Topology",
                SecurityBaselineCheckStatus.Fail,
                "Cookie 会话跨源已配置 CookieDomain,但 Cors.AllowCredentials=false:浏览器不会携带 Cookie。",
                "设置 TenonAdmin:Api:Cors:AllowCredentials=true,并确保 AllowedOrigins 为显式列表(禁止 * )。",
                critical: true);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology,
            "Cookie/CSRF Topology",
            SecurityBaselineCheckStatus.Pass,
            $"Cookie 会话跨源模型:CookieDomain={domain},CORS origins={origins.Length},AllowCredentials=true。",
            "确认 CookieDomain 为 SPA 与 API 的公共父域,且边缘为 HTTPS(SameSite=None 需 Secure)。",
            critical: false);
    }

    /// <summary>会话/密码有效策略下限是否在读取层生效。</summary>
    protected virtual async Task<SecurityBaselinePrecheckItem> CheckSessionPolicyFloorsAsync(CancellationToken ct)
    {
        if (!profile.IsLevel3)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckSessionPolicyFloors,
                "Session Policy Floors",
                SecurityBaselineCheckStatus.Pass,
                "非 Level3:不施加会话/密码下限钳制。",
                "启用 Level3 后有效策略自动施加 15m access / 8h absolute / 密码与锁定下限。",
                critical: false);
        }

        var (access, refresh) = await policy.GetSessionTtlAsync();
        var (maxFail, lockMin) = await policy.GetLoginLockAsync();
        var pwd = await policy.GetPasswordPolicyAsync();
        var history = await policy.GetPasswordHistoryCountAsync();
        var expire = await policy.GetPasswordExpireDaysAsync();

        var issues = new List<string>();
        if (access > SecurityPolicyProvider.Level3MaxAccessMinutes)
            issues.Add($"accessMinutes={access}>{SecurityPolicyProvider.Level3MaxAccessMinutes}");
        if (refresh > SecurityPolicyProvider.Level3MaxAbsoluteSessionMinutes)
            issues.Add($"refreshMinutes={refresh}>{SecurityPolicyProvider.Level3MaxAbsoluteSessionMinutes}");
        if (maxFail <= 0 || maxFail > SecurityPolicyProvider.Level3MaxFailCount)
            issues.Add($"maxFailCount={maxFail}");
        if (lockMin < SecurityPolicyProvider.Level3MinLockMinutes)
            issues.Add($"lockMinutes={lockMin}<{SecurityPolicyProvider.Level3MinLockMinutes}");
        if (pwd.MinLength < SecurityPolicyProvider.Level3MinPasswordLength)
            issues.Add($"minLength={pwd.MinLength}");
        if (history < SecurityPolicyProvider.Level3MinHistoryCount)
            issues.Add($"historyCount={history}");
        if (expire <= 0 || expire > SecurityPolicyProvider.Level3MaxExpireDays)
            issues.Add($"expireDays={expire}");

        if (issues.Count > 0)
        {
            return Item(
                SecurityBaselinePrecheckConstants.CheckSessionPolicyFloors,
                "Session Policy Floors",
                SecurityBaselineCheckStatus.Fail,
                "Level3 有效策略下限未满足:" + string.Join("; ", issues),
                "检查 ISecurityPolicyProvider 实现是否在 Level3 下钳制下限;勿绕过默认 SecurityPolicyProvider。",
                critical: true);
        }

        return Item(
            SecurityBaselinePrecheckConstants.CheckSessionPolicyFloors,
            "Session Policy Floors",
            SecurityBaselineCheckStatus.Pass,
            $"有效策略满足一期下限:access≤{access}m, refresh≤{refresh}m, lock={maxFail}/{lockMin}m, " +
            $"password minLen={pwd.MinLength}, history={history}, expireDays={expire}。",
            "SysConfig 只能再收紧,不能放宽这些下限。",
            critical: true);
    }

    private static SecurityBaselinePrecheckItem Item(
        string id, string name, string status, string message, string remediation, bool critical) =>
        new(id, name, status, message, remediation, critical);

    /// <summary>第二/三期强制项清单(与 <see cref="SecurityBaselinePrecheckConstants.UnimplementedPhase23Mandates"/> 同源)。</summary>
    public static IReadOnlyList<SecurityBaselineUnimplementedMandate> UnimplementedMandates =>
        SecurityBaselinePrecheckConstants.UnimplementedPhase23Mandates;
}
