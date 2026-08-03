using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.DingTalk;

/// <summary>
/// 钉钉登录装配(可选卫星包入口)。在 <c>AddTenonAdmin()</c> <b>之前</b>调用即把钉钉 provider
/// 并入 <see cref="IExternalAuthProvider"/> 集合(按 <see cref="DingTalkAuthOptions.Code"/> 选型,与内置 OIDC / 企业微信并存)。
/// </summary>
public static class DingTalkSetup
{
    /// <summary>
    /// 按配置启用钉钉登录:读 <c>TenonAdmin:ExternalAuth:DingTalk</c> 节;未配 AppKey 则空操作(不点亮入口)。
    /// </summary>
    public static IServiceCollection AddTenonAdminDingTalkAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("TenonAdmin:ExternalAuth:DingTalk").Get<DingTalkAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.AppKey))
            return services;
        return services.AddTenonAdminDingTalkAuth(options);
    }

    /// <summary>显式用给定配置启用钉钉登录(代码侧,不看配置节)。</summary>
    public static IServiceCollection AddTenonAdminDingTalkAuth(this IServiceCollection services, DingTalkAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AppKey) || string.IsNullOrWhiteSpace(options.AppSecret))
            throw new InvalidOperationException("启用钉钉登录需配置 AppKey + AppSecret(TenonAdmin:ExternalAuth:DingTalk)。");

        services.AddSingleton(options);
        services.AddHttpClient(DingTalkExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));

        // 与 GitHub 相同:TryAddEnumerable + TImplementation,工厂注入命名 HttpClient
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, DingTalkExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(DingTalkExternalAuthProvider.HttpClientName);
            return new DingTalkExternalAuthProvider(
                sp.GetRequiredService<DingTalkAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<DingTalkExternalAuthProvider>>());
        }));
        return services;
    }
}
