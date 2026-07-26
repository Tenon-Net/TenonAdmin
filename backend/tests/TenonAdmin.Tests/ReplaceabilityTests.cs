using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

namespace TenonAdmin.Tests;

/// <summary>
/// 可替换机制回归锁(§8 产品承诺,用例名照设计写死)。证明:框架服务可替换、鉴权步骤可覆写、
/// 模块可禁用、用户控制器/种子/实体即插即用。
/// </summary>
public class ReplaceabilityTests
{
    [Fact]
    public void ReplaceService_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IPasswordHasher, FakeHasher>()),
        };
        Assert.IsType<FakeHasher>(f.Services.GetRequiredService<IPasswordHasher>());
    }

    [Fact]
    public void ReplaceSmsSender_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<ISmsSender, FakeSmsSender>()),
        };
        Assert.IsType<FakeSmsSender>(f.Services.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void ReplaceEmailSender_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IEmailSender, FakeEmailSender>()),
        };
        Assert.IsType<FakeEmailSender>(f.Services.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void ReplaceRealtimePublisher_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IRealtimePublisher, FakeRealtimePublisher>()),
        };
        Assert.IsType<FakeRealtimePublisher>(f.Services.GetRequiredService<IRealtimePublisher>());
    }

    // ── scheduling-ledger §12:定时任务的可替换面 ─────────────────────────

    /// <summary>
    /// 定时任务三件套的「<b>前置</b>注册即胜出」——这才是 TryAdd 契约的真判据。
    /// <para>注意别照抄本文件其它用例的 <c>Overrides = s => s.Replace(...)</c> 写法来测 TryAdd:那是
    /// <c>ConfigureTestServices</c>,跑在 <c>AddTenonAdmin</c> <b>之后</b>,TryAdd 改成 Add 它照样绿——
    /// 它证明的是"可替换",不是"TryAdd 注册"。故此处直接在裸容器里前置注册再调 <c>AddTenonAdminServices()</c>。</para>
    /// <para>变异:把 ServicesSetup 里这三行任一的 TryAdd 改成 Add → 内置实现后注册即胜出 → 本条红。</para>
    /// </summary>
    [Fact]
    public async Task PreRegisteredJobServices_ShouldWinOverBuiltIns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(new AdminIdOptions());
        services.AddSingleton(new AdminJobsOptions());
        // 消费者的前置注册(在 AddTenonAdmin* 之前)
        services.AddScoped<IJobService, FakeJobService>();
        services.AddSingleton<IJobHandlerResolver, FakeJobHandlerResolver>();
        services.AddSingleton<JobSchedulerService, SubclassedScheduler>();

        var dbOptions = new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "DataSource=:memory:" };
        services.AddSingleton(dbOptions);
        services.AddTenonAdminSqlSugar(dbOptions, [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();

        // 容器必须异步释放:ChannelEventBus 只实现 IAsyncDisposable,同步 Dispose 会直接抛
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeJobService>(scope.ServiceProvider.GetRequiredService<IJobService>());
        Assert.IsType<FakeJobHandlerResolver>(sp.GetRequiredService<IJobHandlerResolver>());
        Assert.IsType<SubclassedScheduler>(sp.GetRequiredService<JobSchedulerService>());
    }

    /// <summary>消费者自己的 IAdminJob(TestHost 的 SampleJob)必须被默认解析器认得,并出现在处理器清单里。</summary>
    [Fact]
    public async Task ConsumerJobHandler_ShouldBeResolvableAndListed()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();

        var resolver = f.Services.GetRequiredService<IJobHandlerResolver>();
        var handler = await resolver.ResolveAsync(typeof(SampleJob).FullName!, scope.ServiceProvider);
        Assert.IsType<SampleJob>(handler);

        var listed = scope.ServiceProvider.GetRequiredService<IJobService>().ListHandlers();
        Assert.Contains(typeof(SampleJob).FullName, listed);
    }

    // ── excel-ledger §9 G5 六件套:导入导出可替换接口各一条 ─────────────────

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelReader 的 TryAdd 改成 Add(覆盖消费者) → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelReader_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelReader, FakeExcelReader>()),
        };
        Assert.IsType<FakeExcelReader>(f.Services.GetRequiredService<IExcelReader>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelWriter 的 TryAdd 改成 Add → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelWriter_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelWriter, FakeExcelWriter>()),
        };
        Assert.IsType<FakeExcelWriter>(f.Services.GetRequiredService<IExcelWriter>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelTemplateBuilder 的 TryAdd 改成 Add → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelTemplateBuilder_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelTemplateBuilder, FakeExcelTemplateBuilder>()),
        };
        Assert.IsType<FakeExcelTemplateBuilder>(f.Services.GetRequiredService<IExcelTemplateBuilder>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IImportRunner 的 TryAdd 改成 Add → 解析到 ImportRunner → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceImportRunner_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IImportRunner, FakeImportRunner>()),
        };
        using var scope = f.Services.CreateScope();
        Assert.IsType<FakeImportRunner>(scope.ServiceProvider.GetRequiredService<IImportRunner>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IDictTextResolver 的 TryAdd 改成 Add → 解析到 DictTextResolver → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceDictTextResolver_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IDictTextResolver, FakeDictTextResolver>()),
        };
        using var scope = f.Services.CreateScope();
        Assert.IsType<FakeDictTextResolver>(scope.ServiceProvider.GetRequiredService<IDictTextResolver>());
    }

    [Fact]
    public async Task OverrideAuthStep_ShouldAffectLoginFlow()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IAuthService, OverridingAuthService>()),
        };
        var j = await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal("OVERRIDDEN", j.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public void ExternalAuthProvider_ShouldBePluggable()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.AddSingleton<IExternalAuthProvider>(new FakeExternalAuthProvider()),
        };
        // provider 是加法式扩展(TryAddEnumerable/AddSingleton 多实现按 Code 选型),消费者前置注册即并入集合
        Assert.Contains(f.Services.GetServices<IExternalAuthProvider>(), p => p.Code == "fake");
    }

    [Fact]
    public async Task DisabledModule_ShouldRemoveBuiltInController()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Upload"] };
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/sys/file/page")).StatusCode);       // 已禁 → 摘除
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/sys/user/page")).StatusCode);   // 未禁 → 仍在(需认证)
    }

    [Fact]
    public async Task CustomController_ShouldOwnSameRouteAfterModuleDisabled()
    {
        using var f = new AdminAppFactory();   // 默认禁内置 Dict → TestHost 的 CustomDictController 接管其路由
        var r = await f.CreateClient().GetAsync("/api/v1/sys/dict/type/page");
        r.EnsureSuccessStatusCode();
        var j = await r.ReadEnvelope();
        Assert.Equal("custom-dict", j.GetProperty("data").GetProperty("source").GetString());
    }

    [Fact]
    public async Task CustomSeedData_ShouldRunOnceAndBeIdempotent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tenon-seed-{Guid.NewGuid():N}.db");
        int first, second;
        using (var f1 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false })
        {
            _ = f1.CreateClient();                 // 触发宿主启动 → 种子运行
            first = await CountWidgets(f1);
        }
        using (var f2 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false })
        {
            _ = f2.CreateClient();                 // 同库二次启动 → 种子应幂等
            second = await CountWidgets(f2);
        }
        try { File.Delete(dbPath); } catch { /* 尽力而为 */ }

        Assert.Equal(2, first);    // 首启插入 2 行
        Assert.Equal(2, second);   // 二启仍 2 行(未重复插入)
    }

    private static async Task<int> CountWidgets(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepository<SampleWidget>>().AsQueryable().CountAsync();
    }

    /// <summary>用户自定义短信通道(替换框架默认日志通道)</summary>
    private sealed class FakeSmsSender : ISmsSender
    {
        public Task SendCodeAsync(string phone, string code, string purpose, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义邮件通道(替换框架默认日志/SMTP 通道)</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义实时推送通道(替换框架默认空实现 / 内置 SignalR)</summary>
    private sealed class FakeRealtimePublisher : IRealtimePublisher
    {
        public Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义外部登录 provider(接自有 IdP,按 Code 并入 provider 集合)</summary>
    private sealed class FakeExternalAuthProvider : IExternalAuthProvider
    {
        public string Code => "fake";
        public string DisplayName => "Fake";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult("https://idp.test/authorize");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalIdentity("fake", "sub"));
    }

    /// <summary>用户自定义密码哈希(替换框架默认 PBKDF2)</summary>
    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "FAKE:" + password;
        public bool Verify(string password, string hash) => hash == "FAKE:" + password;
    }

    /// <summary>用户自定义 xlsx 读取(替换 MissingExcelProvider / MiniExcelReader)</summary>
    private sealed class FakeExcelReader : IExcelReader
    {
        public Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public async IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
            Stream file, IReadOnlyDictionary<string, string> headerToKey,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>用户自定义 xlsx 写出</summary>
    private sealed class FakeExcelWriter : IExcelWriter
    {
        public Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>用户自定义模板构建</summary>
    private sealed class FakeExcelTemplateBuilder : IExcelTemplateBuilder
    {
        public Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>用户自定义导入编排(替换 ImportRunner)</summary>
    private sealed class FakeImportRunner : IImportRunner
    {
        public Task<ImportPreview> PreviewAsync(Stream file, IReadOnlyDictionary<string, string>? mapping,
            IImportProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportPreview());
        public Task<ImportPreview> ValidateAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportPreview());
        public Task<ImportCommitResult> CommitAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
            DuplicateStrategy strategy, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportCommitResult());
    }

    /// <summary>用户自定义字典 label↔value(替换 DictTextResolver)</summary>
    private sealed class FakeDictTextResolver : IDictTextResolver
    {
        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetItemsAsync(
            string dictTypeCode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
        public Task<string?> ToLabelAsync(string dictTypeCode, string? value, CancellationToken cancellationToken = default)
            => Task.FromResult(value);
        public Task<string?> ToValueAsync(string dictTypeCode, string? label, CancellationToken cancellationToken = default)
            => Task.FromResult(label);
    }

    /// <summary>用户自定义任务管理服务(替换 JobService)</summary>
    private sealed class FakeJobService : IJobService
    {
        public Task<PagedList<SysJob>> PageAsync(JobPageInput input) => throw new NotSupportedException();
        public Task<long> AddAsync(JobInput input) => throw new NotSupportedException();
        public Task UpdateAsync(long id, JobInput input) => throw new NotSupportedException();
        public Task DeleteAsync(long id) => throw new NotSupportedException();
        public Task DeleteBatchAsync(IReadOnlyCollection<long> ids) => throw new NotSupportedException();
        public Task SetEnabledAsync(long id, bool enabled) => throw new NotSupportedException();
        public Task RunOnceAsync(long id) => throw new NotSupportedException();
        public CronPreviewOutput PreviewCron(CronPreviewInput input) => new();
        public IReadOnlyList<string> ListHandlers() => [];
        public Task<JobDashboardOutput> GetDashboardAsync() => Task.FromResult(new JobDashboardOutput());
    }

    /// <summary>用户自定义处理器解析策略(替换 DefaultJobHandlerResolver)</summary>
    private sealed class FakeJobHandlerResolver : IJobHandlerResolver
    {
        public Task<IAdminJob?> ResolveAsync(string handlerName, IServiceProvider scopedProvider, CancellationToken cancellationToken = default)
            => Task.FromResult<IAdminJob?>(null);
    }

    /// <summary>用户子类化调度器(证明 TryAddSingleton + 双注册下托管的也是子类实例)</summary>
    private sealed class SubclassedScheduler(
        ISqlSugarClient db, JobExecutor executor, IEventBus eventBus, AdminJobsOptions options,
        AdminIdOptions idOptions, AdminDatabaseOptions dbOptions, IIdGenerator idGenerator, TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<JobSchedulerService> logger)
        : JobSchedulerService(db, executor, eventBus, options, idOptions, dbOptions, idGenerator, time, logger);

    /// <summary>用户覆写登录出参组装步骤(模板方法覆写,§5.3)</summary>
    private sealed class OverridingAuthService(
        IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens, ISessionService sessions,
        ILogService logService, ILoginLockService loginLock, ICaptchaService captcha, ISecurityPolicyProvider policy,
        ISmsOtpService smsOtp)
        : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
    {
        protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
            base.BuildLoginOutput(user, pair) with { Name = "OVERRIDDEN" };
    }
}
