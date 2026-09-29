using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 第三方接入卫星包装配入口(实现契约 §2)。不装本包 / 不调本方法 → 无 <c>itg_*</c> 表、无管理与开放端点、
/// 无菜单与后台任务(可选性定义)。只引用本包也不暴露端点:模块控制器标了 <c>[NonController]</c>,
/// 由 <see cref="AddTenonAdminIntegration"/> 注册的控制器特性提供者挂回。
/// <para>接线两步(与工作流包同构,缺一不可):</para>
/// <list type="number">
/// <item><see cref="AddTenonAdminIntegration"/> — <c>TryAdd</c> 注册选项与服务(消费者在它之前注册同接口即胜出),并挂回模块控制器</item>
/// <item><see cref="UseIntegration"/> — 在 <c>AddTenonAdmin(..., o =&gt; o.UseIntegration())</c> 里调用,把本程序集并入 CodeFirst 与控制器挂载</item>
/// </list>
/// 只调前一步:启动即失败(<see cref="IntegrationStartupValidator"/>)。只调后一步:内核照常建 <c>itg_*</c> 空表,
/// 但不暴露任何端点、不写菜单与后台任务——<see cref="UseIntegration"/> 只拿得到 <see cref="TenonAdminOptions"/>,
/// 无从登记启动校验。
/// </summary>
public static class IntegrationSetup
{
    /// <summary>配置节路径。</summary>
    public const string ConfigurationSection = "TenonAdmin:Integration";

    /// <summary>
    /// 把本程序集挂入内核:<c>itg_*</c> 实体参与 CodeFirst 建表、并入控制器应用部件(控制器本身由
    /// <see cref="AddTenonAdminIntegration"/> 挂回)。须在 <c>AddTenonAdmin</c> 的 configure 回调里调用(实体扫描与 ApplicationPart 只在那时发生)。
    /// </summary>
    public static TenonAdminOptions UseIntegration(this TenonAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var asm = typeof(IntegrationSetup).Assembly;
        if (!options.ApplicationAssemblies.Contains(asm))
            options.ApplicationAssemblies.Add(asm);
        return options;
    }

    /// <summary>
    /// 启用第三方接入模块 DI:绑定并校验 <see cref="IntegrationOptions"/>(非法值启动即抛),<c>TryAdd</c> 注册全部服务。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenonAdminIntegration(builder.Configuration);
    /// builder.Services.AddTenonAdmin(builder.Configuration, o => o.UseIntegration());
    /// </code>
    /// </example>
    public static IServiceCollection AddTenonAdminIntegration(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<IntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.LastOrDefault(d => d.ServiceType == typeof(IntegrationOptions))?.ImplementationInstance
                      as IntegrationOptions;
        if (options is null)
        {
            options = configuration?.GetSection(ConfigurationSection).Get<IntegrationOptions>() ?? new IntegrationOptions();
            configure?.Invoke(options);
        }
        IntegrationOptionsValidation.Validate(options);

        // 重复调用只保留第一次的非幂等注册(认证方案、MVC 约定、启动校验不能挂两遍)
        if (services.Any(d => d.ServiceType == typeof(IntegrationModuleMarker)))
            return services;
        services.AddSingleton<IntegrationModuleMarker>();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        // 启动前校验接线(漏调 UseIntegration)与实际生效的配置(含消费者前置注册的实例)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IntegrationStartupValidator>());
        // 模块控制器标了 [NonController](只引用本包时默认发现跳过它们),在这里挂回;先于或晚于 AddTenonAdmin 调用都拿到同一个部件管理器
        services.AddMvcCore().ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new IntegrationControllerFeatureProvider()));

        // ── 接入应用与凭据(G02):生成 / 校验 / 生命周期管理 ──
        services.TryAddSingleton<IOpenAppKeyGenerator, OpenAppKeyGenerator>();
        services.TryAddScoped<IOpenAppCredentialValidator, OpenAppCredentialValidator>();
        services.TryAddScoped<IIntegrationAppService, IntegrationAppService>();
        services.TryAddScoped<IOpenAppCredentialService, OpenAppCredentialService>();

        // ── 开放接口(G03):独立认证方案、授权与范围、端点清单、请求上下文、调用记录 ──
        services.AddAuthentication()
            .AddScheme<OpenAppAuthenticationOptions, OpenAppAuthenticationHandler>(OpenAppAuthenticationDefaults.Scheme, "接入应用", _ => { });
        services.Configure<MvcOptions>(o => o.Conventions.Add(new OpenApiConvention()));
        services.TryAddSingleton<IOpenApiCatalog, OpenApiCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpenApiDataScopePolicy, OrgOpenApiDataScopePolicy>());
        services.TryAddScoped<OpenApiDataScopePolicyRegistry>();
        services.TryAddScoped<IOpenAppAuthorizationService, OpenAppAuthorizationService>();
        services.TryAddSingleton<IOpenAppContext, HttpContextOpenAppContext>();
        services.TryAddScoped<IInboundLogService, InboundLogService>();
        services.TryAddScoped<OpenApiCallRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OpenApiStartupValidator>());

        // ── 开放接口约定与可观测性(G04):独立文档、限流、调用记录查询与保留清理 ──
        OpenApiDocumentSetup.AddOpenApiDocuments(services, options);
        services.TryAddScoped<IOpenAppRateLimiter, CacheOpenAppRateLimiter>();
        services.TryAddScoped<IIntegrationRetentionService, IntegrationRetentionService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, IntegrationRetentionJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, IntegrationRetentionJobSeed>());

        // ── 普通出站调用(G07):目标注册表、按目标隔离的客户端、凭据来源与施加、结果分类、调用入口与记录 ──
        services.TryAddSingleton<IOutboundTargetRegistry, OutboundTargetRegistry>();
        services.TryAddSingleton<OutboundHttpClientFactory>();
        services.TryAddSingleton<IOutboundCredentialProvider, ConfigurationOutboundCredentialProvider>();
        services.TryAddScoped<IOutboundAuthenticator, ConfiguredOutboundAuthenticator>();
        services.TryAddSingleton<IOutboundResultClassifier, DefaultOutboundResultClassifier>();
        services.TryAddScoped<IOutboundLogService, OutboundLogService>();
        services.TryAddScoped<IOutboundHttpInvoker, OutboundHttpInvoker>();
        services.TryAddScoped<OutboundAdapterServices>();

        // ── 事务内投递(G08):入队入口与适配器声明校验(适配器由消费者以 TryAddEnumerable 注册为 IDeliveryAdapter) ──
        services.TryAddScoped<IDeliveryOutbox, DeliveryOutbox>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DeliveryAdapterStartupValidator>());

        // ── 投递恢复、核对与管理(G09):投递器、人工操作、回调确认、告警出口、扫描任务与种子 ──
        services.TryAddScoped<IDeliveryDispatcher, DeliveryDispatcher>();
        services.TryAddScoped<IDeliveryAdminService, DeliveryAdminService>();
        services.TryAddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();
        services.TryAddScoped<DeliveryAlertPublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeliveryAlertSink, LoggingDeliveryAlertSink>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, IntegrationDeliveryJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, IntegrationDeliveryJobSeed>());

        // ── 管理菜单(G05/G06):系统应用下「第三方接入」目录;按钮即各管理接口权限码 ──
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, IntegrationMenuSeed>());

        return services;
    }

    /// <summary>模块已注册的标记(防止重复调用导致认证方案等非幂等注册挂两遍)。</summary>
    private sealed class IntegrationModuleMarker;
}
