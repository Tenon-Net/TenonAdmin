using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// SqlSugar 查询 → <see cref="PagedList{T}"/> 的物化扩展(设计 §5.7)。
/// 一次查询同时取回当前页数据与总数(SqlSugar 的 <c>RefAsync</c> 机制),业务侧 <c>.ToPagedListAsync(input.Current, input.Size)</c> 即可。
/// </summary>
public static class PagedListExtensions
{
    /// <summary>单页最大条数——防止调用方传超大 Size 拖垮库(热路径保护,设计 §14 稳健性)。</summary>
    public const int MAX_SIZE = 200;

    public static async Task<PagedList<T>> ToPagedListAsync<T>(this ISugarQueryable<T> query, int current, int size)
    {
        current = current <= 0 ? 1 : current;
        size = size <= 0 ? 20 : Math.Min(size, MAX_SIZE);

        // RefAsync<int> 是 SqlSugar 的"输出参数"载体:分页查询顺带把总数写回它,免二次 Count 往返
        RefAsync<int> total = 0;
        var items = await query.ToPageListAsync(current, size, total);

        return new PagedList<T> { Current = current, Size = size, Total = total, Items = items };
    }
}
