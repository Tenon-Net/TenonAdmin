namespace TenonAdmin.Workflow;

/// <summary>扁平操作队列(Flowable Agenda 单机版):用户动作入队首 Op,循环出队执行直至空或需等人。</summary>
public sealed class WfAgenda
{
    private readonly Queue<IWfOperation> _ops = new();

    public void Plan(IWfOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        _ops.Enqueue(op);
    }

    public bool TryTake(out IWfOperation op) => _ops.TryDequeue(out op!);

    public bool IsEmpty => _ops.Count == 0;
}

/// <summary>Agenda 上的一步操作。实现类方法均为 <c>virtual</c>,便于继承覆写单步。</summary>
public interface IWfOperation
{
    Task ExecuteAsync(WfExecutionContext ctx, CancellationToken cancellationToken);
}
