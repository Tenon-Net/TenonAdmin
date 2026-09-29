using System.Linq.Expressions;
using SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 自定义范围策略在标准查询路径上的约束(实现契约 §4.6):把业务字段限制在当前应用的生效范围内。
/// <see cref="OpenAppDataScope.IsAll"/> 时不加条件;范围为空时恒假(<c>1=0</c>);字符串值生成参数化的 <c>= @p0 OR = @p1 …</c>,
/// 数值生成 <c>IN (...)</c>。
/// <para>列表用它过滤;详情用它过滤后按「不存在」处理;写入前另用 <see cref="OpenAppDataScope.EnsureAllowed(string)"/> 校验目标值。</para>
/// </summary>
public static class OpenAppScopeQueryExtensions
{
    /// <summary>
    /// 按字符串业务字段限制范围。每个值作为参数比较,不走 <c>List.Contains</c>:后者把字符串内联成 SQL 字面量,
    /// SQL Server 上没有 N 前缀,非拉丁字符(如中文编码)在拉丁排序规则下会变成 <c>?</c> 而再也匹配不上。
    /// </summary>
    public static ISugarQueryable<T> WhereInScope<T>(this ISugarQueryable<T> query, OpenAppDataScope scope, Expression<Func<T, string?>> selector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(selector);
        if (scope.IsAll) return query;
        if (scope.Values.Count == 0) return query.Where("1=0");
        var comparisons = scope.Values
            .Select(v => (Expression)Expression.Equal(selector.Body, ValueOf<string?>(v)))
            .ToList();
        return query.Where(Expression.Lambda<Func<T, bool>>(AnyOf(comparisons, 0, comparisons.Count), selector.Parameters));
    }

    /// <summary>按可空数值业务字段(如机构 Id、合作方 Id)限制范围。</summary>
    public static ISugarQueryable<T> WhereInScope<T>(this ISugarQueryable<T> query, OpenAppDataScope scope, Expression<Func<T, long?>> selector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(selector);
        if (scope.IsAll) return query;
        var values = scope.Int64Values.Select(v => (long?)v).ToList();
        if (values.Count == 0) return query.Where("1=0");
        return query.Where(ContainsExpression(new ValueHolder<List<long?>>(values), selector));
    }

    /// <summary>按非空数值业务字段限制范围。</summary>
    public static ISugarQueryable<T> WhereInScope<T>(this ISugarQueryable<T> query, OpenAppDataScope scope, Expression<Func<T, long>> selector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(selector);
        if (scope.IsAll) return query;
        var values = scope.Int64Values.ToList();
        if (values.Count == 0) return query.Where("1=0");
        return query.Where(ContainsExpression(new ValueHolder<List<long>>(values), selector));
    }

    /// <summary>
    /// 组装 <c>x =&gt; holder.Value.Contains(selector(x))</c>:经成员访问(而非常量)引用值集合,
    /// 与手写闭包的表达式形状一致,SqlSugar 各方言都按 <c>IN (...)</c> 翻译(数值内联无编码问题)。
    /// </summary>
    private static Expression<Func<T, bool>> ContainsExpression<T, TValue>(ValueHolder<List<TValue>> holder, Expression<Func<T, TValue>> selector)
    {
        var values = Expression.Property(Expression.Constant(holder), nameof(ValueHolder<List<TValue>>.Value));
        var contains = typeof(List<TValue>).GetMethod(nameof(List<TValue>.Contains), [typeof(TValue)])!;
        var body = Expression.Call(values, contains, selector.Body);
        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    /// <summary>经成员访问引用一个值:SqlSugar 把它翻译成参数(字符串为 NVARCHAR 等 Unicode 参数),而不是内联字面量。</summary>
    private static MemberExpression ValueOf<TValue>(TValue value) =>
        Expression.Property(Expression.Constant(new ValueHolder<TValue>(value)), nameof(ValueHolder<TValue>.Value));

    /// <summary>把比较式两两折成平衡的 <c>OR</c> 树(上千个值时表达式深度也只有十几层)。</summary>
    private static Expression AnyOf(IReadOnlyList<Expression> comparisons, int start, int count) =>
        count == 1
            ? comparisons[start]
            : Expression.OrElse(AnyOf(comparisons, start, count / 2), AnyOf(comparisons, start + count / 2, count - count / 2));

    private sealed class ValueHolder<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }
}
