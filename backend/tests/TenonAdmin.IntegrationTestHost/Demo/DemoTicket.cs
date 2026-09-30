using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>
/// 测试宿主的「消费者业务实体」:机构隔离(<see cref="DataEntity"/>)+ 业务归属字段 <see cref="PartnerCode"/>。
/// <see cref="InternalNote"/> 是内部字段,开放 DTO 不包含它(字段白名单)。
/// </summary>
[SugarTable("demo_ticket", TableDescription = "测试工单")]
public class DemoTicket : DataEntity
{
    [SugarColumn(Length = 128)]
    public string Title { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string PartnerCode { get; set; } = "";

    [SugarColumn(DecimalDigits = 2, Length = 18)]
    public decimal Amount { get; set; }

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? InternalNote { get; set; }

    public bool Closed { get; set; }
}
