using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例"机构隔离业务实体"——继承 <see cref="DataEntity"/> 即获得机构数据范围锚点 <c>CreateOrgId</c>:
/// 查询自动按当前用户数据范围过滤,写(改/删)由仓储写路径守卫兜底(越权改删他机构行会被拒)。
/// 是消费方新增"受数据权限约束的业务表"的抄写样板(对照全局字典等不需机构隔离的表继续用 BaseEntity)。
/// </summary>
[SugarTable("sample_doc", TableDescription = "示例机构隔离业务实体(集成测试)")]
public class SampleDoc : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";
}
