namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOutboundTargetRegistry"/> 默认实现:由启动时已校验的 <see cref="IntegrationOutboundOptions.Targets"/> 构建(单例)。
/// 基础地址统一补齐结尾 <c>/</c>,使相对路径总是拼在它后面,而不是替换掉最后一段。
/// </summary>
public class OutboundTargetRegistry : IOutboundTargetRegistry
{
    private readonly Dictionary<string, OutboundTarget> _targets;

    /// <summary>从模块配置构建。</summary>
    public OutboundTargetRegistry(IntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var outbound = options.Outbound;
        _targets = new Dictionary<string, OutboundTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, target) in outbound.Targets)
            _targets[name] = FromOptions(name, target, outbound);
        Targets = [.. _targets.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public IReadOnlyList<OutboundTarget> Targets { get; }

    /// <inheritdoc />
    public virtual OutboundTarget? Find(string name) =>
        !string.IsNullOrEmpty(name) && _targets.TryGetValue(name, out var target) ? target : null;

    /// <summary>把一条已校验的目标配置转成运行时目标(不含秘密)。</summary>
    public static OutboundTarget FromOptions(string name, OutboundTargetOptions target, IntegrationOutboundOptions outbound)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(outbound);
        var baseUrl = target.BaseUrl.EndsWith('/') ? target.BaseUrl : target.BaseUrl + "/";
        var auth = target.Auth ?? new OutboundAuthOptions();
        return new OutboundTarget(
            name,
            new Uri(baseUrl, UriKind.Absolute),
            TimeSpan.FromSeconds(target.TimeoutSeconds ?? outbound.DefaultTimeoutSeconds),
            [.. target.TrustedCidrs ?? []],
            auth.Type,
            auth.HeaderName,
            auth.Username,
            target.IdempotencyHeader);
    }
}
