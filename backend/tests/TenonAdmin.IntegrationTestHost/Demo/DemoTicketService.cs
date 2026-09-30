using System.Globalization;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Integration;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.IntegrationTestHost;

/// <summary>消费者自己的业务错误码(不占用内核与卫星包号段)。</summary>
public static class DemoTicketErrors
{
    public const int NotFound = 60001;

    public const int ValidationFailed = 60002;

    public static AdminException NotFoundException() => new((ErrorCode)NotFound, null, "工单不存在。");
}

/// <summary>普通业务服务:后台与开放端点共用,不感知调用方是用户还是接入应用。</summary>
public interface IDemoTicketService
{
    ISugarQueryable<DemoTicket> Query();
    Task<DemoTicket?> GetAsync(long id);
    Task<long> CreateAsync(string title, string partnerCode, decimal amount, string? internalNote = null);
    Task<bool> UpdateAsync(long id, string title, decimal amount);
    Task<bool> DeleteAsync(long id);
    Task<bool> CloseAsync(long id);

    /// <summary>建单并在同一事务内登记「同步到合作方」的可靠投递;<paramref name="failAfterEnqueue"/> 模拟入队后业务校验失败。</summary>
    Task<long> CreateAndSyncAsync(string title, string partnerCode, decimal amount, bool failAfterEnqueue = false);
}

public class DemoTicketService(IRepository<DemoTicket> tickets, IDeliveryOutbox outbox) : IDemoTicketService
{
    private const string DeliveryKeyPrefix = "demo-ticket:";

    /// <summary>工单的投递标识(稳定业务键,所有重试与人工操作沿用)。</summary>
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

    public virtual async Task<long> CreateAndSyncAsync(string title, string partnerCode, decimal amount, bool failAfterEnqueue = false)
    {
        long id = 0;
        var result = await tickets.Db.Ado.UseTranAsync(async () =>
        {
            id = await CreateAsync(title, partnerCode, amount);
            await outbox.EnqueueAsync(new DeliveryRequest
            {
                Adapter = DemoPartnerDeliveryAdapter.AdapterName,
                Operation = "ticket.create",
                DeliveryKey = DeliveryKeyOf(id),
                BusinessKey = id.ToString(CultureInfo.InvariantCulture),
                Payload = new DemoTicketPayload(title, amount),
            });
            if (failAfterEnqueue) throw new AdminException((ErrorCode)DemoTicketErrors.ValidationFailed, null, "业务校验失败(测试注入)。");
        });
        if (!result.IsSuccess) throw result.ErrorException;
        return id;
    }

    public virtual ISugarQueryable<DemoTicket> Query() => tickets.AsQueryable();

    public virtual Task<DemoTicket?> GetAsync(long id) => tickets.GetByIdAsync(id);

    public virtual async Task<long> CreateAsync(string title, string partnerCode, decimal amount, string? internalNote = null)
    {
        var ticket = new DemoTicket { Title = title, PartnerCode = partnerCode, Amount = amount, InternalNote = internalNote };
        await tickets.InsertAsync(ticket);
        return ticket.Id;
    }

    public virtual async Task<bool> UpdateAsync(long id, string title, decimal amount)
    {
        var ticket = await tickets.GetByIdAsync(id);
        if (ticket is null) return false;
        ticket.Title = title;
        ticket.Amount = amount;
        return await tickets.UpdateAsync(ticket) > 0;
    }

    public virtual async Task<bool> DeleteAsync(long id) => await tickets.DeleteAsync(id) > 0;

    public virtual async Task<bool> CloseAsync(long id)
    {
        var ticket = await tickets.GetByIdAsync(id);
        if (ticket is null) return false;
        ticket.Closed = true;
        return await tickets.UpdateAsync(ticket) > 0;
    }
}
