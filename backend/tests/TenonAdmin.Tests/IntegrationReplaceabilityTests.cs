using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.Tests;

/// <summary>
/// 第三方接入卫星包的可替换性契约:<see cref="IntegrationSetup.AddTenonAdminIntegration"/> 里每个以 <c>TryAdd</c> 注册的 SPI 接口与选项,
/// 消费者<strong>前置</strong>注册即胜出(裸容器,不走 ConfigureTestServices+Replace)。把任一 TryAdd 退化成 Add → 对应用例红。
/// <c>TryAddEnumerable</c> 的扩展点(范围策略、告警出口等)是追加而非替换,不在此列。
/// </summary>
public class IntegrationReplaceabilityTests
{
    [Fact]
    public async Task PreRegisteredCredentialValidator_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IOpenAppCredentialValidator, AcceptNothingValidator>());
        await using var scope = sp.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IOpenAppCredentialValidator>();
        Assert.IsType<AcceptNothingValidator>(validator);
        Assert.Equal(OpenAppCredentialFailure.NotFound, (await validator.ValidateAsync("anything")).Failure);
    }

    [Fact]
    public void PreRegisteredKeyGenerator_ShouldWinOverBuiltIn()
    {
        using var sp = BuildProvider(s => s.AddSingleton<IOpenAppKeyGenerator, PepperedKeyGenerator>());
        Assert.IsType<PepperedKeyGenerator>(sp.GetRequiredService<IOpenAppKeyGenerator>());
    }

    [Fact]
    public async Task PreRegisteredAppService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IIntegrationAppService, FakeAppService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeAppService>(scope.ServiceProvider.GetRequiredService<IIntegrationAppService>());
    }

    [Fact]
    public async Task PreRegisteredCredentialService_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s => s.AddScoped<IOpenAppCredentialService, FakeCredentialService>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeCredentialService>(scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>());
    }

    [Fact]
    public async Task PreRegisteredOpenApiServices_ShouldWinOverBuiltIn()
    {
        var catalog = new FakeCatalog();
        var context = new FakeOpenAppContext();
        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton<IOpenApiCatalog>(catalog);
            s.AddSingleton<IOpenAppContext>(context);
            s.AddScoped<IOpenAppAuthorizationService, FakeAuthorizationService>();
            s.AddScoped<IInboundLogService, FakeInboundLogService>();
            s.AddScoped<IOpenAppRateLimiter, FakeRateLimiter>();
            s.AddScoped<IIntegrationRetentionService, FakeRetention>();
        });
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        Assert.Same(catalog, provider.GetRequiredService<IOpenApiCatalog>());
        Assert.Same(context, provider.GetRequiredService<IOpenAppContext>());
        Assert.IsType<FakeAuthorizationService>(provider.GetRequiredService<IOpenAppAuthorizationService>());
        Assert.IsType<FakeInboundLogService>(provider.GetRequiredService<IInboundLogService>());
        Assert.IsType<FakeRateLimiter>(provider.GetRequiredService<IOpenAppRateLimiter>());
        Assert.IsType<FakeRetention>(provider.GetRequiredService<IIntegrationRetentionService>());
    }

    [Fact]
    public async Task PreRegisteredOutboundServices_ShouldWinOverBuiltIn()
    {
        var registry = new FakeTargetRegistry();
        var credentials = new FakeCredentialProvider();
        var classifier = new FakeClassifier();
        var factory = new OutboundHttpClientFactory();
        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton<IOutboundTargetRegistry>(registry);
            s.AddSingleton<IOutboundCredentialProvider>(credentials);
            s.AddSingleton<IOutboundResultClassifier>(classifier);
            s.AddSingleton(factory);
            s.AddScoped<IOutboundAuthenticator, FakeAuthenticator>();
            s.AddScoped<IOutboundLogService, FakeOutboundLogService>();
            s.AddScoped<IOutboundHttpInvoker, FakeInvoker>();
        });
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        Assert.Same(registry, provider.GetRequiredService<IOutboundTargetRegistry>());
        Assert.Same(credentials, provider.GetRequiredService<IOutboundCredentialProvider>());
        Assert.Same(classifier, provider.GetRequiredService<IOutboundResultClassifier>());
        Assert.Same(factory, provider.GetRequiredService<OutboundHttpClientFactory>());
        Assert.IsType<FakeAuthenticator>(provider.GetRequiredService<IOutboundAuthenticator>());
        Assert.IsType<FakeOutboundLogService>(provider.GetRequiredService<IOutboundLogService>());
        Assert.IsType<FakeInvoker>(provider.GetRequiredService<IOutboundHttpInvoker>());

        // 适配器依赖聚合拿到的也是消费者的实现
        var adapterServices = provider.GetRequiredService<OutboundAdapterServices>();
        Assert.IsType<FakeInvoker>(adapterServices.Invoker);
        Assert.Same(credentials, adapterServices.Credentials);
    }

    [Fact]
    public async Task PreRegisteredDeliveryServices_ShouldWinOverBuiltIn()
    {
        await using var sp = BuildProvider(s =>
        {
            s.AddScoped<IDeliveryOutbox, FakeOutbox>();
            s.AddScoped<IDeliveryDispatcher, FakeDispatcher>();
            s.AddScoped<IDeliveryAdminService, FakeDeliveryAdmin>();
            s.AddScoped<IDeliveryConfirmationService, FakeConfirmation>();
        });
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeOutbox>(scope.ServiceProvider.GetRequiredService<IDeliveryOutbox>());
        Assert.IsType<FakeDispatcher>(scope.ServiceProvider.GetRequiredService<IDeliveryDispatcher>());
        Assert.IsType<FakeDeliveryAdmin>(scope.ServiceProvider.GetRequiredService<IDeliveryAdminService>());
        Assert.IsType<FakeConfirmation>(scope.ServiceProvider.GetRequiredService<IDeliveryConfirmationService>());
    }

    [Fact]
    public void PreRegisteredOptions_ShouldWinOverConfigurationBinding()
    {
        var mine = new IntegrationOptions
        {
            Credentials = { MaxActivePerApp = 9 },
            OpenApi = { Versions = ["v2"] },
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TenonAdmin:Integration:Credentials:MaxActivePerApp"] = "0" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(mine);
        services.AddTenonAdminIntegration(config, o => o.Credentials.MaxActivePerApp = 0);
        using var sp = services.BuildServiceProvider();
        Assert.Same(mine, sp.GetRequiredService<IntegrationOptions>());
        Assert.Equal(9, mine.Credentials.MaxActivePerApp);
        Assert.Contains(services, d => d.ServiceType == typeof(IOpenApiDocumentProvider) && Equals(d.ServiceKey, "open-v2"));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IOpenApiDocumentProvider) && Equals(d.ServiceKey, "open-v1"));
    }

    [Fact]
    public void Open_document_requires_the_open_marker_in_addition_to_the_group_name()
    {
        using var sp = BuildProvider(_ => { });
        var include = sp.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get("open-v1").ShouldInclude;
        var polluted = new ApiDescription
        {
            GroupName = "open-v1",
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [] },
        };
        var open = new ApiDescription
        {
            GroupName = "open-v1",
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [new OpenApiAttribute()] },
        };

        Assert.False(include(polluted));
        Assert.True(include(open));
    }

    [Fact]
    public void Invalid_configuration_fails_fast_at_registration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TenonAdmin:Integration:Credentials:MaxActivePerApp"] = "0" })
            .Build();
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTenonAdminIntegration(config));
        Assert.Contains("MaxActivePerApp", ex.Message);

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTenonAdminIntegration(
            configure: o => o.Credentials.DefaultRotationOverlapHours = 1000));
    }

    [Fact]
    public async Task Host_refuses_to_start_when_UseIntegration_is_missing()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new TenonAdminOptions());   // AddTenonAdmin 登记的同一实例,但回调里没调 UseIntegration
        builder.Services.AddTenonAdminIntegration();
        using var host = builder.Build();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        Assert.Contains("UseIntegration", ex.ToString());
    }

    [Fact]
    public void Invalid_pre_registered_options_instance_fails_at_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TenonAdminOptions().UseIntegration());
        services.AddSingleton(new IntegrationOptions { Credentials = { MaxActivePerApp = 0 } });
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTenonAdminIntegration());
        Assert.Contains("MaxActivePerApp", ex.ToString());
    }

    [Fact]
    public void Pre_registered_options_factory_with_the_registered_version_uses_the_registered_document()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IntegrationOptions>(_ => new IntegrationOptions());
        services.AddTenonAdminIntegration();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(["v1"], provider.GetRequiredService<IntegrationOptions>().OpenApi.Versions);
        Assert.True(provider.GetRequiredService<IServiceProviderIsKeyedService>()
            .IsKeyedService(typeof(IOpenApiDocumentProvider), "open-v1"));
    }

    [Fact]
    public async Task Pre_registered_options_factory_cannot_add_an_unregistered_document_version()
    {
        var admin = new TenonAdminOptions();
        admin.UseIntegration();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(admin);
        builder.Services.AddSingleton<IntegrationOptions>(_ => new IntegrationOptions { OpenApi = { Versions = ["v2"] } });
        builder.Services.AddTenonAdminIntegration();
        using var host = builder.Build();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        Assert.Contains("open-v2", ex.ToString());
        Assert.Contains("configure", ex.ToString());
    }

    [Fact]
    public void Built_in_services_keep_virtual_steps_for_subclassing()
    {
        // HttpContextOpenAppContext 刻意 sealed(只读请求状态的薄载体),整体替换走 IOpenAppContext
        foreach (var type in new[]
        {
            typeof(OpenAppCredentialValidator), typeof(IntegrationAppService), typeof(OpenAppCredentialService), typeof(OpenAppKeyGenerator),
            typeof(OpenApiCatalog), typeof(OpenAppAuthorizationService), typeof(InboundLogService), typeof(CacheOpenAppRateLimiter),
            typeof(IntegrationRetentionService),
            typeof(OutboundHttpInvoker), typeof(ConfiguredOutboundAuthenticator), typeof(ConfigurationOutboundCredentialProvider),
            typeof(DefaultOutboundResultClassifier), typeof(OutboundLogService), typeof(OutboundTargetRegistry),
            typeof(DeliveryOutbox), typeof(DeliveryDispatcher), typeof(DeliveryAdminService), typeof(DeliveryConfirmationService),
            typeof(DeliveryAlertPublisher), typeof(LoggingDeliveryAlertSink),
        })
        {
            Assert.False(type.IsSealed, $"{type.Name} 不应 sealed");
            var publicInstance = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);
            Assert.All(publicInstance, m => Assert.True(m.IsVirtual, $"{type.Name}.{m.Name} 应为 virtual"));
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> preRegister)
    {
        var services = new ServiceCollection();
        preRegister(services);
        services.AddTenonAdminIntegration();
        return services.BuildServiceProvider();
    }

    private sealed class AcceptNothingValidator : IOpenAppCredentialValidator
    {
        public Task<OpenAppCredentialValidation> ValidateAsync(string? presentedKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.NotFound));
    }

    private sealed class PepperedKeyGenerator : OpenAppKeyGenerator
    {
        public override string ComputeSecretHash(string secret) => base.ComputeSecretHash("pepper:" + secret);
    }

    private sealed class FakeAppService : IIntegrationAppService
    {
        public Task<PagedList<IntegrationAppListItem>> PageAsync(IntegrationAppPageInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IntegrationAppDetail> GetAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> AddAsync(IntegrationAppCreateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(long id, IntegrationAppUpdateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCredentialService : IOpenAppCredentialService
    {
        public Task<IReadOnlyList<OpenAppCredentialView>> ListAsync(long appId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OpenAppCredentialIssued> CreateAsync(long appId, OpenAppCredentialCreateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OpenAppCredentialIssued> RotateAsync(long appId, long credentialId, OpenAppCredentialRotateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeAsync(long appId, long credentialId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(long appId, long credentialId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetExpiryAsync(long appId, long credentialId, OpenAppCredentialExpiryInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCatalog : IOpenApiCatalog
    {
        public IReadOnlyList<OpenApiEndpointInfo> Endpoints => [];
        public OpenApiEndpointInfo? Find(ActionDescriptor action, string httpMethod) => null;
        public bool Contains(string permission) => false;
    }

    private sealed class FakeOpenAppContext : IOpenAppContext
    {
        public bool IsOpenAppRequest => false;
        public OpenAppIdentity Identity => throw new NotSupportedException();
        public OpenAppDataScope DataScope => throw new NotSupportedException();
        public OpenApiEndpointInfo Endpoint => throw new NotSupportedException();
    }

    private sealed class FakeAuthorizationService : IOpenAppAuthorizationService
    {
        public Task<OpenAppGrantView> GetGrantsAsync(long appId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetGrantsAsync(long appId, OpenAppGrantInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OpenAppScopeBindingView>> GetScopesAsync(long appId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetScopesAsync(long appId, OpenAppScopeInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OpenAppAuthorizationSnapshot> GetSnapshotAsync(OpenAppIdentity identity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeInboundLogService : IInboundLogService
    {
        public Task RecordAsync(InboundLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedList<InboundLogView>> PageAsync(InboundLogPageInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRateLimiter : IOpenAppRateLimiter
    {
        public Task<OpenAppRateDecision> AcquireAsync(long appId, int limitPerMinute, CancellationToken cancellationToken = default) => Task.FromResult(OpenAppRateDecision.Allow);
        public Task<OpenAppRateDecision> CheckAuthenticationAsync(string clientKey, CancellationToken cancellationToken = default) => Task.FromResult(OpenAppRateDecision.Allow);
        public Task RecordAuthenticationFailureAsync(string clientKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeRetention : IIntegrationRetentionService
    {
        public Task<IntegrationRetentionResult> CleanupAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IntegrationRetentionResult());
    }

    private sealed class FakeTargetRegistry : IOutboundTargetRegistry
    {
        public IReadOnlyList<OutboundTarget> Targets => [];
        public OutboundTarget? Find(string name) => null;
    }

    private sealed class FakeCredentialProvider : IOutboundCredentialProvider
    {
        public ValueTask<OutboundCredential?> GetAsync(OutboundTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OutboundCredential?>(null);
    }

    private sealed class FakeClassifier : IOutboundResultClassifier
    {
        public OutboundClassification Classify(OutboundHttpResult result) => new(OutboundOutcome.Unknown);
    }

    private sealed class FakeAuthenticator : IOutboundAuthenticator
    {
        public ValueTask ApplyAsync(HttpRequestMessage request, OutboundTarget target, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeOutboundLogService : IOutboundLogService
    {
        public Task RecordAsync(OutboundLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedList<OutboundLogView>> PageAsync(OutboundLogPageInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeInvoker : IOutboundHttpInvoker
    {
        public Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeOutbox : IDeliveryOutbox
    {
        public Task<DeliveryEnqueueResult> EnqueueAsync(DeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDispatcher : IDeliveryDispatcher
    {
        public Task<DeliveryDispatchSummary> RunOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DeliveryDispatchSummary());
    }

    private sealed class FakeDeliveryAdmin : IDeliveryAdminService
    {
        public Task<PagedList<DeliveryListItem>> PageAsync(DeliveryPageInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryDetail> GetAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeliveryStatusCount>> SummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryDetail> ExecuteAsync(long id, string action, DeliveryActionInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeConfirmation : IDeliveryConfirmationService
    {
        public Task<DeliveryConfirmOutcome> ConfirmAsync(DeliveryConfirmation confirmation, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeliveryConfirmOutcome.NotFound);
    }
}
