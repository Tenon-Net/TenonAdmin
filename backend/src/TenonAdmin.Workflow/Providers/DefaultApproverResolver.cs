using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 默认审批人解析:在当前 scope 内按 <see cref="IApproverProvider.Key"/> Ordinal 匹配;
/// 同键多实现时取先注册者(消费者前置注册即可覆盖内置)。找不到抛
    /// <see cref="AdminException"/>。整体替换本类则前置 <c>TryAdd</c>
/// <see cref="IApproverResolver"/>。
/// </summary>
public class DefaultApproverResolver(IServiceProvider services) : IApproverResolver
{
    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<long>> ResolveAsync(
        string providerKey,
        ApproverResolveContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ProviderNotRegistered);
        ArgumentNullException.ThrowIfNull(context);

        var provider = services.GetServices<IApproverProvider>()
            .FirstOrDefault(p => string.Equals(p.Key, providerKey, StringComparison.Ordinal));
        if (provider is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ProviderNotRegistered,
                new Dictionary<string, object?> { ["provider"] = providerKey });

        return await provider.ResolveAsync(context, cancellationToken);
    }
}
