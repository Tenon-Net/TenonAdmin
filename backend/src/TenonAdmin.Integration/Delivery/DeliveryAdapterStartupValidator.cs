using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TenonAdmin.Integration;

/// <summary>投递适配器的启动校验:名称合法且不区分大小写唯一,否则拒绝启动(重名会让记录被投给错误的对方)。</summary>
internal sealed partial class DeliveryAdapterStartupValidator(IServiceProvider services) : IHostedService
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex NamePattern();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var names = scope.ServiceProvider.GetServices<IDeliveryAdapter>().Select(a => a.Name).ToList();
        var errors = names.Where(n => n is null || !NamePattern().IsMatch(n)).Select(n => $"适配器名「{n}」须为 1–64 位字母、数字及 ._-").ToList();
        errors.AddRange(names.Where(n => n is not null).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => $"适配器名「{g.Key}」重复注册"));
        if (errors.Count > 0)
            throw new InvalidOperationException("可靠投递适配器声明不合法,拒绝启动:\n- " + string.Join("\n- ", errors));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
