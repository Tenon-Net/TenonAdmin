using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// <see cref="IDataScopeContext"/> 默认实现:<c>AsyncLocal</c> 载体。
/// <para>值随异步执行流(ExecutionContext)向下传递:授权管道在请求早期写入,
/// 同请求后续的 DB 查询即读到同一范围;不同请求各自独立的执行流,天然隔离、无需清理。</para>
/// </summary>
public sealed class DataScopeContext : IDataScopeContext
{
    private static readonly AsyncLocal<DataScopeResult?> CURRENT = new();

    /// <summary>未设置时返回 <see cref="DataScopeResult.Unrestricted"/>(系统/可信上下文放行)</summary>
    public DataScopeResult Current
    {
        get => CURRENT.Value ?? DataScopeResult.Unrestricted;
        set => CURRENT.Value = value;
    }
}
