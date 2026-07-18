using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>外部登录 provider 的内置装配(设计批次 D)。内核只装内置 OIDC provider;企业微信/钉钉等由卫星包各自前置注册。</summary>
public static class ExternalAuthSetup
{
    /// <summary>
    /// 按 appsettings 的 <c>TenonAdmin:ExternalAuth:Oidc</c> 列表,每个条目注册一个 <see cref="OidcExternalAuthProvider"/> 实例。
    /// <para>用 plain <c>AddSingleton</c>(<b>非</b> <c>TryAddEnumerable</c>):多条 OIDC 都是同一 impl 类型、仅配置不同,
    /// 须保留 N 个实例(TryAddEnumerable 按 impl 类型去重会塌成 1)。消费者接自有 IdP:在 <c>AddTenonAdmin()</c> 前
    /// 自行注册 <see cref="IExternalAuthProvider"/>(用不同 Code),与内置并存,由 AuthService 按 Code 选型。</para>
    /// </summary>
    public static IServiceCollection AddExternalAuthProviders(this IServiceCollection services, AdminExternalAuthOptions options)
    {
        if (options.Oidc.Count == 0)
            return services;

        services.AddHttpClient();   // 幂等(内部 TryAdd):确保 IHttpClientFactory 可用,token 端点交换用

        foreach (var oidc in options.Oidc)
            services.AddSingleton<IExternalAuthProvider>(sp => new OidcExternalAuthProvider(
                oidc, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<OidcExternalAuthProvider>>()));

        return services;
    }
}
