using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 消息通知服务(设计 §4 消息中心,轮询模型)——管理员发布/管理广播通知,用户读取"我的通知"、
/// 查未读数、标记已读。已读状态按用户记 <see cref="SysNoticeRead"/>。
/// </summary>
public interface INoticeService
{
    // ── 管理端 ──────────────────────────────────────────────────────

    /// <summary>发布一条通知(广播全体),返回新 Id。</summary>
    Task<long> PublishAsync(NoticePublishInput input);

    /// <summary>分页查询全部通知(管理端,按发布时间倒序)。</summary>
    Task<PagedList<SysNotice>> PageAsync(NoticePageInput input);

    /// <summary>删除通知(软删),不存在抛 <see cref="ErrorCode.NoticeNotFound"/>。</summary>
    Task DeleteAsync(long id);

    // ── 用户端(任何登录用户) ────────────────────────────────────────

    /// <summary>分页查询"我的通知"(按发布时间倒序,含当前用户已读标记)。</summary>
    Task<PagedList<NoticeMineItem>> PageMineAsync(NoticePageInput input);

    /// <summary>当前用户未读通知数(顶栏角标用)。</summary>
    Task<int> UnreadCountAsync();

    /// <summary>标记某条通知为已读(幂等)。</summary>
    Task MarkReadAsync(long noticeId);

    /// <summary>标记当前用户全部通知为已读。</summary>
    Task MarkAllReadAsync();
}
