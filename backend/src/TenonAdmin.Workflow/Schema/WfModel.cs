using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenonAdmin.Workflow;

/// <summary>
/// 流程定义 JSON schema v1 文档根——钉钉树模型的唯一权威(双前端经 API/DTO 消费同一形状)。
/// 存入 <see cref="WfDefinitionVersion.ModelJson"/>;发布即版本快照,实例永远跑发布时的副本。
/// </summary>
/// <remarks>
/// M1 节点:<c>start | approval | cc</c> 纯串行。<c>formSchema</c> / 节点 <c>formPerms</c> 字段已预留空值,
/// 避免 M3 加列或改 schema 形状。序列化须走 <see cref="WfModelJson"/>,保证枚举 camelCase 与草案一致。
/// </remarks>
public sealed class WfModel
{
    /// <summary>schema 版本;当前恒为 <see cref="WfModelJson.CurrentVersion"/>。</summary>
    public int Version { get; set; } = WfModelJson.CurrentVersion;

    /// <summary>树根(必须为 <see cref="WfNodeType.Start"/>)。</summary>
    public WfNode Root { get; set; } = new() { Type = WfNodeType.Start };

    /// <summary>
    /// M3 简易动态表单控件描述;M1/M2 恒 <c>null</c>。
    /// 与实体列 <see cref="WfDefinitionVersion.FormSchema"/> 同步预留。
    /// </summary>
    public WfFormSchema? FormSchema { get; set; }

    /// <summary>
    /// 开发者表单挂载点:消费者前端页面路径(如 <c>views/biz/leave/form</c>);
    /// 详情页动态挂载。与 <see cref="FormSchema"/> 二选一或皆空(纯审批)。
    /// </summary>
    public string? FormComponent { get; set; }

    /// <summary>流程级空审批人默认(节点未配时生效;再缺则落 <see cref="WorkflowOptions.Nobody"/>)。</summary>
    public WfNobodyAction? Nobody { get; set; }

    /// <summary><see cref="WfNobodyAction.Transfer"/> 时流程级指定用户。</summary>
    public long? NobodyTransferUserId { get; set; }
}

/// <summary>
/// <see cref="WfModel"/> ↔ JSON 的唯一入口(写入 <c>ModelJson</c> / 读回引擎)。
/// camelCase + 字符串枚举 + 写时忽略 null,与设计草案示例逐字段对齐。
/// </summary>
public static class WfModelJson
{
    /// <summary>当前 schema 版本号。</summary>
    public const int CurrentVersion = 1;

    /// <summary>与双前端约定一致的序列化选项;读写 <c>ModelJson</c> 必须用此实例。</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(WfModel model) =>
        JsonSerializer.Serialize(model, Options);

    public static WfModel? Deserialize(string json) =>
        JsonSerializer.Deserialize<WfModel>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
