using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TenonAdmin.Workflow;

/// <summary>工作流内嵌 JSON 的客户端输出投影；持久化 JSON 仍保留原始数值类型。</summary>
internal static class WfClientJson
{
    private const long MaxSafeInteger = 9_007_199_254_740_991;

    private static readonly HashSet<string> HistoryScalarIdFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "toUserId", "forkId", "parentTokenId", "childTokenId",
        "parentNodeVisitId", "childEntryNodeVisitId", "adminUserId",
    };

    /// <summary>复制设计器模型，并只投影内置 provider 明确定义的 JsonElement Id 参数。</summary>
    public static WfModel ProjectModel(WfModel model)
    {
        var projected = WfModelJson.Deserialize(WfModelJson.Serialize(model)) ?? model;
        ProjectModelNodes(projected.Root);
        return projected;
    }

    /// <summary>投影版本快照字符串，同时保留未知字段与任意业务 JSON。</summary>
    public static string ProjectModelJson(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject model) return json;
            ProjectModelObject(model);
            return model.ToJsonString(WfModelJson.Options);
        }
        catch (JsonException)
        {
            // 损坏的存量快照沿用原输出；本投影不能改变既有读取失败边界。
            return json;
        }
    }

    /// <summary>只投影内核定义的节点执行完成消息；消费者自定义 outbox 正文保持原样。</summary>
    public static string? ProjectExecutionOutboxPayload(string messageType, string? payloadJson)
    {
        if (!string.Equals(
                messageType,
                WfOutboxStore.MessageTypeNodeExecutionCompleted,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(payloadJson))
            return payloadJson;

        try
        {
            if (JsonNode.Parse(payloadJson) is not JsonObject payload) return payloadJson;
            var changed = ProjectOutboxIdProperty(payload, "executionId")
                          | ProjectOutboxIdProperty(payload, "instanceId")
                          | ProjectOutboxIdProperty(payload, "tokenId")
                          | ProjectOutboxIdProperty(payload, "nodeVisitId")
                          | ProjectOutboxIdProperty(payload, "definitionVersionId");
            return changed ? payload.ToJsonString(WfModelJson.Options) : payloadJson;
        }
        catch (JsonException)
        {
            return payloadJson;
        }
    }

    /// <summary>投影历史 allowlist 中已知为 Id 的字段；其它字段原样返回。</summary>
    public static JsonElement ProjectHistoryValue(string propertyName, JsonElement value)
    {
        if (string.Equals(propertyName, "userIds", StringComparison.OrdinalIgnoreCase))
            return ProjectIdValue(value);
        return HistoryScalarIdFields.Contains(propertyName) ? ProjectIdValue(value) : value;
    }

    /// <summary>投影 schema 已明确为 user/attachment 的标量或数组值。</summary>
    public static JsonElement ProjectIdValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var projected = value.EnumerateArray().Select(ProjectIdValue).ToArray();
            return JsonSerializer.SerializeToElement(projected, WfModelJson.Options);
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var id)
            && !IsJavaScriptSafe(id))
        {
            return JsonSerializer.SerializeToElement(id.ToString(CultureInfo.InvariantCulture), WfModelJson.Options);
        }

        return value;
    }

    private static void ProjectAssigneeParams(WfAssignee? assignee)
    {
        if (assignee?.Params is not { } parameters) return;
        switch (assignee.Provider)
        {
            case ApproverProviderKeys.User:
                ProjectParameter(parameters, "userIds");
                ProjectParameter(parameters, "userId");
                break;
            case ApproverProviderKeys.Role:
                ProjectParameter(parameters, "roleIds");
                ProjectParameter(parameters, "roleId");
                break;
            case ApproverProviderKeys.Position:
                ProjectParameter(parameters, "positionId");
                ProjectParameter(parameters, "orgId");
                break;
        }
    }

    private static void ProjectModelNodes(WfNode? node)
    {
        for (var current = node; current is not null; current = current.Next)
        {
            ProjectAssigneeParams(current.Props?.Assignee);
            if (current.Conditions is not null)
            {
                foreach (var arm in current.Conditions)
                    ProjectModelNodes(arm.Next);
            }
            if (current.ParallelArms is not null)
            {
                foreach (var arm in current.ParallelArms)
                    ProjectModelNodes(arm.Next);
            }
        }
    }

    private static void ProjectParameter(IDictionary<string, JsonElement> parameters, string key)
    {
        if (parameters.TryGetValue(key, out var value))
            parameters[key] = ProjectIdValue(value);
    }

    private static void ProjectModelObject(JsonObject model)
    {
        ProjectIdProperty(model, "nobodyTransferUserId");
        if (model["root"] is JsonObject root) ProjectModelNode(root);
    }

    private static void ProjectModelNode(JsonObject node)
    {
        if (node["props"] is JsonObject props)
        {
            ProjectIdProperty(props, "nobodyTransferUserId");
            if (props["timeout"] is JsonObject timeout) ProjectIdProperty(timeout, "transferUserId");
            if (props["initiatorScope"] is JsonArray scope)
            {
                foreach (var item in scope.OfType<JsonObject>())
                    ProjectIdProperty(item, "id");
            }
            ProjectAssigneeObject(props["assignee"] as JsonObject);
        }

        if (node["conditions"] is JsonArray conditions)
        {
            foreach (var arm in conditions.OfType<JsonObject>())
                if (arm["next"] is JsonObject next) ProjectModelNode(next);
        }
        if (node["parallelArms"] is JsonArray parallelArms)
        {
            foreach (var arm in parallelArms.OfType<JsonObject>())
                if (arm["next"] is JsonObject next) ProjectModelNode(next);
        }
        if (node["next"] is JsonObject successor) ProjectModelNode(successor);
    }

    private static void ProjectAssigneeObject(JsonObject? assignee)
    {
        if (assignee?["provider"] is not JsonValue providerValue
            || !providerValue.TryGetValue<string>(out var provider)
            || assignee["params"] is not JsonObject parameters)
            return;

        switch (provider)
        {
            case ApproverProviderKeys.User:
                ProjectIdProperty(parameters, "userIds");
                ProjectIdProperty(parameters, "userId");
                break;
            case ApproverProviderKeys.Role:
                ProjectIdProperty(parameters, "roleIds");
                ProjectIdProperty(parameters, "roleId");
                break;
            case ApproverProviderKeys.Position:
                ProjectIdProperty(parameters, "positionId");
                ProjectIdProperty(parameters, "orgId");
                break;
        }
    }

    private static void ProjectIdProperty(JsonObject owner, string propertyName)
    {
        if (owner[propertyName] is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                ProjectIdNode(array, index);
            return;
        }

        if (owner[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var id)
            && !IsJavaScriptSafe(id))
            owner[propertyName] = id.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ProjectOutboxIdProperty(JsonObject payload, string propertyName)
    {
        if (payload[propertyName] is not JsonValue value
            || !value.TryGetValue<long>(out var id)
            || IsJavaScriptSafe(id))
            return false;
        payload[propertyName] = id.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static void ProjectIdNode(JsonArray array, int index)
    {
        if (array[index] is JsonValue value
            && value.TryGetValue<long>(out var id)
            && !IsJavaScriptSafe(id))
            array[index] = id.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsJavaScriptSafe(long value) => value is >= -MaxSafeInteger and <= MaxSafeInteger;
}
