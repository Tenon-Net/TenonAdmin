using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>发布通知入参(管理员)。发布即广播全体用户。</summary>
public record NoticePublishInput
{
    /// <summary>标题</summary>
    public string Title { get; init; } = "";

    /// <summary>正文</summary>
    public string? Content { get; init; }

    /// <summary>类型(通知 / 公告)</summary>
    public NoticeType Type { get; init; } = NoticeType.Notice;
}

/// <summary>通知分页查询入参(管理端全量列表 / 用户端"我的通知"共用)。</summary>
public record NoticePageInput : PageInputBase
{
    /// <summary>标题(模糊匹配,可选)</summary>
    public string? Title { get; init; }

    /// <summary>类型(精确匹配,可选)</summary>
    public NoticeType? Type { get; init; }
}

/// <summary>"我的通知"列表项:通知内容 + 当前用户是否已读。</summary>
public record NoticeMineItem
{
    /// <summary>通知 Id</summary>
    public long Id { get; init; }

    /// <summary>标题</summary>
    public string Title { get; init; } = "";

    /// <summary>正文</summary>
    public string? Content { get; init; }

    /// <summary>类型</summary>
    public NoticeType Type { get; init; }

    /// <summary>发布时间(= 通知创建时间)</summary>
    public DateTime PublishTime { get; init; }

    /// <summary>当前用户是否已读</summary>
    public bool IsRead { get; init; }
}
