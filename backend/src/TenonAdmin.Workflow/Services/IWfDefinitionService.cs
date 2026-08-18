using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程定义 CRUD + 发布/停用 + 版本历史。方法全 <c>virtual</c>;
/// 消费者可继承覆写单步或前置 <c>TryAdd</c> 整体替换。
/// </summary>
public interface IWfDefinitionService
{
    /// <summary>分页查询定义(不含草稿模型)。</summary>
    Task<PagedList<WfDefinition>> PageAsync(
        WfDefinitionPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>取定义详情(含草稿模型)。</summary>
    Task<WfDefinitionDetailOutput> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>新增定义 + 草稿版本 0;返回定义 Id。</summary>
    Task<long> AddAsync(WfDefinitionInput input, CancellationToken cancellationToken = default);

    /// <summary>更新元数据与草稿模型(不改已发布快照 / 不改 Status)。</summary>
    Task UpdateAsync(WfDefinitionInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发布:校验草稿 → 写入不可变版本快照 → <c>CurrentVersion++</c> → Status=Published。
    /// 返回新版本号。
    /// </summary>
    Task<int> PublishAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>停用:不可新发起;在途实例不受影响。</summary>
    Task DisableAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>软删定义。有在途实例则拒绝;已完结单据仍可按版本快照看详情。</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>已发布版本列表(Version≥1,按版本号降序)。</summary>
    Task<IReadOnlyList<WfDefinitionVersionOutput>> ListVersionsAsync(
        long definitionId,
        CancellationToken cancellationToken = default);
}
