using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="INoticeService"/> 默认实现。通知为全体广播(实体继承 <see cref="BaseEntity"/> 不带机构范围),
/// 每用户已读状态记 <see cref="SysNoticeRead"/>(存在一行=已读)。未读数每次实时统计——
/// <c>// ponytail: 未读用两条 COUNT 级查询实时算,不缓存(避免发布/已读后缓存滞后);用户量巨大再上按用户缓存 + 发布/已读时失效。</c>
/// </summary>
public class NoticeService(
    IRepository<SysNotice> notices,
    IRepository<SysNoticeRead> reads,
    IRepository<SysNoticeReceiver> receivers,
    IRbacService rbac,
    ICurrentUser currentUser,
    IRealtimePublisher? realtime = null) : INoticeService
{
    /// <summary>当前登录用户 Id(用户端端点均 <c>[ActiveSession]</c> 保证已认证;缺失按令牌失效处理)。</summary>
    private long CurrentUserId
    {
        get
        {
            AdminException.ThrowIf(currentUser.UserId is null, ErrorCode.TokenInvalid);
            return currentUser.UserId!.Value;
        }
    }

    /// <summary>
    /// "当前用户可见"的通知查询:全体广播 + 定向到我角色/我本人的。
    /// <para>ICurrentUser 不带角色,角色经 <see cref="IRbacService.GetUserRoleIdsAsync"/>(已缓存)取;
    /// 定向命中的 NoticeId 用一次 join 预载(ponytail: 全量载入,定向通知量巨大再改 exists 子查询)。</para>
    /// </summary>
    protected virtual async Task<ISugarQueryable<SysNotice>> VisibleToMeAsync(long me)
    {
        var myRoleIds = (await rbac.GetUserRoleIdsAsync(me)).ToList();
        var targetedIds = await receivers.AsQueryable()
            .InnerJoin<SysNotice>((r, n) => r.NoticeId == n.Id)
            .Where((r, n) => (n.ReceiverType == ReceiverType.Role && myRoleIds.Contains(r.ReceiverId))
                          || (n.ReceiverType == ReceiverType.User && r.ReceiverId == me))
            .Select((r, n) => r.NoticeId)
            .ToListAsync();
        return notices.AsQueryable()
            .Where(n => n.ReceiverType == ReceiverType.All || targetedIds.Contains(n.Id));
    }

    /// <inheritdoc />
    public virtual async Task<long> PublishAsync(NoticePublishInput input)
    {
        var entity = new SysNotice
        {
            Title = input.Title,
            Content = input.Content,
            Type = input.Type,
            ReceiverType = input.ReceiverType,
        };
        await notices.InsertAsync(entity);
        // 定向发送:每个目标(角色 Id 或用户 Id)写一行接收目标;全体广播不写行。
        if (input.ReceiverType != ReceiverType.All && input.ReceiverIds is { Count: > 0 })
        {
            var rows = input.ReceiverIds.Distinct()
                .Select(rid => new SysNoticeReceiver { NoticeId = entity.Id, ReceiverId = rid })
                .ToList();
            await receivers.InsertRangeAsync(rows);
        }
        // 实时推送(开启时):发布即广播 notice-changed,各端立刻自查未读角标(替代最长 30s 轮询延迟)。
        // ponytail: 广播让各端自查(client 再打 unread-count,2 条 COUNT 很便宜);定向精确唤醒 receiver(角色→用户)集,公告量大再优化。
        if (realtime is not null)
            await realtime.NotifyAllAsync("notice-changed");
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysNotice>> PageAsync(NoticePageInput input) =>
        await notices.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Title), n => n.Title.Contains(input.Title!))
            .WhereIF(input.Type.HasValue, n => n.Type == input.Type!.Value)
            .OrderBy(n => n.CreateTime, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var notice = await notices.GetByIdAsync(id);
        AdminException.ThrowIf(notice is null, ErrorCode.NoticeNotFound);
        await notices.DeleteAsync(id);
        // 已读回执行为无害孤儿,不清理:未读统计只看"活通知里没读过的",删掉的通知自然不计。
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<NoticeMineItem>> PageMineAsync(NoticePageInput input)
    {
        var me = CurrentUserId;
        // 已读回执全量载入,既用于 OnlyUnread 过滤、也用于回填 IsRead。ponytail: 量大再改子查询。
        var readIds = await reads.AsQueryable().Where(r => r.UserId == me).Select(r => r.NoticeId).ToListAsync();
        var visible = await VisibleToMeAsync(me);
        var page = await visible
            .WhereIF(!string.IsNullOrEmpty(input.Title), n => n.Title.Contains(input.Title!))
            .WhereIF(input.Type.HasValue, n => n.Type == input.Type!.Value)
            .WhereIF(input.OnlyUnread == true && readIds.Count > 0, n => !readIds.Contains(n.Id))
            .OrderBy(n => n.CreateTime, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        var readSet = readIds.ToHashSet();
        return new PagedList<NoticeMineItem>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = page.Items.Select(n => new NoticeMineItem
            {
                Id = n.Id,
                Title = n.Title,
                Content = n.Content,
                Type = n.Type,
                PublishTime = n.CreateTime,
                IsRead = readSet.Contains(n.Id),
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<int> UnreadCountAsync()
    {
        var me = CurrentUserId;
        // 未读 = 可见通知里当前用户没有已读回执的。ponytail: readIds 全量载入,量大再改 NotExists 子查询。
        var readIds = await reads.AsQueryable().Where(r => r.UserId == me).Select(r => r.NoticeId).ToListAsync();
        var visible = await VisibleToMeAsync(me);
        return await visible
            .WhereIF(readIds.Count > 0, n => !readIds.Contains(n.Id))
            .CountAsync();
    }

    /// <inheritdoc />
    public virtual async Task MarkReadAsync(long noticeId)
    {
        var me = CurrentUserId;
        var already = await reads.AsQueryable().AnyAsync(r => r.UserId == me && r.NoticeId == noticeId);
        if (already) return; // 幂等:已读回执唯一,不重复插
        await reads.InsertAsync(new SysNoticeRead { UserId = me, NoticeId = noticeId });
    }

    /// <inheritdoc />
    public virtual async Task MarkAllReadAsync()
    {
        var me = CurrentUserId;
        var readIds = await reads.AsQueryable().Where(r => r.UserId == me).Select(r => r.NoticeId).ToListAsync();
        var visible = await VisibleToMeAsync(me);
        var unreadIds = await visible
            .WhereIF(readIds.Count > 0, n => !readIds.Contains(n.Id))
            .Select(n => n.Id).ToListAsync();
        // ponytail: 逐条插入已读回执;未读量巨大再改批量插入。
        foreach (var noticeId in unreadIds)
            await reads.InsertAsync(new SysNoticeRead { UserId = me, NoticeId = noticeId });
    }
}
