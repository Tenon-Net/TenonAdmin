using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.OpenApi;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 模块接线与配置的启动校验:在所有托管服务启动之前执行(<see cref="IHostedLifecycleService.StartingAsync"/>,先于内核建表与种子),
/// 不满足即拒绝启动。
/// <list type="bullet">
/// <item>调用了 <see cref="IntegrationSetup.AddTenonAdminIntegration"/>,却没在 <c>AddTenonAdmin</c> 的回调里调
/// <see cref="IntegrationSetup.UseIntegration"/>:<c>itg_*</c> 表不会建立,菜单与后台任务种子却照样写入;</item>
/// <item>容器里实际生效的 <see cref="IntegrationOptions"/> 按注册时同一规则再校验一遍;
/// 工厂前置注册无法在 DI 组装期求值,因此还会校验其文档版本是否已注册。</item>
/// </list>
/// 反方向(只调 <see cref="IntegrationSetup.UseIntegration"/>)在这里无从发现:本校验由 <see cref="IntegrationSetup.AddTenonAdminIntegration"/>
/// 登记,而 <c>UseIntegration</c> 只拿得到 <see cref="TenonAdminOptions"/>。那种接法不暴露端点、不写菜单与任务,只多出 <c>itg_*</c> 空表。
/// </summary>
internal sealed class IntegrationStartupValidator(IServiceProvider services, IntegrationOptions options) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var admin = services.GetService<TenonAdminOptions>();
        if (admin is null || !admin.ApplicationAssemblies.Contains(typeof(IntegrationSetup).Assembly))
            throw new InvalidOperationException(
                "已调用 AddTenonAdminIntegration,但没有在 AddTenonAdmin(..., o => o.UseIntegration()) 里挂入第三方接入模块:" +
                "itg_* 表不会建立、管理接口不会挂载,菜单与后台任务种子却会照样写入。请在 AddTenonAdmin 的配置回调里调用 UseIntegration()。");
        IntegrationOptionsValidation.Validate(options);
        var keyedServices = services.GetRequiredService<IServiceProviderIsKeyedService>();
        foreach (var version in options.OpenApi.Versions)
        {
            var documentName = OpenApiDocumentSetup.DocumentName(version);
            if (!keyedServices.IsKeyedService(typeof(IOpenApiDocumentProvider), documentName))
                throw new InvalidOperationException(
                    $"实际生效的 IntegrationOptions 声明了开放文档 {documentName},但未注册对应文档服务。" +
                    "OpenApi:Versions 须通过 AddTenonAdminIntegration 的 configure 参数或前置 IntegrationOptions 实例声明。");
        }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
