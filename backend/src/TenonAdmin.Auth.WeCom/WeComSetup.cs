using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.WeCom;

/// <summary>
/// 企业微信登录装配(可选卫星包入口)。在 <c>AddTenonAdmin()</c> <b>之前</b>调用即把企业微信 provider
/// 并入 <see cref="IExternalAuthProvider"/> 集合(按 <see cref="WeComAuthOptions.Code"/> 选型,与内置 OIDC / 钉钉并存)。
/// </summary>
public static class WeComSetup
{
    /// <summary>
    /// 按配置启用企业微信登录:读 <c>TenonAdmin:ExternalAuth:WeCom</c> 节;未配 CorpId 则空操作(不点亮入口)。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenonAdminWeComAuth(builder.Configuration); // 先注册
    /// builder.Services.AddTenonAdmin(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddTenonAdminWeComAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("TenonAdmin:ExternalAuth:WeCom").Get<WeComAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.CorpId))
            return services;   // 未配置:空操作
        return services.AddTenonAdminWeComAuth(options);
    }

    /// <summary>显式用给定配置启用企业微信登录(代码侧,不看配置节)。</summary>
    public static IServiceCollection AddTenonAdminWeComAuth(this IServiceCollection services, WeComAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CorpId) || string.IsNullOrWhiteSpace(options.CorpSecret))
            throw new InvalidOperationException("启用企业微信登录需配置 CorpId + CorpSecret(TenonAdmin:ExternalAuth:WeCom)。");

        services.AddSingleton(options);
        // TryAddEnumerable 按 impl 类型去重:装两次也只有一个 WeCom provider;与内置/钉钉 provider 并存,按 Code 选型。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, WeComExternalAuthProvider>());
        return services;
    }
}
