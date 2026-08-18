namespace TenonAdmin.Workflow;

/// <summary>引擎命令标记。每条命令由 <see cref="IWorkflowEngine.ExecuteAsync"/> 在单一事务内消化。</summary>
public interface IWfCommand
{
}

/// <summary>引擎执行结果(事务已提交)。服务层据此发通知 / 返回 DTO。</summary>
public sealed class WfEngineResult
{
    public required long InstanceId { get; init; }

    public required WfInstanceStatus InstanceStatus { get; init; }

    /// <summary>本次新建的待办 Id(进入审批节点时);无则 null。</summary>
    public long? CreatedTaskId { get; init; }

    /// <summary>新建待办的办理人(通知用)。</summary>
    public IReadOnlyList<long> NewAssigneeUserIds { get; init; } = [];

    /// <summary>本次新抄送接收人。</summary>
    public IReadOnlyList<long> NewCcUserIds { get; init; } = [];
}

/// <summary>发起实例:建 instance + token,Agenda 从 start 自动推进到首个需停顿的节点。</summary>
public sealed class StartInstanceCmd : IWfCommand
{
    public required long DefinitionVersionId { get; init; }

    public required long StarterUserId { get; init; }

    /// <summary>发起人主属机构;写入 ApproverResolveContext,可空。</summary>
    public long? StarterOrgId { get; init; }

    public string? BusinessKey { get; init; }

    public string? VariablesJson { get; init; }

    /// <summary>按节点 Id 提交发起人自选审批人(仅 selfSelect Provider)。</summary>
    public IReadOnlyDictionary<string, List<long>>? SelectedUserIdsByNode { get; init; }
}

/// <summary>
/// 完成待办并推进 token。仅 <see cref="WfTaskAction.Approve"/>(前进)与
/// <see cref="WfTaskAction.Reject"/>(默认终止);转办走 <see cref="TransferTaskCmd"/>。
/// </summary>
public sealed class CompleteTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    public required long UserId { get; init; }

    public required WfTaskAction Action { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// 任务级转办:当前办理人把待办交给他人。不推进 token、不删待办;
/// 写 <c>wf_his_task</c>(Action=Transfer)后替换 actor。
/// </summary>
public sealed class TransferTaskCmd : IWfCommand
{
    public required long TaskId { get; init; }

    /// <summary>当前办理人(须为 Pending Approver)。</summary>
    public required long UserId { get; init; }

    /// <summary>转办目标用户。</summary>
    public required long ToUserId { get; init; }

    public string? Comment { get; init; }
}
