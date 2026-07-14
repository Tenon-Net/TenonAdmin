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

    /// <summary>机构分类字典编码(机构管理"分类"下拉:公司/部门/小组)</summary>
    internal const string ORG_CATEGORY_CODE = "org_category";

    /// <summary>性别字典编码(用户管理"性别"下拉:男/女/未知)</summary>
    internal const string GENDER_CODE = "gender";

    public IEnumerable<SysDictType> HasData() =>
    [
        new SysDictType { Id = 1, Code = COMMON_STATUS_CODE, Name = "通用状态", Sort = 1, Enabled = true, Remark = "启用/停用等二态开关的通用字典" },
        new SysDictType { Id = 2, Code = ORG_CATEGORY_CODE, Name = "机构分类", Sort = 2, Enabled = true, Remark = "机构类型:公司/部门/小组" },
        new SysDictType { Id = 3, Code = GENDER_CODE, Name = "性别", Sort = 3, Enabled = true, Remark = "用户性别:男/女/未知" },
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
        new SysDictItem { Id = 3, DictTypeCode = DictTypeSeed.ORG_CATEGORY_CODE, Label = "公司", Value = "1", Sort = 1, Enabled = true },
        new SysDictItem { Id = 4, DictTypeCode = DictTypeSeed.ORG_CATEGORY_CODE, Label = "部门", Value = "2", Sort = 2, Enabled = true },
        new SysDictItem { Id = 5, DictTypeCode = DictTypeSeed.ORG_CATEGORY_CODE, Label = "小组", Value = "3", Sort = 3, Enabled = true },
        new SysDictItem { Id = 6, DictTypeCode = DictTypeSeed.GENDER_CODE, Label = "男", Value = "1", Sort = 1, Enabled = true },
        new SysDictItem { Id = 7, DictTypeCode = DictTypeSeed.GENDER_CODE, Label = "女", Value = "2", Sort = 2, Enabled = true },
        new SysDictItem { Id = 8, DictTypeCode = DictTypeSeed.GENDER_CODE, Label = "未知", Value = "0", Sort = 3, Enabled = true },
    ];
}
