namespace TenonAdmin.Core;

/// <summary>
/// 批量删除入参:目标 Id 集合。各模块的 <c>POST .../batch-delete</c> 端点统一收此形状,
/// 服务层 <c>DeleteBatchAsync</c> 复用单删的守卫/软删/失效逻辑逐个处理。空集合视为无操作。
/// </summary>
public sealed class BatchDeleteInput
{
    /// <summary>要删除的记录 Id 集合。</summary>
    public IReadOnlyCollection<long> Ids { get; init; } = [];
}
