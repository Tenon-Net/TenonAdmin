namespace TenonAdmin.Integration;

/// <summary>
/// 范围策略注册表:按键(不区分大小写)取策略;同键多个实现时<b>先注册者生效</b>——消费者在
/// <c>AddTenonAdminIntegration</c> 之前注册同键策略即可覆盖内置 <c>org</c>。保留键 <see cref="OpenApiDataScopes.None"/> 不可注册。
/// </summary>
public class OpenApiDataScopePolicyRegistry
{
    private readonly Dictionary<string, IOpenApiDataScopePolicy> _byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>由 DI 注入全部已注册策略。</summary>
    public OpenApiDataScopePolicyRegistry(IEnumerable<IOpenApiDataScopePolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        foreach (var policy in policies)
        {
            var key = policy.Key?.Trim();
            if (string.IsNullOrEmpty(key) || string.Equals(key, OpenApiDataScopes.None, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"范围策略 {policy.GetType().FullName} 的 Key 为空或使用了保留键 \"{OpenApiDataScopes.None}\"。");
            _byKey.TryAdd(key, policy);
        }
    }

    /// <summary>全部生效策略。</summary>
    public virtual IReadOnlyCollection<IOpenApiDataScopePolicy> Policies => _byKey.Values;

    /// <summary>按键取策略;不存在返回 null。</summary>
    public virtual IOpenApiDataScopePolicy? Find(string? key) =>
        key is not null && _byKey.TryGetValue(key.Trim(), out var policy) ? policy : null;

    /// <summary>键是否可被端点声明(已注册策略或 <see cref="OpenApiDataScopes.None"/>)。</summary>
    public virtual bool IsDeclarable(string? key) =>
        string.Equals(key, OpenApiDataScopes.None, StringComparison.Ordinal) || Find(key) is not null;
}
