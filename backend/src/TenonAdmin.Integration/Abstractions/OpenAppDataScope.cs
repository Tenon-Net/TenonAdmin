using System.Globalization;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 一次开放请求的生效数据范围(不可变)。<see cref="IsAll"/> 表示管理员显式授予该维度全部数据;
/// 否则只允许 <see cref="Values"/>(机构策略已展开下级)。未绑定的请求到不了动作——授权阶段即 403。
/// </summary>
public sealed class OpenAppDataScope
{
    /// <summary>内核机构维度的拒绝值:空机构集合、不附加「仅本人」(应用没有用户身份)。</summary>
    public static readonly DataScopeResult DenyKernelScope = DataScopeResult.Restricted([], includeSelf: false, userId: 0);

    /// <summary>声明为 <see cref="OpenApiDataScopes.None"/> 的端点使用的范围:不允许任何受范围约束的值。</summary>
    public static readonly OpenAppDataScope None = new(OpenApiDataScopes.None, false, []);

    private readonly HashSet<string> _set;

    /// <summary>构造生效范围。</summary>
    public OpenAppDataScope(string key, bool isAll, IReadOnlyCollection<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(values);
        Key = key;
        IsAll = isAll;
        Values = values.ToList();
        _set = new HashSet<string>(Values, StringComparer.Ordinal);
    }

    /// <summary>范围策略键。</summary>
    public string Key { get; }

    /// <summary>是否显式授予全部数据。</summary>
    public bool IsAll { get; }

    /// <summary>允许的值(<see cref="IsAll"/> 为真时为空)。</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>是否 <see cref="OpenApiDataScopes.None"/> 范围。</summary>
    public bool IsNone => Key == OpenApiDataScopes.None;

    /// <summary>值是否在范围内(空值恒不在范围内,除非 <see cref="IsAll"/>)。</summary>
    public bool Allows(string? value) => IsAll || (value is not null && _set.Contains(value));

    /// <summary>数值型值(如机构 Id)是否在范围内。</summary>
    public bool Allows(long? value) =>
        IsAll || (value is { } v && _set.Contains(v.ToString(CultureInfo.InvariantCulture)));

    /// <summary>写入/详情前校验目标值;不在范围内抛 <see cref="IntegrationErrorCode.DataOutOfScope"/>。</summary>
    public void EnsureAllowed(string? value) =>
        IntegrationErrorCode.ThrowIf(!Allows(value), IntegrationErrorCode.DataOutOfScope,
            new Dictionary<string, object?> { ["scope"] = Key }, "目标数据不在接入应用的数据范围内。");

    /// <summary>数值型值的写入/详情校验。</summary>
    public void EnsureAllowed(long? value) =>
        IntegrationErrorCode.ThrowIf(!Allows(value), IntegrationErrorCode.DataOutOfScope,
            new Dictionary<string, object?> { ["scope"] = Key }, "目标数据不在接入应用的数据范围内。");

    /// <summary>把值解析成 long(非数字值被忽略);用于数值型字段的 IN 过滤。</summary>
    public IReadOnlyList<long> Int64Values =>
        Values.Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (long?)n : null)
            .Where(n => n.HasValue).Select(n => n!.Value).ToList();

    /// <inheritdoc />
    public override string ToString() => IsAll ? $"{Key}:*" : $"{Key}:[{Values.Count}]";
}
