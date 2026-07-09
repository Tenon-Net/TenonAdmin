using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonApp.Modules;

/// <summary>
/// 示例"机构隔离业务实体"——继承 <see cref="DataEntity"/> 即获得机构数据范围锚点 <c>CreateOrgId</c>:
/// 查询自动按当前用户数据范围过滤,写(改/删)由仓储写路径守卫兜底(越权改删他机构行会被拒)。
/// 复制此四件套即可新增一张受数据权限约束的业务表;不需机构隔离的表(如全局字典)改继承 <c>BaseEntity</c>。
/// </summary>
[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
