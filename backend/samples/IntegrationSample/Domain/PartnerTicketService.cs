using System.Globalization;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Samples.Integration;

/// <summary>新建工单入参。</summary>
public sealed record PartnerTicketCreateInput(string Title, string PartnerCode, decimal Amount, string? Channel = null, string? InternalNote = null);

/// <summary>新建结果:本地工单 Id 与同步到合作方的投递标识(可在「可靠投递」页按它查进度)。</summary>
public sealed record PartnerTicketCreated(long Id, string DeliveryKey);

/// <summary>合作方工单业务服务:后台接口与开放接口共用,不感知调用方是后台用户还是接入应用。</summary>
public interface IPartnerTicketService
{
    ISugarQueryable<PartnerTicket> Query();

    Task<PartnerTicket?> GetAsync(long id);

    Task<PartnerTicketCreated> CreateAsync(PartnerTicketCreateInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// 新建工单的两段式:
/// <list type="number">
/// <item><b>普通第三方调用</b>(事务外、同步):向合作方目录确认合作方存在;对方不可用时以 4903x 业务码返回,本地不写任何数据;</item>
/// <item><b>事务内可靠投递</b>:本地建单与「同步到合作方」的投递记录在同一事务提交——之后由后台投递器送达,对方故障不影响本地已成功的业务。</item>
/// </list>
/// 不在数据库事务里做网络调用(持锁等网络);需要与业务原子的外呼一律入队。
/// </summary>
public class PartnerTicketService(IRepository<PartnerTicket> tickets, IDeliveryOutbox outbox, PartnerDirectoryClient directory) : IPartnerTicketService
{
    private const string DeliveryKeyPrefix = "partner-ticket:";

    /// <summary>工单的投递标识:稳定业务键,所有重试与人工操作都用它作幂等标识。</summary>
    public static string DeliveryKeyOf(long ticketId) => DeliveryKeyPrefix + ticketId.ToString(CultureInfo.InvariantCulture);

    /// <summary>从投递标识反解工单 Id(回调用);不是本业务的标识返回 false。</summary>
    public static bool TryParseTicketId(string? deliveryKey, out long ticketId)
    {
        ticketId = 0;
        return deliveryKey is not null
            && deliveryKey.StartsWith(DeliveryKeyPrefix, StringComparison.Ordinal)
            && long.TryParse(deliveryKey.AsSpan(DeliveryKeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out ticketId)
            && ticketId > 0;
    }

    public virtual ISugarQueryable<PartnerTicket> Query() => tickets.AsQueryable();

    public virtual Task<PartnerTicket?> GetAsync(long id) => tickets.GetByIdAsync(id);

    public virtual async Task<PartnerTicketCreated> CreateAsync(PartnerTicketCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var channel = input.Channel ?? PartnerTicketChannels.Modern;
        if (channel is not (PartnerTicketChannels.Modern or PartnerTicketChannels.Legacy))
            throw new AdminException((ErrorCode)SampleErrors.ChannelInvalid, new Dictionary<string, object?> { ["channel"] = channel }, "同步通道只能是 modern 或 legacy。");

        await directory.EnsurePartnerExistsAsync(input.PartnerCode, cancellationToken);

        PartnerTicketCreated? created = null;
        var result = await tickets.Db.Ado.UseTranAsync(async () =>
        {
            var ticket = new PartnerTicket
            {
                Title = input.Title, PartnerCode = input.PartnerCode, Amount = input.Amount, Channel = channel, InternalNote = input.InternalNote,
            };
            await tickets.InsertAsync(ticket);
            var enqueued = await outbox.EnqueueAsync(new DeliveryRequest
            {
                Adapter = channel == PartnerTicketChannels.Legacy ? LegacyTicketSyncAdapter.AdapterName : PartnerTicketSyncAdapter.AdapterName,
                Operation = "ticket.create",
                DeliveryKey = DeliveryKeyOf(ticket.Id),
                BusinessKey = ticket.Id.ToString(CultureInfo.InvariantCulture),
                Payload = new PartnerTicketPayload(ticket.Title, ticket.Amount),   // 只有业务数据,凭据在投递时再取
            }, cancellationToken);
            created = new PartnerTicketCreated(ticket.Id, enqueued.DeliveryKey);
        });
        if (!result.IsSuccess) throw result.ErrorException;
        return created!;
    }
}
