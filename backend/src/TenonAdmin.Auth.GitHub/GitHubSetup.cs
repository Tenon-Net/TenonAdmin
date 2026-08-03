using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Auth.GitHub;

/// <summary>GitHub 登录装配:在 <c>AddTenonAdmin()</c> 之前调用。</summary>
public static class GitHubSetup
{
    public static IServiceCollection AddTenonAdminGitHubAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("TenonAdmin:ExternalAuth:GitHub").Get<GitHubAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.ClientId))
            return services;
        return services.AddTenonAdminGitHubAuth(options);
    }

    public static IServiceCollection AddTenonAdminGitHubAuth(this IServiceCollection services, GitHubAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("启用 GitHub 登录需配置 ClientId + ClientSecret(TenonAdmin:ExternalAuth:GitHub)。");

        services.AddSingleton(options);
        services.AddHttpClient(GitHubExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
                // 台账:GitHub API 要求有效 User-Agent;无密钥
                GitHubExternalAuthProvider.EnsureDefaultUserAgent(c);
            });

        // 必须带 TImplementation=GitHubExternalAuthProvider:TryAddEnumerable 用 impl 类型去重;
        // 仅 Singleton<IExternalAuthProvider>(factory) 会把 impl 当成接口本身 → ArgumentException。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, GitHubExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(GitHubExternalAuthProvider.HttpClientName);
            return new GitHubExternalAuthProvider(
                sp.GetRequiredService<GitHubAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<GitHubExternalAuthProvider>>());
        }));
        return services;
    }
}
