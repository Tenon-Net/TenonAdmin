using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>
/// 抄送列表:当前用户的 <c>wf_cc</c> 分页 + 标已读。
/// 抄送不是待办,不走任务动词;实现方法全 <c>virtual</c>,消费者可继承覆写或前置
/// <c>TryAdd</c> 整体替换。
/// </summary>
public interface IWfCcService
{
    /// <summary>我的抄送分页(<c>wf_cc.UserId ==</c> 当前用户)。</summary>
    Task<PagedList<WfCcItemOutput>> PageMineAsync(
        long userId,
        WfCcPageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把属于 <paramref name="userId"/> 的抄送行标已读;已读则幂等返回。
    /// 行不存在或不属于该用户 → <c>48027</c>。
    /// </summary>
    Task MarkReadAsync(long ccId, long userId, CancellationToken cancellationToken = default);
}
