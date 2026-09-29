using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放接口分页入参(实现契约 §4.7):沿用内核 <c>current</c>/<c>size</c> 约定;页码 ≤0 归一为 1,
/// 页大小 ≤0 归一为 20,上限 <see cref="MaxSize"/>(低于内核后台接口的 200,对外更保守)。
/// </summary>
public record OpenApiPageInput
{
    /// <summary>开放接口单页最大条数。</summary>
    public const int MaxSize = 100;

    /// <summary>默认页大小。</summary>
    public const int DefaultSize = 20;

    /// <summary>页码(从 1 起)。</summary>
    public int Current { get; init; } = 1;

    /// <summary>每页条数(1–100)。</summary>
    public int Size { get; init; } = DefaultSize;

    /// <summary>归一后的页码。</summary>
    public int NormalizedCurrent => Current < 1 ? 1 : Current;

    /// <summary>归一后的页大小。</summary>
    public int NormalizedSize => Size <= 0 ? DefaultSize : Math.Min(Size, MaxSize);
}

/// <summary>开放接口分页辅助:查询 → 当前页实体 → 映射为输出 DTO,返回沿用内核 <see cref="PagedList{T}"/> 形状。</summary>
public static class OpenApiPagingExtensions
{
    /// <summary>按 <see cref="OpenApiPageInput"/> 分页并映射(映射在内存进行,只作用于当前页)。</summary>
    public static async Task<PagedList<TDto>> ToOpenPagedListAsync<TEntity, TDto>(
        this ISugarQueryable<TEntity> query,
        OpenApiPageInput input,
        Func<TEntity, TDto> map)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(map);
        var page = await query.ToPagedListAsync(input.NormalizedCurrent, input.NormalizedSize);
        return new PagedList<TDto>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(map).ToList(),
        };
    }
}
