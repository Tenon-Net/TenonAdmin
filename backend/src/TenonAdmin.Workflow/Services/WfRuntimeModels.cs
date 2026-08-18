using TenonAdmin.Core;

namespace TenonAdmin.Workflow;

/// <summary>发起流程入参(<c>POST .../instance/start</c>):定义 Id + 业务键 + 摘要变量。</summary>
public record WfStartInput
{
    /// <summary>流程定义 Id(须已发布)</summary>
    public long DefinitionId { get; init; }

    /// <summary>业务单据键(可空=纯审批)</summary>
    public string? BusinessKey { get; init; }

    /// <summary>发起摘要变量 JSON(金额/天数/类型…)</summary>
    public string? VariablesJson { get; init; }

    /// <summary>按节点 Id 提交发起人自选审批人(仅 selfSelect Provider 使用)</summary>
    public Dictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }
}

/// <summary>当前用户可发起的已发布定义列表项。</summary>
public record WfStartableDefinitionOutput
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public string? GroupName { get; init; }
    public int Version { get; init; }
    public string? FormComponent { get; init; }
}

/// <summary>当前用户可发起的已发布版本快照。</summary>
public record WfStartableDefinitionDetailOutput : WfStartableDefinitionOutput
{
    public long DefinitionVersionId { get; init; }
    public WfModel Model { get; init; } = new();
}

/// <summary>我发起的实例分页查询。</summary>
public record WfInstancePageInput : PageInputBase
{
    public WfInstanceStatus? Status { get; init; }

    public long? DefinitionId { get; init; }

    /// <summary>业务键精确匹配(可选)</summary>
    public string? BusinessKey { get; init; }
}

/// <summary>待办 / 已办分页查询(当前用户)。</summary>
public record WfTaskPageInput : PageInputBase
{
    public long? DefinitionId { get; init; }
}

/// <summary>审批动词入参(同意 / 拒绝 / 转办)。</summary>
public record WfTaskActionInput
{
    public long TaskId { get; init; }

    public string? Comment { get; init; }

    /// <summary>转办目标用户 Id;仅 transfer 必填</summary>
    public long ToUserId { get; init; }
}

/// <summary>我发起的列表项。</summary>
public record WfInstanceListItemOutput
{
    public long Id { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public int Version { get; init; }
    public string? BusinessKey { get; init; }
    public WfInstanceStatus Status { get; init; }
    public string? VariablesJson { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 实例详情——进度 + 意见 + <see cref="FormComponent"/> 业务表单挂载点(前端动态挂载消费者组件)。
/// </summary>
public record WfInstanceDetailOutput
{
    public long Id { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public long DefinitionVersionId { get; init; }
    public int Version { get; init; }
    public string? BusinessKey { get; init; }
    public long StarterUserId { get; init; }
    public WfInstanceStatus Status { get; init; }
    public string? VariablesJson { get; init; }

    /// <summary>
    /// 开发者表单挂载点:消费者前端页面路径(来自模型 <c>formComponent</c>);
    /// 空=纯审批或走 M3 动态表单。
    /// </summary>
    public string? FormComponent { get; init; }

    public DateTime CreateTime { get; init; }

    /// <summary>当前用户在本实例上的待办(若有;详情页操作按钮用)</summary>
    public WfTodoItemOutput? MyPendingTask { get; init; }

    /// <summary>审批意见时间线(<c>wf_his_task</c>,按时间升序)</summary>
    public IReadOnlyList<WfHisTaskOutput> HisTasks { get; init; } = [];
}

/// <summary>事件流条目(<c>wf_history</c>)。</summary>
public record WfHistoryItemOutput
{
    public long Id { get; init; }
    public WfHistoryEventType EventType { get; init; }
    public string? NodeId { get; init; }
    public string? PayloadJson { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>待办列表项。</summary>
public record WfTodoItemOutput
{
    public long TaskId { get; init; }
    public long ActorId { get; init; }
    public long InstanceId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public WfSignMode SignMode { get; init; }
    public DateTime? DueTime { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? BusinessKey { get; init; }
    public long StarterUserId { get; init; }
    public string? VariablesJson { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>已办列表项(<c>wf_his_task</c>)。</summary>
public record WfDoneItemOutput
{
    public long HisTaskId { get; init; }
    public long InstanceId { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public WfTaskAction Action { get; init; }
    public string? Comment { get; init; }
    public long? TransferToUserId { get; init; }
    public long DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? BusinessKey { get; init; }
    public WfInstanceStatus InstanceStatus { get; init; }
    public long StarterUserId { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>历史任务(详情时间线)。</summary>
public record WfHisTaskOutput
{
    public long Id { get; init; }
    public string NodeId { get; init; } = "";
    public string? NodeName { get; init; }
    public long UserId { get; init; }
    public WfTaskAction Action { get; init; }
    public string? Comment { get; init; }
    public long? TransferToUserId { get; init; }
    public long DurationMs { get; init; }
    public DateTime CreateTime { get; init; }
}
