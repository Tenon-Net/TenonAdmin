using System.Globalization;
using System.Text;
using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置流程定义服务:草稿落 <see cref="DraftVersion"/>(=0),发布即不可变快照。
/// </summary>
public class WfDefinitionService(
    IRepository<WfDefinition> definitions,
    IRepository<WfDefinitionVersion> versions,
    IEnumerable<IApproverProvider> approverProviders,
    TimeProvider timeProvider,
    ICurrentUser? currentUser = null) : IWfDefinitionService
{
    /// <summary>未发布工作副本版本号;已发布快照从 1 起递增。</summary>
    public const int DraftVersion = 0;

    /// <inheritdoc />
    public virtual async Task<PagedList<WfDefinition>> PageAsync(
        WfDefinitionPageInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await definitions.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(input.Name), d => d.Name.Contains(input.Name!))
            .WhereIF(!string.IsNullOrWhiteSpace(input.GroupName), d => d.GroupName == input.GroupName)
            .WhereIF(input.Status.HasValue, d => d.Status == input.Status!.Value)
            .ToPagedListAsync(input, q => q.OrderBy(d => d.Id, OrderByType.Desc));
    }

    /// <inheritdoc />
    public virtual async Task<WfDefinitionDetailOutput> GetAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        var draft = await GetOrCreateDraftAsync(def.Id, cancellationToken);
        return MapDetail(def, draft);
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(
        WfDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = NormalizeName(input.Name);
        ValidateMetadata(input.Icon, input.GroupName);
        var modelJson = SerializeModel(input.Model ?? CreateDefaultModel());

        var db = definitions.Db;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = new WfDefinition
            {
                Name = name,
                Icon = input.Icon,
                GroupName = input.GroupName,
                Status = WfDefinitionStatus.Draft,
                CurrentVersion = 0,
            };
            await db.Insertable(def).ExecuteCommandAsync();

            var draft = new WfDefinitionVersion
            {
                DefinitionId = def.Id,
                Version = DraftVersion,
                ModelJson = modelJson,
                FormSchema = null,
                PublishTime = null,
                PublishUserId = null,
            };
            await db.Insertable(draft).ExecuteCommandAsync();
            return def.Id;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        return tran.Data;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(
        WfDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

        var name = NormalizeName(input.Name);
        ValidateMetadata(input.Icon, input.GroupName);
        var modelJson = SerializeModel(input.Model ?? CreateDefaultModel());

        var db = definitions.Db;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = await db.Queryable<WfDefinition>().InSingleAsync(input.Id)
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

            def.Name = name;
            def.Icon = input.Icon;
            def.GroupName = input.GroupName;
            await db.Updateable(def)
                .UpdateColumns(d => new { d.Name, d.Icon, d.GroupName, d.UpdateTime, d.UpdateUserId })
                .ExecuteCommandAsync();

            var draft = await db.Queryable<WfDefinitionVersion>()
                .Where(v => v.DefinitionId == def.Id && v.Version == DraftVersion)
                .FirstAsync();
            if (draft is null)
            {
                draft = new WfDefinitionVersion
                {
                    DefinitionId = def.Id,
                    Version = DraftVersion,
                    ModelJson = modelJson,
                };
                await db.Insertable(draft).ExecuteCommandAsync();
            }
            else
            {
                draft.ModelJson = modelJson;
                await db.Updateable(draft)
                    .UpdateColumns(v => new { v.ModelJson, v.UpdateTime, v.UpdateUserId })
                    .ExecuteCommandAsync();
            }
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
    }

    /// <inheritdoc />
    public virtual async Task<int> PublishAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

        var db = definitions.Db;
        var publishUserId = currentUser?.UserId;
        var now = timeProvider.GetLocalNow().DateTime;

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            var def = await db.Queryable<WfDefinition>().InSingleAsync(id)
                      ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);

            var draft = await db.Queryable<WfDefinitionVersion>()
                .Where(v => v.DefinitionId == def.Id && v.Version == DraftVersion)
                .FirstAsync();
            if (draft is null || string.IsNullOrWhiteSpace(draft.ModelJson))
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?> { ["reason"] = "draftEmpty" });

            var model = WfModelJson.Deserialize(draft.ModelJson)
                        ?? throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid);
            ValidateModelForPublish(model);

            var nextVersion = def.CurrentVersion + 1;
            var snapshot = new WfDefinitionVersion
            {
                DefinitionId = def.Id,
                Version = nextVersion,
                ModelJson = WfModelJson.Serialize(model),
                FormSchema = model.FormSchema is null ? null : System.Text.Json.JsonSerializer.Serialize(model.FormSchema, WfModelJson.Options),
                PublishTime = now,
                PublishUserId = publishUserId,
            };
            await db.Insertable(snapshot).ExecuteCommandAsync();

            def.CurrentVersion = nextVersion;
            def.Status = WfDefinitionStatus.Published;
            await db.Updateable(def)
                .UpdateColumns(d => new { d.CurrentVersion, d.Status, d.UpdateTime, d.UpdateUserId })
                .ExecuteCommandAsync();

            return nextVersion;
        });

        if (!tran.IsSuccess)
            throw tran.ErrorException ?? WorkflowErrorCode.Exception(WorkflowErrorCode.OperationFailed);
        return tran.Data;
    }

    /// <inheritdoc />
    public virtual async Task DisableAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        if (def.Status == WfDefinitionStatus.Disabled)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionStatusConflict,
                new Dictionary<string, object?> { ["status"] = "disabled" });

        def.Status = WfDefinitionStatus.Disabled;
        await definitions.UpdateAsync(def);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = await RequireDefinitionAsync(id);
        var db = definitions.Db;

        var versionIds = await db.Queryable<WfDefinitionVersion>()
            .Where(v => v.DefinitionId == def.Id)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count > 0)
        {
            // 跨机构看在途单:本机构看不见的运行中单据,也不能把定义抽掉。
            var hasRunning = await db.Queryable<WfInstance>()
                .ClearFilter<IOrgScoped>()
                .Where(i => versionIds.Contains(i.DefinitionVersionId) && i.Status == WfInstanceStatus.Running)
                .AnyAsync();
            if (hasRunning)
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionHasRunningInstances);
        }

        await definitions.DeleteAsync(def.Id);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<WfDefinitionVersionOutput>> ListVersionsAsync(
        long definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RequireDefinitionAsync(definitionId);

        var list = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId && v.Version >= 1)
            .OrderBy(v => v.Version, OrderByType.Desc)
            .ToListAsync();

        return list.Select(v => new WfDefinitionVersionOutput
        {
            Id = v.Id,
            DefinitionId = v.DefinitionId,
            Version = v.Version,
            ModelJson = v.ModelJson,
            FormSchema = v.FormSchema,
            PublishTime = v.PublishTime,
            PublishUserId = v.PublishUserId,
        }).ToList();
    }

    /// <summary>
    /// 校验可发布模型(树语义,M2a/M3b-0):根为 start;节点类型限 start|approval|cc|branch|parallel|webhook|aiDecision;
    /// 节点 Id 跨整棵树(含分支臂内)非空且唯一;branch/parallel 节点的臂配置合法
    /// (见 <see cref="ValidateBranch"/>/<see cref="ValidateParallel"/>);跳转目标引用完整
    /// (见 <see cref="ValidateNodeReferences"/>)。
    /// </summary>
    protected virtual void ValidateModelForPublish(WfModel model)
    {
        if (model.Root is null || model.Root.Type != WfNodeType.Start)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "rootNotStart" });
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var providerKeys = approverProviders.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        ValidateChain(model.Root, seen, providerKeys);
        ValidateFormModel(model);
        ValidateNodeReferences(model);
    }

    private const int FormSchemaMaxBytes = 64 * 1024;
    private const int FormSchemaMaxFields = 50;

    /// <summary>
    /// 内置表单发布校验(M3):表单 schema 与消费者挂载点互斥;旧的无 schema 模型保留。
    /// 只有 approval 节点在存在内置 schema 时解释 formPerms,避免破坏 M1/M2 草案。
    /// </summary>
    protected virtual void ValidateFormModel(WfModel model)
    {
        var formComponent = model.FormComponent?.Trim();
        model.FormComponent = string.IsNullOrEmpty(formComponent) ? null : formComponent;
        if (formComponent is not null)
        {
            ValidateLength(formComponent, 256, "formComponent");
            if (ContainsDisallowedControlCharacter(formComponent, allowNewlines: false))
                ThrowFormModelInvalid("formComponentControlChars");
        }

        var schema = model.FormSchema;
        if (schema is null) return;
        if (formComponent is not null)
            ThrowFormModelInvalid("formSchemaAndComponentMutuallyExclusive");

        var serialized = JsonSerializer.Serialize(schema, WfModelJson.Options);
        if (Encoding.UTF8.GetByteCount(serialized) > FormSchemaMaxBytes)
            ThrowFormModelInvalid("formSchemaTooLarge");
        if (schema.Version != WfModelJson.CurrentVersion)
            ThrowFormModelInvalid("formSchemaVersionInvalid");
        if (schema.Fields is not { Count: > 0 })
            ThrowFormModelInvalid("formSchemaFieldsRequired");
        if (schema.Fields.Count > FormSchemaMaxFields)
            ThrowFormModelInvalid("formSchemaFieldsTooMany");

        var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            ValidateFormField(field);
            if (!fieldKeys.Add(field.Key))
                ThrowFormModelInvalid("formFieldKeyDuplicate");
        }

        foreach (var node in WfModelIndex.Build(model).Nodes)
        {
            if (node.Type != WfNodeType.Approval || node.Props?.FormPerms is null) continue;
            var permissionKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var permission in node.Props.FormPerms)
            {
                if (permission is null)
                    ThrowFormModelInvalid("formPermInvalid", node);
                if (!permissionKeys.Add(permission.Field))
                    ThrowFormModelInvalid("formPermFieldDuplicate", node);
                if (!fieldKeys.Contains(permission.Field))
                    ThrowFormModelInvalid("formPermFieldUnknown", node);
                if (permission.Access is not (WfFormPermAccess.Hidden or WfFormPermAccess.Readonly or WfFormPermAccess.Editable))
                    ThrowFormModelInvalid("formPermAccessInvalid", node);
            }
        }
    }

    protected virtual void ValidateFormField(WfFormField? field)
    {
        if (field is null)
            ThrowFormModelInvalid("formFieldInvalid");
        if (string.IsNullOrEmpty(field.Key) || field.Key.Length > 64 || !IsFormFieldKey(field.Key))
            ThrowFormModelInvalid("formFieldKeyInvalid");

        field.Label = field.Label?.Trim() ?? "";
        if (field.Label.Length is < 1 or > 128 || ContainsDisallowedControlCharacter(field.Label, allowNewlines: false))
            ThrowFormModelInvalid("formFieldLabelInvalid");

        if (field.Placeholder is not null)
        {
            field.Placeholder = field.Placeholder.Trim();
            if (field.Placeholder.Length > 256 || ContainsDisallowedControlCharacter(field.Placeholder, allowNewlines: false))
                ThrowFormModelInvalid("formFieldPlaceholderInvalid");
            if (field.Placeholder.Length == 0) field.Placeholder = null;
        }

        ValidateFormFieldProps(field);
    }

    protected virtual void ValidateFormFieldProps(WfFormField field)
    {
        if (field.Type is not (WfFormFieldType.Text or WfFormFieldType.Textarea or WfFormFieldType.Number
            or WfFormFieldType.Money or WfFormFieldType.Date or WfFormFieldType.Datetime or WfFormFieldType.Select
            or WfFormFieldType.MultiSelect or WfFormFieldType.User or WfFormFieldType.Attachment))
            ThrowFormModelInvalid("formFieldTypeInvalid");

        var props = field.Props;
        if (props is null || props.Count == 0)
        {
            if (field.Type is WfFormFieldType.Select or WfFormFieldType.MultiSelect)
                ThrowFormModelInvalid("formFieldOptionsRequired");
            return;
        }

        var allowed = field.Type switch
        {
            WfFormFieldType.Text => new[] { "maxLength" },
            WfFormFieldType.Textarea => new[] { "maxLength", "rows" },
            WfFormFieldType.Number => new[] { "min", "max", "precision" },
            WfFormFieldType.Money => new[] { "min", "max" },
            WfFormFieldType.Date or WfFormFieldType.Datetime => new[] { "min", "max" },
            WfFormFieldType.Select or WfFormFieldType.MultiSelect => new[] { "options", "maxSelected" },
            WfFormFieldType.User => new[] { "multiple", "maxSelected" },
            WfFormFieldType.Attachment => new[] { "multiple", "maxCount", "accept", "maxSizeMb" },
            _ => null,
        };
        if (allowed is null) ThrowFormModelInvalid("formFieldTypeInvalid");
        foreach (var key in props.Keys)
        {
            if (!allowed!.Contains(key, StringComparer.Ordinal))
                ThrowFormModelInvalid("formFieldPropUnknown");
        }

        switch (field.Type)
        {
            case WfFormFieldType.Text:
                ValidateOptionalInt(props, "maxLength", 1, 256);
                break;
            case WfFormFieldType.Textarea:
                ValidateOptionalInt(props, "maxLength", 1, 4000);
                ValidateOptionalInt(props, "rows", 2, 8);
                break;
            case WfFormFieldType.Number:
                ValidateNumericRange(props, money: false);
                ValidateOptionalInt(props, "precision", 0, 6);
                break;
            case WfFormFieldType.Money:
                ValidateNumericRange(props, money: true);
                break;
            case WfFormFieldType.Date:
                ValidateDateRange(props);
                break;
            case WfFormFieldType.Datetime:
                ValidateDateTimeRange(props);
                break;
            case WfFormFieldType.Select:
            case WfFormFieldType.MultiSelect:
                var optionCount = ValidateOptions(props);
                if (field.Type == WfFormFieldType.MultiSelect)
                    ValidateOptionalInt(props, "maxSelected", 1, Math.Min(100, optionCount));
                else if (props.ContainsKey("maxSelected"))
                    ThrowFormModelInvalid("formFieldPropInvalid");
                break;
            case WfFormFieldType.User:
                ValidateUserProps(props);
                break;
            case WfFormFieldType.Attachment:
                ValidateAttachmentProps(props);
                break;
        }
    }

    private static bool IsFormFieldKey(string key) =>
        char.IsAsciiLetter(key[0]) && key[1..].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    protected virtual void ValidateOptionalInt(
        IReadOnlyDictionary<string, JsonElement> props,
        string key,
        int min,
        int max)
    {
        if (!props.TryGetValue(key, out var value)) return;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
            ThrowFormModelInvalid("formFieldPropInvalid");
    }

    protected virtual void ValidateNumericRange(IReadOnlyDictionary<string, JsonElement> props, bool money)
    {
        decimal? min = ReadOptionalDecimal(props, "min", money);
        decimal? max = ReadOptionalDecimal(props, "max", money);
        if (min.HasValue && max.HasValue && min > max)
            ThrowFormModelInvalid("formFieldRangeInvalid");
    }

    private static decimal? ReadOptionalDecimal(
        IReadOnlyDictionary<string, JsonElement> props,
        string key,
        bool money)
    {
        if (!props.TryGetValue(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
            throw FormValidationException("formFieldPropInvalid");
        if (money && decimal.Round(number, 2) != number)
            throw FormValidationException("formFieldPropInvalid");
        return number;
    }

    protected virtual void ValidateDateRange(IReadOnlyDictionary<string, JsonElement> props)
    {
        var min = ReadOptionalDate(props, "min");
        var max = ReadOptionalDate(props, "max");
        if (min.HasValue && max.HasValue && min > max)
            ThrowFormModelInvalid("formFieldRangeInvalid");
    }

    protected virtual void ValidateDateTimeRange(IReadOnlyDictionary<string, JsonElement> props)
    {
        var min = ReadOptionalDateTime(props, "min");
        var max = ReadOptionalDateTime(props, "max");
        if (min.HasValue && max.HasValue && min > max)
            ThrowFormModelInvalid("formFieldRangeInvalid");
    }

    private static DateOnly? ReadOptionalDate(IReadOnlyDictionary<string, JsonElement> props, string key)
    {
        if (!props.TryGetValue(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw FormValidationException("formFieldPropInvalid");
        return date;
    }

    private static DateTimeOffset? ReadOptionalDateTime(IReadOnlyDictionary<string, JsonElement> props, string key)
    {
        if (!props.TryGetValue(key, out var value)) return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var separator = text?.IndexOf('T', StringComparison.Ordinal) ?? -1;
        var timePart = separator >= 0 ? text![(separator + 1)..] : "";
        var hasOffset = timePart.EndsWith('Z') || timePart.Contains('+') || timePart.Contains('-');
        if (text is null || separator < 0 || !hasOffset
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            throw FormValidationException("formFieldPropInvalid");
        return dateTime;
    }

    protected virtual int ValidateOptions(IReadOnlyDictionary<string, JsonElement> props)
    {
        if (!props.TryGetValue("options", out var value) || value.ValueKind != JsonValueKind.Array)
            ThrowFormModelInvalid("formFieldOptionsRequired");
        var options = value.EnumerateArray().ToArray();
        if (options.Length is < 1 or > 100)
            ThrowFormModelInvalid("formFieldOptionsInvalid");
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            if (option.ValueKind != JsonValueKind.Object)
                ThrowFormModelInvalid("formFieldOptionsInvalid");
            var properties = option.EnumerateObject().ToArray();
            if (properties.Length != 2)
                ThrowFormModelInvalid("formFieldOptionsInvalid");
            JsonElement? label = null;
            JsonElement? optionValue = null;
            foreach (var property in properties)
            {
                if (property.NameEquals("label") && label is null)
                    label = property.Value;
                else if (property.NameEquals("value") && optionValue is null)
                    optionValue = property.Value;
                else
                    ThrowFormModelInvalid("formFieldOptionsInvalid");
            }
            if (label is null || optionValue is null
                || label.Value.ValueKind != JsonValueKind.String || optionValue.Value.ValueKind != JsonValueKind.String)
                ThrowFormModelInvalid("formFieldOptionsInvalid");
            var labelText = label.Value.GetString()?.Trim() ?? "";
            var valueText = optionValue.Value.GetString()?.Trim() ?? "";
            if (labelText.Length is < 1 or > 128 || valueText.Length is < 1 or > 64
                || ContainsDisallowedControlCharacter(labelText, false)
                || ContainsDisallowedControlCharacter(valueText, false))
                ThrowFormModelInvalid("formFieldOptionsInvalid");
            if (!values.Add(valueText))
                ThrowFormModelInvalid("formFieldOptionDuplicate");
        }
        return options.Length;
    }

    protected virtual void ValidateUserProps(IReadOnlyDictionary<string, JsonElement> props)
    {
        var multiple = ReadOptionalBool(props, "multiple") ?? false;
        if (props.ContainsKey("maxSelected"))
        {
            if (!multiple) ThrowFormModelInvalid("formFieldPropInvalid");
            ValidateOptionalInt(props, "maxSelected", 1, 100);
        }
    }

    protected virtual void ValidateAttachmentProps(IReadOnlyDictionary<string, JsonElement> props)
    {
        var multiple = ReadOptionalBool(props, "multiple") ?? false;
        if (props.TryGetValue("maxCount", out var maxCount))
        {
            ValidateOptionalInt(props, "maxCount", 1, 20);
            if (!multiple && maxCount.GetInt32() != 1) ThrowFormModelInvalid("formFieldPropInvalid");
        }
        if (props.TryGetValue("accept", out var accept))
        {
            var acceptText = accept.ValueKind == JsonValueKind.String ? accept.GetString() : null;
            if (acceptText is null || acceptText.Length > 256
                || ContainsDisallowedControlCharacter(acceptText, false)
                || !IsAttachmentAccept(acceptText))
                ThrowFormModelInvalid("formFieldPropInvalid");
        }
        ValidateOptionalInt(props, "maxSizeMb", 1, 100);
    }

    private static bool IsAttachmentAccept(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(token => token.Length > 1
            && token[0] == '.' && token[1..].All(char.IsAsciiLetterOrDigit));
    }

    private static bool? ReadOptionalBool(IReadOnlyDictionary<string, JsonElement> props, string key)
    {
        if (!props.TryGetValue(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            throw FormValidationException("formFieldPropInvalid");
        return value.GetBoolean();
    }

    private static AdminException FormValidationException(string reason) =>
        WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
            new Dictionary<string, object?> { ["reason"] = reason });

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    protected virtual void ThrowFormModelInvalid(string reason, WfNode? node = null) =>
        throw WorkflowErrorCode.Exception(
            WorkflowErrorCode.ModelInvalid,
            node is null
                ? new Dictionary<string, object?> { ["reason"] = reason }
                : new Dictionary<string, object?> { ["reason"] = reason, ["nodeId"] = node.Id });

    /// <summary>
    /// 跳转目标与超时目标的引用完整性(M2b):<c>onReject=toNode</c> ⇒ <see cref="WfNodeProps.RejectToNodeId"/>
    /// 非空且指向全树存在的节点;<c>returnPolicy=node</c> ⇒ <see cref="WfNodeProps.ReturnToNodeId"/> 同理;
    /// <c>timeout.action=transfer</c>(且 <c>hours &gt; 0</c>)⇒ <see cref="WfTimeout.TransferUserId"/> 为正。
    /// <para>必须独立于 <see cref="ValidateChain"/> 单独走一趟:跳转目标可能在当前遍历位置<b>之后</b>、
    /// 或在另一条分支臂上,只查已 <c>seen</c> 的集合会把合法的前向/跨臂引用误判成非法。这里复用
    /// <see cref="WfModelIndex"/> 的整树索引,不再手写第三次遍历。</para>
    /// <para>不校验会漏出「拒绝动作永久不可用」的定义:运行到该节点点拒绝才抛
    /// <see cref="WorkflowErrorCode.ModelInvalid"/> 并回滚,而那个码的语义(根非 start / 缺节点)
    /// 完全看不出是配置问题。</para>
    /// </summary>
    protected virtual void ValidateNodeReferences(WfModel model)
    {
        var index = WfModelIndex.Build(model);
        foreach (var node in index.Nodes)
        {
            if (node.Props?.OnReject == WfRejectAction.ToNode)
            {
                RequireNodeReference(index, node, node.Props.RejectToNodeId, "rejectToNodeId");
                ValidateRejectParallelBoundary(index, node, node.Props.RejectToNodeId!);
            }

            if (node.Props?.ReturnPolicy == WfReturnPolicy.Node)
            {
                RequireNodeReference(index, node, node.Props.ReturnToNodeId, "returnToNodeId");
            }

            // 超时自动转办缺目标是「永久失败」形态:待办到期后每一拍都失败一次,直到有人手工办掉。
            // 运行期只能计数 + 日志,发布期拒了才是根治。复用 48002 + reason,零新增错误码。
            if (node.Props?.Timeout is { Hours: > 0, Action: WfTimeoutAction.Transfer } timeout
                && timeout.TransferUserId is not > 0)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "timeoutTransferUserIdInvalid",
                        ["nodeId"] = node.Id,
                    });
            }
        }
    }

    /// <summary>拒绝跳转不得进入、离开或跨越并行臂。</summary>
    protected virtual void ValidateRejectParallelBoundary(WfModelIndex index, WfNode node, string targetNodeId)
    {
        var sourceParallel = index.FindEnclosingParallel(node.Id);
        var targetParallel = index.FindEnclosingParallel(targetNodeId);
        var sourceArm = index.FindEnclosingParallelArm(node.Id);
        var targetArm = index.FindEnclosingParallelArm(targetNodeId);
        if (!ReferenceEquals(sourceParallel, targetParallel) || !ReferenceEquals(sourceArm, targetArm))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?>
                {
                    ["reason"] = "rejectToNodeOutsideParallelArm",
                    ["nodeId"] = node.Id,
                    ["targetNodeId"] = targetNodeId,
                });
        }
    }

    /// <summary>目标节点 Id 非空且能在整树索引里解析;否则抛 <c>&lt;field&gt;Invalid</c>。</summary>
    protected virtual void RequireNodeReference(
        WfModelIndex index,
        WfNode node,
        string? targetNodeId,
        string field)
    {
        ValidateLength(targetNodeId, 64, field);
        if (string.IsNullOrWhiteSpace(targetNodeId) || index.Find(targetNodeId) is null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?>
                {
                    ["reason"] = $"{field}Invalid",
                    ["nodeId"] = node.Id,
                    ["targetNodeId"] = targetNodeId,
                });
        }
    }

    /// <summary>沿 <c>.Next</c> 走一条链,逐节点校验;<paramref name="seen"/> 跨整棵树共享,
    /// 遇到 branch 节点额外递归校验它的每条臂。</summary>
    protected virtual void ValidateChain(WfNode? node, HashSet<string> seen, HashSet<string> providerKeys)
    {
        for (var n = node; n is not null; n = n.Next)
        {
            ValidateNode(n, seen, providerKeys);

            if (n.Type == WfNodeType.Branch)
            {
                ValidateBranch(n, seen, providerKeys);
            }
            else if (n.Type == WfNodeType.Parallel)
            {
                ValidateParallel(n, seen, providerKeys);
            }
        }
    }

    /// <summary>
    /// 单节点公共校验:类型白名单、非 branch 节点不得携带 <see cref="WfNode.Conditions"/>、
    /// Id 非空唯一、长度、审批人 Provider 已注册。
    /// </summary>
    protected virtual void ValidateNode(WfNode node, HashSet<string> seen, HashSet<string> providerKeys)
    {
        if (node.Type is not (WfNodeType.Start or WfNodeType.Approval or WfNodeType.Cc or WfNodeType.Branch or WfNodeType.Parallel
            or WfNodeType.Webhook or WfNodeType.AiDecision))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.NodeTypeUnsupported,
                new Dictionary<string, object?> { ["type"] = node.Type.ToString() });
        }

        if (node.Type != WfNodeType.Branch && node.Conditions is { Count: > 0 })
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "conditionsOnNonBranch", ["nodeId"] = node.Id });
        }

        if (node.Type != WfNodeType.Parallel && node.ParallelArms is not null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "parallelArmsOnNonParallel", ["nodeId"] = node.Id });
        }

        if (node.Type == WfNodeType.Parallel && node.Conditions is not null)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "conditionsOnParallel", ["nodeId"] = node.Id });
        }

        if (string.IsNullOrWhiteSpace(node.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "emptyNodeId" });
        }

        ValidateLength(node.Id, 64, "nodeId");
        ValidateLength(node.Name, 128, "nodeName");

        if (node.Type != WfNodeType.AiDecision && node.Props?.AllPassRatio is { } ratio)
        {
            if (node.Type != WfNodeType.Approval)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "allPassRatioUnsupported",
                        ["nodeId"] = node.Id,
                    });
            }

            if (node.Props.Mode == WfApprovalMode.All && ratio is < 1 or > 100)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "allPassRatioOutOfRange",
                        ["nodeId"] = node.Id,
                    });
            }

            if ((node.Props.Mode == WfApprovalMode.Seq
                 || node.Props.Assignee?.Provider == ApproverProviderKeys.MultiLeader)
                && ratio != 100)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "sequentialAllPassRatioInvalid",
                        ["nodeId"] = node.Id,
                    });
            }
        }

        if (node.Type is WfNodeType.Webhook or WfNodeType.AiDecision
            && node.Props?.MaxAttempts is { } maxAttempts
            && !WorkflowOptions.IsValidMaxAttempts(maxAttempts))
        {
            throw WorkflowErrorCode.Exception(
                WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?>
                {
                    ["reason"] = "maxAttemptsOutOfRange",
                    ["nodeId"] = node.Id,
                    ["value"] = maxAttempts,
                    ["min"] = WorkflowOptions.MinMaxAttempts,
                    ["max"] = WorkflowOptions.MaxMaxAttempts,
                });
        }

        if (node.Type == WfNodeType.AiDecision)
            ValidateAiDecisionNode(node, providerKeys);

        if (!seen.Add(node.Id))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "duplicateNodeId", ["nodeId"] = node.Id });
        }

        var provider = node.Props?.Assignee?.Provider;
        if (node.Type is WfNodeType.Approval or WfNodeType.Cc)
        {
            if (!string.IsNullOrWhiteSpace(provider))
            {
                ValidateLength(provider, 64, "provider");
                if (!providerKeys.Contains(provider))
                {
                    throw WorkflowErrorCode.Exception(WorkflowErrorCode.ProviderNotRegistered,
                        new Dictionary<string, object?> { ["provider"] = provider, ["nodeId"] = node.Id });
                }
            }
        }
    }

    /// <summary>
    /// AI Decision 发布配置：只允许指令、显式输入字段白名单以及既有人工兜底/重试预算。
    /// 不回显指令或字段原文，避免把可疑配置写入错误 envelope。
    /// </summary>
    protected virtual void ValidateAiDecisionNode(WfNode node, HashSet<string> providerKeys)
    {
        var props = node.Props;
        if (props is not null && HasUnsupportedAiDecisionProps(props))
            ThrowAiDecisionModelInvalid(node, "aiPropsUnsupported");

        var instructions = props?.AiInstructions;
        if (string.IsNullOrWhiteSpace(instructions))
            ThrowAiDecisionModelInvalid(node, "aiInstructionsRequired");
        if (instructions.Length > 2000)
            ThrowAiDecisionModelInvalid(node, "aiInstructionsTooLong");
        if (ContainsDisallowedControlCharacter(instructions, allowNewlines: true))
            ThrowAiDecisionModelInvalid(node, "aiInstructionsControlChars");

        var inputFields = props?.AiInputFields;
        if (inputFields is not { Count: > 0 })
            ThrowAiDecisionModelInvalid(node, "aiInputFieldsRequired");
        if (inputFields.Count > 32)
            ThrowAiDecisionModelInvalid(node, "aiInputFieldsTooMany");

        var seenInputFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inputField in inputFields)
        {
            if (string.IsNullOrWhiteSpace(inputField))
                ThrowAiDecisionModelInvalid(node, "aiInputFieldBlank");
            if (!string.Equals(inputField, inputField.Trim(), StringComparison.Ordinal))
                ThrowAiDecisionModelInvalid(node, "aiInputFieldPadded");
            if (inputField.Length > 64)
                ThrowAiDecisionModelInvalid(node, "aiInputFieldTooLong");
            if (ContainsDisallowedControlCharacter(inputField, allowNewlines: false))
                ThrowAiDecisionModelInvalid(node, "aiInputFieldControlChars");
            if (!seenInputFields.Add(inputField))
                ThrowAiDecisionModelInvalid(node, "aiInputFieldDuplicate");
        }

        var provider = props?.Assignee?.Provider;
        if (string.IsNullOrWhiteSpace(provider))
            ThrowAiDecisionModelInvalid(node, "aiAssigneeProviderRequired");
        if (provider.Length > 64)
            ThrowAiDecisionModelInvalid(node, "aiAssigneeProviderTooLong");
        if (!providerKeys.Contains(provider))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ProviderNotRegistered,
                new Dictionary<string, object?>
                {
                    ["reason"] = "aiAssigneeProviderNotRegistered",
                    ["provider"] = provider,
                    ["nodeId"] = node.Id,
                });
        }
    }

    /// <summary>
    /// AI 节点只接受人工兜底、重试预算、指令与输入字段白名单。仅检查已映射且非空的已知字段，
    /// 未知 JSON 字段仍沿用 schema v1 的忽略策略。
    /// </summary>
    protected virtual bool HasUnsupportedAiDecisionProps(WfNodeProps props) =>
        typeof(WfNodeProps)
            .GetProperties(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Any(property => property.Name is not (
                    nameof(WfNodeProps.Assignee)
                    or nameof(WfNodeProps.MaxAttempts)
                    or nameof(WfNodeProps.AiInstructions)
                    or nameof(WfNodeProps.AiInputFields))
                && property.GetValue(props) is not null);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    protected virtual void ThrowAiDecisionModelInvalid(WfNode node, string reason)
    {
        throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
            new Dictionary<string, object?> { ["reason"] = reason, ["nodeId"] = node.Id });
    }

    protected virtual bool ContainsDisallowedControlCharacter(string value, bool allowNewlines) =>
        value.Any(character => char.IsControl(character)
            && (!allowNewlines || character is not ('\r' or '\n')));

    /// <summary>
    /// branch 专属校验:臂非空(<c>branchNoArms</c>)、臂 Id 非空(<c>emptyArmId</c>)且本 branch 内唯一
    /// (<c>duplicateArmId</c>)、非默认臂须有 <see cref="WfBranchArm.Expr"/>(<c>branchArmWithoutExpr</c>)、
    /// 恰好一条默认臂(<c>branchDefaultArmCount</c>);再递归校验每条臂的子链。
    /// </summary>
    protected virtual void ValidateBranch(WfNode branch, HashSet<string> seen, HashSet<string> providerKeys)
    {
        if (branch.Conditions is not { Count: > 0 } arms)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "branchNoArms", ["nodeId"] = branch.Id });
        }

        var armIds = new HashSet<string>(StringComparer.Ordinal);
        var defaultCount = 0;
        foreach (var arm in arms)
        {
            if (string.IsNullOrWhiteSpace(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?> { ["reason"] = "emptyArmId", ["nodeId"] = branch.Id });
            }

            ValidateLength(arm.Id, 64, "armId");
            ValidateLength(arm.Name, 128, "armName");

            if (!armIds.Add(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "duplicateArmId", ["nodeId"] = branch.Id, ["armId"] = arm.Id,
                    });
            }

            if (arm.IsDefault)
            {
                defaultCount++;
            }
            else if (arm.Expr is null)
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "branchArmWithoutExpr", ["nodeId"] = branch.Id, ["armId"] = arm.Id,
                    });
            }

            ValidateChain(arm.Next, seen, providerKeys);
        }

        if (defaultCount != 1)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "branchDefaultArmCount", ["nodeId"] = branch.Id });
        }
    }

    /// <summary>并行节点至少两臂,臂 Id 在节点内非空且唯一,并递归校验臂内节点。</summary>
    protected virtual void ValidateParallel(WfNode parallel, HashSet<string> seen, HashSet<string> providerKeys)
    {
        if (parallel.ParallelArms is not { Count: >= 2 } arms)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "parallelArmCountInvalid", ["nodeId"] = parallel.Id });
        }

        var armIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arm in arms)
        {
            if (string.IsNullOrWhiteSpace(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?> { ["reason"] = "emptyParallelArmId", ["nodeId"] = parallel.Id });
            }

            ValidateLength(arm.Id, 64, "armId");
            ValidateLength(arm.Name, 128, "armName");
            if (!armIds.Add(arm.Id))
            {
                throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "duplicateParallelArmId", ["nodeId"] = parallel.Id, ["armId"] = arm.Id,
                    });
            }

            ValidateChain(arm.Next, seen, providerKeys);
        }

        var index = WfModelIndex.Build(new WfModel { Root = parallel });
        if (index.Nodes.Any(node => node.Type == WfNodeType.Parallel
            && !ReferenceEquals(node, parallel)
            && index.FindEnclosingParallel(node.Id) is not null))
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelInvalid,
                new Dictionary<string, object?> { ["reason"] = "nestedParallel", ["nodeId"] = parallel.Id });
        }
    }

    /// <summary>按 Id 取定义;不存在抛 <see cref="WorkflowErrorCode.DefinitionNotFound"/>。</summary>
    protected virtual async Task<WfDefinition> RequireDefinitionAsync(long id)
    {
        if (id <= 0)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        var def = await definitions.GetByIdAsync(id);
        if (def is null)
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNotFound);
        return def;
    }

    /// <summary>取草稿版本 0;缺失则插入默认模型草稿。</summary>
    protected virtual async Task<WfDefinitionVersion> GetOrCreateDraftAsync(
        long definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = await versions.AsQueryable()
            .Where(v => v.DefinitionId == definitionId && v.Version == DraftVersion)
            .FirstAsync();
        if (draft is not null)
            return draft;

        draft = new WfDefinitionVersion
        {
            DefinitionId = definitionId,
            Version = DraftVersion,
            ModelJson = SerializeModel(CreateDefaultModel()),
        };
        await versions.InsertAsync(draft);
        return draft;
    }

    protected virtual string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed))
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.DefinitionNameInvalid);
        ValidateLength(trimmed, 128, "definitionName");
        return trimmed;
    }

    protected virtual void ValidateMetadata(string? icon, string? groupName)
    {
        ValidateLength(icon, 64, "icon");
        ValidateLength(groupName, 64, "groupName");
    }

    protected virtual void ValidateLength(string? value, int maxLength, string field)
    {
        if (value?.Length > maxLength)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.ModelFieldTooLong,
                new Dictionary<string, object?>
                {
                    ["field"] = field,
                    ["maxLength"] = maxLength,
                });
        }
    }

    protected virtual string SerializeModel(WfModel model) => WfModelJson.Serialize(model);

    protected virtual WfModel CreateDefaultModel() => new()
    {
        Version = WfModelJson.CurrentVersion,
        Root = new WfNode { Id = "start", Type = WfNodeType.Start, Name = "" },
    };

    protected virtual WfDefinitionDetailOutput MapDetail(WfDefinition def, WfDefinitionVersion draft)
    {
        var model = WfModelJson.Deserialize(draft.ModelJson) ?? CreateDefaultModel();
        return new WfDefinitionDetailOutput
        {
            Id = def.Id,
            Name = def.Name,
            Icon = def.Icon,
            GroupName = def.GroupName,
            Status = def.Status,
            CurrentVersion = def.CurrentVersion,
            CreateTime = def.CreateTime,
            CreateUserId = def.CreateUserId,
            UpdateTime = def.UpdateTime,
            UpdateUserId = def.UpdateUserId,
            Model = model,
        };
    }
}
