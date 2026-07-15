using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>发布通知入参(管理员)。可发给全体 / 指定角色 / 指定用户。</summary>
public record NoticePublishInput
{
    /// <summary>标题</summary>
    public string Title { get; init; } = "";

    /// <summary>正文</summary>
    public string? Content { get; init; }

    /// <summary>类型(通知 / 公告 / 消息)</summary>
    public NoticeType Type { get; init; } = NoticeType.Notice;

    /// <summary>接收范围(全体 / 角色 / 用户)</summary>
    public ReceiverType ReceiverType { get; init; } = ReceiverType.All;

    /// <summary>接收目标 Id 集合(角色 Id 或用户 Id,取决于 <see cref="ReceiverType"/>);为 <see cref="ReceiverType.All"/> 时忽略。</summary>
    public IReadOnlyCollection<long>? ReceiverIds { get; init; }
}

/// <summary>通知分页查询入参(管理端全量列表 / 用户端"我的通知"共用)。</summary>
public record NoticePageInput : PageInputBase
{
    /// <summary>标题(模糊匹配,可选)</summary>
    public string? Title { get; init; }

    /// <summary>类型(精确匹配,可选)</summary>
    public NoticeType? Type { get; init; }

    /// <summary>仅未读(用户端"未读"页签用;管理端列表忽略)</summary>
    public bool? OnlyUnread { get; init; }
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
