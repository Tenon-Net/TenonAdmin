using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 字典服务——字典类型/字典项的标准增删改查。字典项的启用列表是前端下拉高频读的数据,
/// <see cref="GetItemsByTypeAsync"/> 走读穿透缓存,增删改均显式失效对应类型的缓存。
/// </summary>
public interface IDictService
{
    /// <summary>分页查询字典类型,按编码/名称模糊过滤,按排序升序返回。</summary>
    Task<PagedList<SysDictType>> PageTypesAsync(DictTypePageInput input);

    /// <summary>按 Id 取字典类型,不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task<SysDictType> GetTypeAsync(long id);

    /// <summary>新增字典类型,Code 唯一(已存在抛 <see cref="ErrorCode.DictTypeCodeExists"/>),返回新 Id。</summary>
    Task<long> AddTypeAsync(DictTypeInput input);

    /// <summary>更新字典类型(仅 Name/Sort/Enabled/Remark 生效,Code 创建后不可变),不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task UpdateTypeAsync(long id, DictTypeInput input);

    /// <summary>删除字典类型(级联软删该类型下全部字典项),不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task DeleteTypeAsync(long id);

    /// <summary>按类型编码取启用中的字典项列表(读穿透缓存,前端下拉的数据源)。</summary>
    Task<IReadOnlyList<SysDictItem>> GetItemsByTypeAsync(string typeCode);

    /// <summary>新增字典项,返回新 Id。</summary>
    Task<long> AddItemAsync(DictItemInput input);

    /// <summary>更新字典项,不存在抛 <see cref="ErrorCode.DictItemNotFound"/>。</summary>
    Task UpdateItemAsync(long id, DictItemInput input);

    /// <summary>删除字典项(软删),不存在抛 <see cref="ErrorCode.DictItemNotFound"/>。</summary>
    Task DeleteItemAsync(long id);
}
