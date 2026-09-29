using System.Globalization;
using TenonAdmin.Integration;
using TenonAdmin.SqlSugar;
using TenonApp.Modules;

namespace TenonApp.Integrations;

/// <summary>新建结果:本地文档 Id 与同步到对方的投递标识(可在「系统集成 → 可靠投递」页按它查进度)。</summary>
public sealed record SampleDocSynced(long Id, string DeliveryKey);

/// <summary>新建文档并同步到对方系统。</summary>
public interface ISampleDocSyncService
{
    /// <summary>本地建文档与「同步到对方」的投递记录在同一事务提交。</summary>
    Task<SampleDocSynced> CreateAndSyncAsync(string title, CancellationToken cancellationToken = default);
}

/// <summary>
/// 事务内可靠投递示例:业务写入与投递记录同库同事务,提交后由后台投递器送达;对方故障不会回滚本地已成功的业务,
/// 本地回滚则投递记录一并消失。事务里只写库、不做网络调用——需要与业务原子的外呼一律入队。
/// </summary>
public class SampleDocSyncService(IRepository<SampleDoc> docs, IDeliveryOutbox outbox) : ISampleDocSyncService
{
    /// <inheritdoc />
    public virtual async Task<SampleDocSynced> CreateAndSyncAsync(string title, CancellationToken cancellationToken = default)
    {
        SampleDocSynced? created = null;
        var result = await docs.Db.Ado.UseTranAsync(async () =>
        {
            var doc = new SampleDoc { Title = title };
            await docs.InsertAsync(doc);
            var enqueued = await outbox.EnqueueAsync(new DeliveryRequest
            {
                Adapter = SampleDocSyncAdapter.AdapterName,
                Operation = "doc.create",
                DeliveryKey = $"sample-doc:{doc.Id}",   // 稳定的业务键:所有重试与人工操作都用它作幂等标识
                BusinessKey = doc.Id.ToString(CultureInfo.InvariantCulture),
                Payload = new SampleDocPayload(doc.Title),
            }, cancellationToken);
            created = new SampleDocSynced(doc.Id, enqueued.DeliveryKey);
        });
        if (!result.IsSuccess) throw result.ErrorException;
        return created!;
    }
}
