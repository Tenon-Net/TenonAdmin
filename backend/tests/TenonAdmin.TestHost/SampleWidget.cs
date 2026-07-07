using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.TestHost;

/// <summary>示例"用户业务实体"——放在宿主(非内置)程序集,验证用户实体也能被 CodeFirst 建表(§5.7)。</summary>
[SugarTable("sample_widget", TableDescription = "示例业务实体(集成测试)")]
public class SampleWidget : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "名称")]
    public string Name { get; set; } = "";
}
