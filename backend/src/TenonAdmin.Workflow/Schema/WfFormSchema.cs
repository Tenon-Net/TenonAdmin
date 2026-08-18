using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// 简易动态表单 schema(定义级 <c>formSchema</c>)——M3 启用;M1/M2 恒为 <c>null</c> 或空字段列表。
/// 与实体列 <see cref="WfDefinitionVersion.FormSchema"/> 同步预留,避免 M3 加列迁移。
/// </summary>
public sealed class WfFormSchema
{
    /// <summary>表单 schema 自身版本(与流程模型 <see cref="WfModel.Version"/> 独立)。</summary>
    public int Version { get; set; } = 1;

    /// <summary>单列控件列表(~10 种;布局设计器/公式/联动/子表永久不做)。</summary>
    public List<WfFormField> Fields { get; set; } = [];
}

/// <summary>简易动态表单的一个控件描述(M3)。</summary>
public sealed class WfFormField
{
    /// <summary>字段键(条件表达式 / formPerms 引用)。</summary>
    public string Key { get; set; } = "";

    /// <summary>显示标签。</summary>
    public string Label { get; set; } = "";

    public WfFormFieldType Type { get; set; } = WfFormFieldType.Text;

    /// <summary>是否必填。</summary>
    public bool Required { get; set; }

    /// <summary>占位提示。</summary>
    public string? Placeholder { get; set; }

    /// <summary>控件附加参数(选项列表、精度、上传限制等);形状随 <see cref="Type"/> 变化。</summary>
    public Dictionary<string, JsonElement>? Props { get; set; }
}

/// <summary>
/// 审批节点字段权限一项(<c>props.formPerms[]</c>)。M1 预留空数组,M3 启用。
/// </summary>
public sealed class WfFormFieldPerm
{
    /// <summary>对应 <see cref="WfFormField.Key"/>。</summary>
    public string Field { get; set; } = "";

    /// <summary>可见/只读/可编辑。</summary>
    public WfFormPermAccess Access { get; set; } = WfFormPermAccess.Editable;
}
