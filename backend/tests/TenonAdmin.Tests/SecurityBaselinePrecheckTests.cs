using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 可选安全态势预检:Redis/TLS/密钥/拓扑等稳定 check id;报告含能力版本与历史未实现清单。
/// 驱动真实 <see cref="SecurityBaselinePrecheckService"/> / <see cref="RedisConnectionSecurity"/>。
/// </summary>
public class SecurityBaselinePrecheckTests
{
    /// <summary>
    /// 落盘样本 JSON 的目录。优先 <c>GROK_SCRATCH</c>(外部交付流水线注入);
    /// 否则用系统临时目录下的固定子路径——原先硬编码 <c>ADMINI~1\...\grok-goal-...</c>,
    /// 在别的机器/账号上 CreateDirectory 直接 UnauthorizedAccessException,把本已绿的预检断言冲红。
    /// </summary>
    private static readonly string ScratchDir =
        Environment.GetEnvironmentVariable("GROK_SCRATCH")
        ?? Path.Combine(Path.GetTempPath(), "tenon-admin-tests", "level3-precheck");

    private static readonly string SamplePath = Path.Combine(ScratchDir, "level3-precheck-sample.json");

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class MapConfig(Dictionary<string, string?> map) : IConfigService
    {
        public Task<string?> GetValueByKeyAsync(string key) =>
            Task.FromResult(map.TryGetValue(key, out var v) ? v : null);

        public Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input) => throw new NotImplementedException();
        public Task<SysConfig> GetAsync(long id) => throw new NotImplementedException();
        public Task<SiteInfoOutput> GetSiteInfoAsync() => throw new NotImplementedException();
        public Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items) => throw new NotImplementedException();
        public Task<long> AddAsync(ConfigInput input) => throw new NotImplementedException();
        public Task UpdateAsync(long id, ConfigInput input) => throw new NotImplementedException();
        public Task DeleteAsync(long id) => throw new NotImplementedException();
    }

    private static SecurityBaselinePrecheckService Make(
        SecurityProfile profile,
        string cacheProvider = "Memory",
        string? redisConn = null,
        bool requireTls = false,
        string? dataProtectionKey = null,
        string environment = "Production")
    {
        var security = new AdminSecurityOptions
        {
            Profile = profile,
            DataProtection = new AdminDataProtectionOptions
            {
                Key = dataProtectionKey,
                KeyVersion = 1,
            },
        };
        var cache = new AdminCacheOptions
        {
            Provider = cacheProvider,
            RedisConnectionString = redisConn,
            RequireTls = requireTls,
        };
        var env = new FakeEnv(environment);
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(
            new MapConfig(new Dictionary<string, string?>()),
            security,
            new AdminJwtOptions());

        // Level3 预检会看运行时 ICacheProvider 类型名是否含 Redis;测 ok 路径时注入假 Redis 实现
        ICacheProvider? runtimeCache = string.Equals(cacheProvider, "Redis", StringComparison.OrdinalIgnoreCase)
            ? new FakeRedisCacheProvider()
            : new FakeMemoryCacheProvider();

        return new SecurityBaselinePrecheckService(
            accessor,
            security,
            cache,
            policy,
            env,
            users: null,
            cacheProvider: runtimeCache,
            logger: NullLogger<SecurityBaselinePrecheckService>.Instance);
    }

    /// <summary>声明完整 ISecureCacheCapabilities 的假分布式缓存(不依赖类名)。</summary>
    private sealed class FakeRedisCacheProvider : ICacheProvider, ISecureCacheCapabilities
    {
        public bool IsDistributed => true;
        public bool HasAuthenticationConfigured => true;
        public bool HasTlsConfigured => true;
        public Task<(bool Ok, string Message)> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((true, "fake PING ok"));

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
        public Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
    }

    private sealed class FakeMemoryCacheProvider : ICacheProvider
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
        public Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
    }

    [Theory]
    [InlineData("localhost:6379", false, false)]
    [InlineData("localhost:6379,abortConnect=false", false, false)]
    [InlineData("localhost:6379,password=s3cret", true, false)]
    [InlineData("localhost:6379,password=s3cret,ssl=true", true, true)]
    [InlineData("my.redis:6380,password=x,ssl=True,abortConnect=False", true, true)]
    [InlineData("host:6379,pwd=abc", true, false)]
    public void RedisConnectionSecurity_inspects_auth_and_tls(string conn, bool auth, bool tls)
    {
        var (hasAuth, hasTls) = RedisConnectionSecurity.Inspect(conn);
        Assert.Equal(auth, hasAuth);
        Assert.Equal(tls, hasTls);
        Assert.DoesNotContain("s3cret", RedisConnectionSecurity.Summarize(conn));
        Assert.DoesNotContain("password=", RedisConnectionSecurity.Summarize(conn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedisConnectionSecurity_RequireTls_option_counts_as_tls()
    {
        var (_, hasTls) = RedisConnectionSecurity.Inspect("localhost:6379,password=x", requireTlsOption: true);
        Assert.True(hasTls);
    }

    [Fact]
    public async Task Level3_redis_without_tls_and_auth_fails_with_stable_ids()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var svc = Make(
            SecurityProfile.Level3,
            cacheProvider: "Redis",
            redisConn: "localhost:6379,abortConnect=false", // 无 password、无 ssl
            requireTls: false,
            dataProtectionKey: key);

        var result = await svc.RunAsync();

        Assert.Equal(SecurityBaselinePrecheckConstants.CapabilityVersion, result.CapabilityVersion);
        Assert.Equal(nameof(SecurityProfile.Level3), result.Profile);
        Assert.False(result.OverallCompliantForPhase1);
        Assert.True(result.HasCriticalFailures);

        var byId = result.Checks.ToDictionary(c => c.Id);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, byId[SecurityBaselinePrecheckConstants.CheckRedisAuth].Status);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, byId[SecurityBaselinePrecheckConstants.CheckRedisTls].Status);
        Assert.Equal("fail", byId[SecurityBaselinePrecheckConstants.CheckRedisAuth].Status);
        Assert.Contains(SecurityBaselinePrecheckConstants.CheckRedisAuth, result.CriticalFailureIds);
        Assert.Contains(SecurityBaselinePrecheckConstants.CheckRedisTls, result.CriticalFailureIds);

        // 无密钥泄漏
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(key, json);
    }

    [Fact]
    public async Task Level3_memory_provider_fails_redis_provider_check()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var svc = Make(SecurityProfile.Level3, cacheProvider: "Memory", dataProtectionKey: key);
        var result = await svc.RunAsync();

        var redisProvider = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckRedisProvider);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, redisProvider.Status);
        Assert.True(redisProvider.Critical);
        Assert.Contains(SecurityBaselinePrecheckConstants.CheckRedisProvider, result.CriticalFailureIds);
    }

    [Fact]
    public async Task Level3_missing_secret_key_fails()
    {
        var svc = Make(
            SecurityProfile.Level3,
            cacheProvider: "Redis",
            redisConn: "localhost:6379,password=p,ssl=true",
            dataProtectionKey: null);

        var result = await svc.RunAsync();
        var item = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckSecretProtectorKey);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, item.Status);
        Assert.True(item.Critical);
        Assert.Contains(SecurityBaselinePrecheckConstants.CheckSecretProtectorKey, result.CriticalFailureIds);
    }

    [Fact]
    public async Task Level3_short_dataprotection_key_fails_precheck()
    {
        // 少于 32 字节:预检必须 fail,不得启动后首次使用才炸
        var shortKey = Convert.ToBase64String(new byte[16]);
        var svc = Make(
            SecurityProfile.Level3,
            cacheProvider: "Redis",
            redisConn: "prod.redis:6380,password=strong-pass,ssl=true",
            dataProtectionKey: shortKey);
        var result = await svc.RunAsync();
        var item = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckSecretProtectorKey);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, item.Status);
        Assert.True(item.Critical);
    }

    [Fact]
    public async Task Level3_memory_without_secure_capabilities_fails_redis_actual()
    {
        // 类名再像 Redis 也不算:必须实现 ISecureCacheCapabilities
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = key, KeyVersion = 1 },
        };
        var cache = new AdminCacheOptions
        {
            Provider = "Redis",
            RedisConnectionString = "prod.redis:6380,password=x,ssl=true",
        };
        var env = new FakeEnv("Production");
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(
            new MapConfig(new Dictionary<string, string?>()),
            security,
            new AdminJwtOptions());
        // 只实现 ICacheProvider、不声明安全能力 → redis_actual 必须 fail
        var svc = new SecurityBaselinePrecheckService(
            accessor, security, cache, policy, env,
            users: null,
            cacheProvider: new FakeMemoryCacheProvider(),
            logger: NullLogger<SecurityBaselinePrecheckService>.Instance);

        var result = await svc.RunAsync();
        var item = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckRedisActual);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, item.Status);
        Assert.True(item.Critical);
    }

    [Fact]
    public async Task Level3_ok_path_passes_phase1_criticals()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var svc = Make(
            SecurityProfile.Level3,
            cacheProvider: "Redis",
            redisConn: "prod.redis:6380,password=strong-pass,ssl=true,abortConnect=false",
            dataProtectionKey: key,
            environment: "Production");

        var result = await svc.RunAsync();

        Assert.Equal(SecurityBaselinePrecheckConstants.CapabilityVersion, result.CapabilityVersion);
        Assert.False(result.HasCriticalFailures);
        // mfa_init 在无 users 仓储时为 warn → OverallCompliantForPhase1 仍 true(仅 fail 阻断)
        Assert.True(result.OverallCompliantForPhase1);

        Assert.All(
            result.Checks.Where(c => c.Critical),
            c => Assert.Equal(SecurityBaselineCheckStatus.Pass, c.Status));

        Assert.NotEmpty(result.UnimplementedMandates);
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "audit_retention_180d");
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "malware_scan");
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "field_crypto");
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "clientid_hmac");
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "sbom_supply_chain");
        Assert.Contains(result.UnimplementedMandates, m => m.Id == "crypto_profile_gm");

        // 落盘样本 JSON 供交付证据(脱敏)
        Directory.CreateDirectory(ScratchDir);
        var sample = new
        {
            result.CapabilityVersion,
            result.Profile,
            result.Environment,
            checks = result.Checks.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                status = c.Status,
                message = c.Message,
                remediation = c.Remediation,
                critical = c.Critical,
            }),
            unimplementedMandates = result.UnimplementedMandates.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                phase = m.Phase,
                description = m.Description,
            }),
            result.OverallCompliantForPhase1,
            note = "Phase-1 capability report only; does not claim 等保三级 certification.",
        };
        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        await File.WriteAllTextAsync(SamplePath, json);
        Assert.True(File.Exists(SamplePath));
        Assert.Contains("level3-phase1", json);
        Assert.Contains("unimplementedMandates", json);
    }

    [Fact]
    public async Task Default_profile_is_pass_not_level3_compliance_warn()
    {
        // ADR 0006:默认 Profile 不再因「未开 Level3」告警;可选安全独立开关
        var svc = Make(SecurityProfile.None, environment: "Production");
        var result = await svc.RunAsync();

        Assert.False(result.OverallCompliantForPhase1);
        Assert.False(result.HasCriticalFailures);
        var profileCheck = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckProfileLevel3);
        Assert.Equal(SecurityBaselineCheckStatus.Pass, profileCheck.Status);
        Assert.DoesNotContain("等保", profileCheck.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Level3_startup_with_missing_redis_auth_does_not_refuse_host()
    {
        // ADR 0006:历史 Profile=Level3 不再 fail-closed 阻断启动;预检仍可报告缺口。
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:Profile"] = "Level3",
                ["TenonAdmin:Security:DataProtection:Key"] = key,
                ["TenonAdmin:Cache:Provider"] = "Memory",
            },
        };

        var client = f.CreateClient();
        Assert.NotNull(client);
    }

    [Fact]
    public async Task Baseline_api_returns_structured_precheck_for_super_admin()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var r = await c.GetAsync("/api/v1/sys/security/baseline");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(SecurityBaselinePrecheckConstants.CapabilityVersion, data.GetProperty("capabilityVersion").GetString());
        Assert.True(data.TryGetProperty("checks", out var checks) && checks.GetArrayLength() > 0);
        Assert.True(data.TryGetProperty("unimplementedMandates", out var um) && um.GetArrayLength() > 0);
        Assert.True(data.TryGetProperty("overallCompliantForPhase1", out _));

        // 别名路径
        var r2 = await c.GetAsync("/api/v1/sys/level3/precheck");
        r2.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Level3_without_bound_superadmin_is_warn_not_critical()
    {
        // ADR 0006:无 InitGrant 仪式;未绑定超管仅为 warn,不 critical fail
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Security:DataProtection:Key"] = key,
                ["TenonAdmin:Security:DataProtection:KeyVersion"] = "1",
            },
            Overrides = s =>
            {
                s.Replace(ServiceDescriptor.Singleton<ISecurityProfileAccessor, AlwaysLevel3Profile>());
                foreach (var d in s.ToList())
                {
                    if (d.ServiceType != typeof(IHostedService)) continue;
                    var name = d.ImplementationType?.Name
                               ?? d.ImplementationInstance?.GetType().Name
                               ?? "";
                    if (name.Contains("SecurityStartupDiagnostic", StringComparison.Ordinal))
                        s.Remove(d);
                }
            },
        };

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var supers = await users.AsQueryable().Where(u => u.IsSuperAdmin).ToListAsync();
        foreach (var u in supers)
        {
            u.TotpEnabled = false;
            u.TotpSeedProtected = null;
            await users.UpdateAsync(u);
        }

        var precheck = scope.ServiceProvider.GetRequiredService<ISecurityBaselinePrecheckService>();
        var result = await precheck.RunAsync();
        var mfa = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckMfaInitState);
        Assert.Equal(SecurityBaselineCheckStatus.Warn, mfa.Status);
        Assert.False(mfa.Critical);
        Assert.DoesNotContain(SecurityBaselinePrecheckConstants.CheckMfaInitState, result.CriticalFailureIds);
    }

    [Fact]
    public async Task Level3_cors_without_CookieDomain_is_critical_topology_fail()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = key, KeyVersion = 1 },
            Level3 = new AdminLevel3Options
            {
                CookieDomain = null,
            },
        };
        var cache = new AdminCacheOptions
        {
            Provider = "Redis",
            RedisConnectionString = "redis://:pw@localhost:6379?ssl=true",
            RequireTls = true,
        };
        var env = new FakeEnv("Production");
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(new MapConfig(new()), security, new AdminJwtOptions());
        var api = new AdminApiOptions
        {
            Cors = new AdminCorsOptions { AllowedOrigins = ["https://admin.example.com"], AllowCredentials = true },
        };
        var svc = new SecurityBaselinePrecheckService(
            accessor, security, cache, policy, env,
            users: null,
            cacheProvider: new FakeRedisCacheProvider(),
            api: api);

        var result = await svc.RunAsync();
        var topo = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, topo.Status);
        Assert.True(topo.Critical);
    }

    [Fact]
    public async Task Level3_cors_with_CookieDomain_but_AllowCredentials_false_is_critical()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = key, KeyVersion = 1 },
            Level3 = new AdminLevel3Options
            {
                CookieDomain = ".example.com",
            },
        };
        var cache = new AdminCacheOptions
        {
            Provider = "Redis",
            RedisConnectionString = "redis://:pw@localhost:6379?ssl=true",
            RequireTls = true,
        };
        var env = new FakeEnv("Production");
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(new MapConfig(new()), security, new AdminJwtOptions());
        var api = new AdminApiOptions
        {
            Cors = new AdminCorsOptions
            {
                AllowedOrigins = ["https://admin.example.com"],
                AllowCredentials = false,
            },
        };
        var svc = new SecurityBaselinePrecheckService(
            accessor, security, cache, policy, env,
            users: null,
            cacheProvider: new FakeRedisCacheProvider(),
            api: api);

        var result = await svc.RunAsync();
        var topo = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology);
        Assert.Equal(SecurityBaselineCheckStatus.Fail, topo.Status);
        Assert.True(topo.Critical);
        Assert.Contains("AllowCredentials", topo.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Level3_cors_CookieDomain_and_AllowCredentials_pass_topology()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var security = new AdminSecurityOptions
        {
            Profile = SecurityProfile.Level3,
            DataProtection = new AdminDataProtectionOptions { Key = key, KeyVersion = 1 },
            Level3 = new AdminLevel3Options { CookieDomain = ".example.com" },
        };
        var cache = new AdminCacheOptions
        {
            Provider = "Redis",
            RedisConnectionString = "redis://:pw@localhost:6379?ssl=true",
            RequireTls = true,
        };
        var env = new FakeEnv("Production");
        var accessor = new SecurityProfileAccessor(security, env);
        var policy = new SecurityPolicyProvider(new MapConfig(new()), security, new AdminJwtOptions());
        var api = new AdminApiOptions
        {
            Cors = new AdminCorsOptions
            {
                AllowedOrigins = ["https://admin.example.com"],
                AllowCredentials = true,
            },
        };
        var svc = new SecurityBaselinePrecheckService(
            accessor, security, cache, policy, env,
            users: null,
            cacheProvider: new FakeRedisCacheProvider(),
            api: api);

        var result = await svc.RunAsync();
        var topo = result.Checks.Single(c => c.Id == SecurityBaselinePrecheckConstants.CheckCookieCsrfTopology);
        Assert.Equal(SecurityBaselineCheckStatus.Pass, topo.Status);
    }

    private sealed class AlwaysLevel3Profile : ISecurityProfileAccessor
    {
        public SecurityProfile Profile => SecurityProfile.Level3;
        public bool IsLevel3 => true;
        public bool IsProductionWithoutLevel3 => false;
    }
}
