using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;
using TenonAdmin.Integration;

namespace TenonAdmin.Tests;

/// <summary>第三方接入测试的共用小工具:日志捕获、时钟替换、建应用与发凭据、作用域执行与业务码断言的快捷方法。</summary>
internal static class IntegrationTestSupport
{
    /// <summary>用可拨时钟替换宿主的 <see cref="TimeProvider"/>(起点取整到秒,贴近真实时间以免影响雪花号与令牌)。</summary>
    public static MutableTime NewClock() =>
        new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public static Action<IServiceCollection> UseClock(MutableTime clock) =>
        services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));

    public static async Task<(long AppId, OpenAppCredentialIssued Issued)> CreateAppWithKeyAsync(
        IServiceProvider services,
        string code,
        OpenAppCredentialCreateInput? credential = null,
        long? ownerOrgId = null)
    {
        using var scope = services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IIntegrationAppService>();
        var creds = scope.ServiceProvider.GetRequiredService<IOpenAppCredentialService>();
        var appId = await apps.AddAsync(new IntegrationAppCreateInput { Code = code, Name = code, OwnerOrgId = ownerOrgId });
        var issued = await creds.CreateAsync(appId, credential ?? new OpenAppCredentialCreateInput());
        return (appId, issued);
    }

    public static async Task<OpenAppCredentialValidation> ValidateAsync(IServiceProvider services, string? key)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOpenAppCredentialValidator>().ValidateAsync(key);
    }

    public static async Task InScopeAsync(IServiceProvider services, Func<IServiceProvider, Task> action)
    {
        await using var scope = services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public static async Task<T> InScopeAsync<T>(IServiceProvider services, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>断言抛出指定业务码的 <see cref="AdminException"/>,并返回它供继续检查参数。</summary>
    public static async Task<AdminException> ExpectCodeAsync(int expected, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<AdminException>(action);
        Assert.Equal(expected, (int)ex.Code);
        return ex;
    }
}

/// <summary>最小日志捕获(含异常文本),用于断言秘密不进入任何日志。</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public string AllText => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
