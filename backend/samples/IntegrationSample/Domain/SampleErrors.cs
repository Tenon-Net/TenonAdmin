using TenonAdmin.Core;

namespace TenonAdmin.Samples.Integration;

/// <summary>示例自己的业务错误码(消费者号段,不占用内核与卫星包号段)。</summary>
public static class SampleErrors
{
    public const int PartnerNotFound = 61001;
    public const int TicketNotFound = 61002;
    public const int ChannelInvalid = 61003;
    public const int CallbackStatusInvalid = 61004;

    public static AdminException PartnerNotFoundException(string code) =>
        new((ErrorCode)PartnerNotFound, new Dictionary<string, object?> { ["partnerCode"] = code }, $"未找到编号为 {code} 的工单对接方。");

    public static AdminException TicketNotFoundException() => new((ErrorCode)TicketNotFound, null, "工单不存在。");
}
