using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Samples.Integration;

/// <summary>同步通道:决定工单经哪个适配器送达合作方(演示不同对方能力下的恢复方式)。</summary>
public static class PartnerTicketChannels
{
    /// <summary>对方按幂等标识去重、且能按标识查询结果。</summary>
    public const string Modern = "modern";

    /// <summary>对方既不去重也不能查询:结果未知时只能人工核对。</summary>
    public const string Legacy = "legacy";
}

/// <summary>
/// 示例业务实体「合作方工单」:机构隔离(<see cref="DataEntity"/>,后台用户按数据范围看)+ 业务归属字段 <see cref="PartnerCode"/>
/// (开放接口按「合作方」范围隔离)。<see cref="InternalNote"/> 是内部字段,不出现在开放 DTO 里。
/// </summary>
[SugarTable("sample_partner_ticket", TableDescription = "合作方工单(第三方接入示例)")]
public class PartnerTicket : DataEntity
{
    [SugarColumn(Length = 128, ColumnDescription = "标题")]
    public string Title { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "合作方编码")]
    public string PartnerCode { get; set; } = "";

    [SugarColumn(DecimalDigits = 2, Length = 18, ColumnDescription = "金额")]
    public decimal Amount { get; set; }

    [SugarColumn(Length = 16, ColumnDescription = "同步通道")]
    public string Channel { get; set; } = PartnerTicketChannels.Modern;

    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "内部备注(不对外)")]
    public string? InternalNote { get; set; }
}
