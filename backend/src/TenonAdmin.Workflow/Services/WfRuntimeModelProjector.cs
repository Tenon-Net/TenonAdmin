namespace TenonAdmin.Workflow;

/// <summary>把内部发布模型投影为运行时客户端允许读取的最小模型。</summary>
internal static class WfRuntimeModelProjector
{
    public static WfRuntimeModelOutput? Project(WfModel? model) => model is null
        ? null
        : new WfRuntimeModelOutput
        {
            Version = model.Version,
            Root = ProjectNode(model.Root),
            FormSchema = model.FormSchema,
            FormComponent = model.FormComponent,
        };

    private static WfRuntimeNodeOutput ProjectNode(WfNode node) => new()
    {
        Id = node.Id,
        Type = node.Type,
        Name = node.Name,
        Props = ProjectProps(node.Props),
        Conditions = node.Conditions?.ConvertAll(ProjectBranchArm),
        ParallelArms = node.ParallelArms?.ConvertAll(ProjectParallelArm),
        Next = node.Next is null ? null : ProjectNode(node.Next),
    };

    private static WfRuntimeBranchArmOutput ProjectBranchArm(WfBranchArm arm) => new()
    {
        Id = arm.Id,
        Name = arm.Name,
        IsDefault = arm.IsDefault,
        Next = arm.Next is null ? null : ProjectNode(arm.Next),
    };

    private static WfRuntimeParallelArmOutput ProjectParallelArm(WfParallelArmDefinition arm) => new()
    {
        Id = arm.Id,
        Name = arm.Name,
        Next = arm.Next is null ? null : ProjectNode(arm.Next),
    };

    private static WfRuntimeNodePropsOutput? ProjectProps(WfNodeProps? props) => props is null
        ? null
        : new WfRuntimeNodePropsOutput
        {
            Assignee = props.Assignee is null
                ? null
                : new WfRuntimeAssigneeOutput { Provider = props.Assignee.Provider },
            ReturnPolicy = props.ReturnPolicy,
            ButtonLabels = props.ButtonLabels is null
                ? null
                : new WfButtonLabels
                {
                    Approve = props.ButtonLabels.Approve,
                    Reject = props.ButtonLabels.Reject,
                    Return = props.ButtonLabels.Return,
                    Transfer = props.ButtonLabels.Transfer,
                    Delegate = props.ButtonLabels.Delegate,
                    Urge = props.ButtonLabels.Urge,
                },
            FormPerms = props.FormPerms?.ConvertAll(permission => new WfFormFieldPerm
            {
                Field = permission.Field,
                Access = permission.Access,
            }),
        };
}
