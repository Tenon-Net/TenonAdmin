using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>流程定义分页查询入参。</summary>
public record WfDefinitionPageInput : PageInputBase
{
    /// <summary>名称模糊匹配(可选)</summary>
    public string? Name { get; init; }

    /// <summary>分组精确匹配(可选)</summary>
    public string? GroupName { get; init; }

    /// <summary>状态精确匹配(可选)</summary>
    public WfDefinitionStatus? Status { get; init; }
}

/// <summary>
/// 新增/更新入参。更新时 <see cref="Id"/> 必填;
/// <see cref="Model"/> 写入草稿版本 0(未发布工作副本),发布后才进不可变快照。
/// </summary>
public record WfDefinitionInput
{
    /// <summary>定义 Id;仅 update 必填</summary>
    public long Id { get; init; }

    /// <summary>流程名称</summary>
    public string Name { get; init; } = "";

    /// <summary>图标</summary>
    public string? Icon { get; init; }

    /// <summary>分组</summary>
    public string? GroupName { get; init; }

    /// <summary>钉钉树模型;缺省时落默认仅 start 根节点。</summary>
    public WfModel? Model { get; init; }
}

/// <summary>仅带定义 Id 的写操作入参(publish / disable)。</summary>
public record WfDefinitionIdInput
{
    public long Id { get; init; }
}

/// <summary>定义详情:元数据 + 草稿模型(设计器编辑源)。</summary>
public record WfDefinitionDetailOutput
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public string? GroupName { get; init; }
    public WfDefinitionStatus Status { get; init; }
    public int CurrentVersion { get; init; }
    public DateTime CreateTime { get; init; }
    public long? CreateUserId { get; init; }
    public DateTime? UpdateTime { get; init; }
    public long? UpdateUserId { get; init; }

    /// <summary>草稿模型(Version=0);从未保存过模型时为默认 start 根。</summary>
    public WfModel Model { get; init; } = new();
}

/// <summary>已发布版本列表项(含完整 ModelJson,供版本历史/回看)。</summary>
public record WfDefinitionVersionOutput
{
    public long Id { get; init; }
    public long DefinitionId { get; init; }
    public int Version { get; init; }
    public string ModelJson { get; init; } = "";
    public string? FormSchema { get; init; }
    public DateTime? PublishTime { get; init; }
    public long? PublishUserId { get; init; }
}
