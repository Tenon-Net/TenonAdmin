using Microsoft.Extensions.Logging;

namespace TenonAdmin.Integration;

/// <summary>默认告警出口:结构化 Warning 日志(日志平台据此告警)。</summary>
public class LoggingDeliveryAlertSink(ILogger<LoggingDeliveryAlertSink> logger) : IDeliveryAlertSink
{
    /// <inheritdoc />
    public virtual Task NotifyAsync(DeliveryAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        logger.LogWarning("可靠投递需要人工处理:{DeliveryKey}(Id={DeliveryId},适配器 {Adapter},操作 {Operation})进入 {Status},原因 {Reason}",
            alert.DeliveryKey, alert.DeliveryId, alert.Adapter, alert.Operation, alert.Status, alert.Reason);
        return Task.CompletedTask;
    }
}

/// <summary>把告警分发给全部出口;单个出口失败只记错误日志,不影响投递状态与其他出口。</summary>
public class DeliveryAlertPublisher(IEnumerable<IDeliveryAlertSink> sinks, TimeProvider time, ILogger<DeliveryAlertPublisher> logger)
{
    /// <summary>记录进入需要人工处理的状态后调用。</summary>
    public virtual async Task PublishAsync(IntegrationDelivery row, DeliveryTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(transition);
        if (!transition.NeedsAttention) return;
        var alert = new DeliveryAlert(row.Id, row.DeliveryKey, row.Adapter, row.Operation, transition.Status,
            transition.Reason ?? transition.LastError, time.GetUtcNow());
        foreach (var sink in sinks)
        {
            try
            {
                await sink.NotifyAsync(alert, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError("投递告警出口 {Sink} 失败。DeliveryKey={DeliveryKey} ExceptionType={ExceptionType}",
                    sink.GetType().Name, row.DeliveryKey, ex.GetType().Name);
            }
        }
    }
}
