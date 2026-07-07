using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 字典类型种子(T5)。播一个"通用状态"类型作为起步样例与冒烟锚点;固定 Id 保幂等,
/// 界面上对字典项的增删改不会被重启覆盖(种子只在缺失时补,不回改已存在行)。
/// </summary>
internal sealed class DictTypeSeed : ISeedData<SysDictType>
{
    /// <summary>通用状态字典编码(其它模块可复用作启用/停用下拉)</summary>
    internal const string COMMON_STATUS_CODE = "common_status";

    public IEnumerable<SysDictType> HasData() =>
    [
        new SysDictType { Id = 1, Code = COMMON_STATUS_CODE, Name = "通用状态", Sort = 1, Enabled = true, Remark = "启用/停用等二态开关的通用字典" },
    ];
}

/// <summary>
/// 字典项种子(T5)。挂在 <see cref="DictTypeSeed.COMMON_STATUS_CODE"/> 下的两项;固定 Id 幂等。
/// </summary>
internal sealed class DictItemSeed : ISeedData<SysDictItem>
{
    public IEnumerable<SysDictItem> HasData() =>
    [
        new SysDictItem { Id = 1, DictTypeCode = DictTypeSeed.COMMON_STATUS_CODE, Label = "启用", Value = "1", Sort = 1, Enabled = true },
        new SysDictItem { Id = 2, DictTypeCode = DictTypeSeed.COMMON_STATUS_CODE, Label = "停用", Value = "0", Sort = 2, Enabled = true },
    ];
}
