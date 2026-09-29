using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>管理员可选的范围候选值(树形数据用 <see cref="ParentValue"/> 表达层级)。</summary>
/// <param name="Value">绑定值</param>
/// <param name="Label">显示名</param>
/// <param name="ParentValue">父节点值;顶层为 null</param>
public sealed record OpenApiScopeOption(string Value, string Label, string? ParentValue = null);

/// <summary>应用在某个范围策略上的绑定(管理员配置)。</summary>
/// <param name="ScopeKey">范围策略键</param>
/// <param name="AllValues">显式授予该维度全部数据</param>
/// <param name="Values">绑定值(<paramref name="AllValues"/> 为真时忽略)</param>
public sealed record OpenAppScopeBinding(string ScopeKey, bool AllValues, IReadOnlyList<string> Values);

/// <summary>一次请求的范围解析结果:业务维度的生效范围 + 内核机构维度(写入 <see cref="IDataScopeContext"/>)。</summary>
/// <param name="Scope">请求内可读的生效范围</param>
/// <param name="KernelScope">内核机构维度:决定 <c>IOrgScoped</c> 实体的全局过滤与仓储写守卫</param>
public sealed record OpenAppScopeResolution(OpenAppDataScope Scope, DataScopeResult KernelScope);

/// <summary>
/// 开放接口数据范围策略(实现契约 §4.6)。消费者按业务定义自己的范围(合作方、仓库……),<c>TryAddEnumerable</c> 注册;
/// 同键多个实现时先注册者生效(前置注册即覆盖内置)。
/// <para>框架保证:端点必须声明策略、应用必须有绑定、策略在动作执行前被解析并写入请求上下文与内核范围。
/// 框架<b>不</b>自动识别任意 SQL 的业务归属——自定义策略的查询与写入须使用 <see cref="IOpenAppContext.DataScope"/>
/// (如 <see cref="OpenAppScopeQueryExtensions"/>、<see cref="OpenAppDataScope.EnsureAllowed(string)"/>)。</para>
/// </summary>
public interface IOpenApiDataScopePolicy
{
    /// <summary>策略键(端点声明与绑定都用它;不可为 <see cref="OpenApiDataScopes.None"/>)。</summary>
    string Key { get; }

    /// <summary>显示名(管理界面)。</summary>
    string Name { get; }

    /// <summary>管理界面的候选值;不提供候选值的策略返回空集合(管理员手工填写)。</summary>
    Task<IReadOnlyList<OpenApiScopeOption>> ListOptionsAsync(string? keyword, CancellationToken cancellationToken = default);

    /// <summary>校验并规范化绑定值(去空白、去重、存在性等);非法抛 <see cref="IntegrationErrorCode.ScopeValueInvalid"/>。</summary>
    Task<IReadOnlyList<string>> NormalizeBindingAsync(IReadOnlyCollection<string> values, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把绑定解析成本次请求的生效范围与内核机构维度。<b>默认安全</b>:无法判断时应返回拒绝,
    /// 业务维度已构成完整分区、确需放开机构维度时必须显式返回 <see cref="DataScopeResult.Unrestricted"/>。
    /// </summary>
    Task<OpenAppScopeResolution> ResolveAsync(OpenAppScopeBinding binding, CancellationToken cancellationToken = default);
}

/// <summary>
/// 自定义范围策略基类:值为不透明字符串(≤128,去空白去重),无候选值;内核机构维度默认<b>拒绝</b>(空机构集合)。
/// 子类覆写 <see cref="ResolveKernelScope"/> 显式放开机构维度,或覆写其他 virtual 步骤。
/// </summary>
public abstract class OpenApiDataScopePolicyBase : IOpenApiDataScopePolicy
{
    /// <summary>单个绑定值的最大长度。</summary>
    public const int MaxValueLength = 128;

    /// <summary>单个绑定的最大值个数。</summary>
    public const int MaxValues = 1000;

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<OpenApiScopeOption>> ListOptionsAsync(string? keyword, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OpenApiScopeOption>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<string>> NormalizeBindingAsync(IReadOnlyCollection<string> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalized = values
            .Select(v => v?.Trim() ?? "")
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        IntegrationErrorCode.ThrowIf(
            normalized.Count == 0 || normalized.Count > MaxValues || normalized.Any(v => v.Length > MaxValueLength),
            IntegrationErrorCode.ScopeValueInvalid,
            new Dictionary<string, object?> { ["scope"] = Key });
        return Task.FromResult<IReadOnlyList<string>>(normalized);
    }

    /// <inheritdoc />
    public virtual Task<OpenAppScopeResolution> ResolveAsync(OpenAppScopeBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var scope = new OpenAppDataScope(Key, binding.AllValues, binding.AllValues ? [] : binding.Values);
        return Task.FromResult(new OpenAppScopeResolution(scope, ResolveKernelScope(scope)));
    }

    /// <summary>内核机构维度(<c>IOrgScoped</c> 实体)。默认拒绝;业务维度即完整分区时子类显式返回不受限。</summary>
    protected virtual DataScopeResult ResolveKernelScope(OpenAppDataScope scope) => OpenAppDataScope.DenyKernelScope;
}
