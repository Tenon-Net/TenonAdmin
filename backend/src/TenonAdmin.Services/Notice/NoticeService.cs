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
    ICurrentUser currentUser) : INoticeService
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

    /// <inheritdoc />
    public virtual async Task<long> PublishAsync(NoticePublishInput input)
    {
        var entity = new SysNotice { Title = input.Title, Content = input.Content, Type = input.Type };
        await notices.InsertAsync(entity);
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
        var page = await notices.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Title), n => n.Title.Contains(input.Title!))
            .WhereIF(input.Type.HasValue, n => n.Type == input.Type!.Value)
            .OrderBy(n => n.CreateTime, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

        var ids = page.Items.Select(n => n.Id).ToList();
        var readIds = ids.Count == 0
            ? []
            : (await reads.AsQueryable()
                .Where(r => r.UserId == me && ids.Contains(r.NoticeId))
                .Select(r => r.NoticeId).ToListAsync()).ToHashSet();

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
                IsRead = readIds.Contains(n.Id),
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public virtual async Task<int> UnreadCountAsync()
    {
        var me = CurrentUserId;
        // 未读 = 活通知里当前用户没有已读回执的。ponytail: readIds 全量载入,量大再改 NotExists 子查询。
        var readIds = await reads.AsQueryable().Where(r => r.UserId == me).Select(r => r.NoticeId).ToListAsync();
        return await notices.AsQueryable()
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
        var unreadIds = await notices.AsQueryable()
            .WhereIF(readIds.Count > 0, n => !readIds.Contains(n.Id))
            .Select(n => n.Id).ToListAsync();
        // ponytail: 逐条插入已读回执;未读量巨大再改批量插入。
        foreach (var noticeId in unreadIds)
            await reads.InsertAsync(new SysNoticeRead { UserId = me, NoticeId = noticeId });
    }
}
