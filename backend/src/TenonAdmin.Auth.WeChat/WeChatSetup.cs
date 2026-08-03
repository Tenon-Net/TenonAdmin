using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.WeChat;

public static class WeChatSetup
{
    public static IServiceCollection AddTenonAdminWeChatAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("TenonAdmin:ExternalAuth:WeChat").Get<WeChatAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.AppId))
            return services;
        return services.AddTenonAdminWeChatAuth(options);
    }

    public static IServiceCollection AddTenonAdminWeChatAuth(this IServiceCollection services, WeChatAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AppId) || string.IsNullOrWhiteSpace(options.AppSecret))
            throw new InvalidOperationException("启用微信登录需配置 AppId + AppSecret(TenonAdmin:ExternalAuth:WeChat)。");

        services.AddSingleton(options);
        services.AddHttpClient(WeChatExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));

        // 必须带 TImplementation=WeChatExternalAuthProvider(同 WeCom TryAddEnumerable 成法);
        // 仅 Singleton<IExternalAuthProvider>(factory) → ArgumentException,装包后 0 个 provider。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, WeChatExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(WeChatExternalAuthProvider.HttpClientName);
            return new WeChatExternalAuthProvider(
                sp.GetRequiredService<WeChatAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<WeChatExternalAuthProvider>>());
        }));
        return services;
    }
}
