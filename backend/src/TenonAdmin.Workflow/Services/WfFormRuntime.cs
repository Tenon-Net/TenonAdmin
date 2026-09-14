using System.Globalization;
using System.Text.Json;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Workflow;

/// <summary>
/// 内置表单运行时边界:解析提交值、按节点权限合并，并执行已发布 schema 的值校验。
/// </summary>
internal static class WfFormRuntime
{
    public static async Task<string?> ApplyAsync(
        ISqlSugarClient db,
        bool enabled,
        WfFormSchema? schema,
        string? currentJson,
        string? submittedJson,
        IReadOnlyCollection<WfFormFieldPerm>? permissions,
        CancellationToken cancellationToken,
        long actorUserId = 0,
        bool allowAnyFileOwner = false)
    {
        if (!enabled) return submittedJson ?? currentJson;

        if (schema is null) return submittedJson ?? currentJson;

        var current = ParseObject(currentJson);
        var submitted = ParseObject(submittedJson);
        var values = new Dictionary<string, JsonElement>(current, StringComparer.Ordinal);
        var fieldKeys = schema.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in submitted)
        {
            if (!fieldKeys.Contains(key))
            {
                if (!current.ContainsKey(key)) ThrowInvalid(key, "unknownField");
                continue;
            }
            values[key] = value;
        }

        foreach (var field in schema.Fields)
        {
            if (AccessOf(field.Key, permissions) is WfFormPermAccess.Hidden or WfFormPermAccess.Readonly)
            {
                if (current.TryGetValue(field.Key, out var original)) values[field.Key] = original;
                else values.Remove(field.Key);
            }
        }

        var attachmentRefs = new List<(WfFormField Field, long Id, bool CheckOwner)>();
        foreach (var field in schema.Fields)
        {
            var access = AccessOf(field.Key, permissions);
            if (access != WfFormPermAccess.Editable) continue;
            if (!values.TryGetValue(field.Key, out var value)) value = default;
            IReadOnlySet<long> previousIds = current.TryGetValue(field.Key, out var previous)
                ? AttachmentIds(previous, field)
                : new HashSet<long>();
            ValidateField(field, value, attachmentRefs, previousIds);
        }

        if (attachmentRefs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attachmentIds = attachmentRefs.Select(reference => reference.Id).Distinct().ToList();
            var existing = await db.Queryable<SysFile>()
                .Where(file => attachmentIds.Contains(file.Id))
                .ToListAsync();
            if (existing.Count != attachmentIds.Count)
                ThrowInvalid("attachment", "attachmentNotFound");

            var fileMap = existing.ToDictionary(file => file.Id);
            foreach (var (field, id, checkOwner) in attachmentRefs)
            {
                var file = fileMap[id];
                if (checkOwner && !allowAnyFileOwner && (actorUserId <= 0 || file.CreateUserId != actorUserId))
                    ThrowInvalid(field.Key, "attachmentNotAllowed");

                var accept = ReadStringProperty(field.Props, "accept");
                if (!string.IsNullOrWhiteSpace(accept))
                {
                    var allowed = accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!allowed.Contains(file.Extension)) ThrowInvalid(field.Key, "attachmentTypeInvalid");
                }

                var maxSizeMb = ReadInt(field.Props, "maxSizeMb") ?? 10;
                if (file.SizeBytes > (long)maxSizeMb * 1024 * 1024)
                    ThrowInvalid(field.Key, "attachmentSizeInvalid");
            }
        }

        return values.Count == 0
            ? null
            : JsonSerializer.Serialize(values, WfModelJson.Options);
    }

    private static WfFormPermAccess AccessOf(string field, IReadOnlyCollection<WfFormFieldPerm>? permissions) =>
        permissions?.FirstOrDefault(permission => permission is not null && permission.Field == field)?.Access
        ?? WfFormPermAccess.Editable;

    /// <summary>读取接口只投影 schema 字段，并按适用节点合并 hidden 权限，避免把内置表单原值回传给无权查看者。</summary>
    public static string? ProjectVisibleJson(
        WfFormSchema? schema,
        string? currentJson,
        IEnumerable<WfFormFieldPerm>? permissions)
    {
        if (schema is null || string.IsNullOrWhiteSpace(currentJson)) return currentJson;

        // 存量变量损坏必须显式失败，不能把诊断性数据伪装成空值返回给调用方。
        var values = ParseObject(currentJson);

        var hidden = permissions?
            .Where(permission => permission is not null && permission.Access == WfFormPermAccess.Hidden)
            .Select(permission => permission.Field)
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        var visible = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (hidden.Contains(field.Key)) continue;
            if (values.TryGetValue(field.Key, out var value)) visible[field.Key] = value;
        }

        return visible.Count == 0 ? null : JsonSerializer.Serialize(visible, WfModelJson.Options);
    }

    private static Dictionary<string, JsonElement> ParseObject(string? json)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                ThrowInvalid(null, "objectRequired");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == "__proto__" || !result.TryAdd(property.Name, property.Value.Clone()))
                    ThrowInvalid(property.Name, "duplicateKey");
            }
        }
        catch (JsonException)
        {
            ThrowInvalid(null, "jsonInvalid");
        }

        return result;
    }

    private static void ValidateField(
        WfFormField field,
        JsonElement value,
        List<(WfFormField Field, long Id, bool CheckOwner)> attachmentRefs,
        IReadOnlySet<long> previousIds)
    {
        if (IsEmpty(value))
        {
            if (field.Required) ThrowInvalid(field.Key, "required");
            return;
        }

        switch (field.Type)
        {
            case WfFormFieldType.Text:
            case WfFormFieldType.Textarea:
                if (value.ValueKind != JsonValueKind.String)
                    ThrowInvalid(field.Key, "valueInvalid");
                var defaultLength = field.Type == WfFormFieldType.Text ? 256 : 4000;
                var maxLength = ReadInt(field.Props, "maxLength") ?? defaultLength;
                if (value.GetString()!.Length > maxLength) ThrowInvalid(field.Key, "valueInvalid");
                break;

            case WfFormFieldType.Number:
            case WfFormFieldType.Money:
                var number = ReadNumber(field.Key, value);
                var props = field.Props;
                var precision = field.Type == WfFormFieldType.Money ? 2 : ReadInt(props, "precision");
                if (precision is not null && decimal.Round(number, precision.Value) != number)
                    ThrowInvalid(field.Key, "valueInvalid");
                var min = ReadDecimal(props, "min");
                var max = ReadDecimal(props, "max");
                if (min is not null && number < min || max is not null && number > max)
                    ThrowInvalid(field.Key, "rangeInvalid");
                break;

            case WfFormFieldType.Date:
                var date = ReadString(field.Key, value);
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _))
                    ThrowInvalid(field.Key, "valueInvalid");
                var minDate = ReadStringProperty(field.Props, "min");
                var maxDate = ReadStringProperty(field.Props, "max");
                if (minDate is not null && string.CompareOrdinal(date, minDate) < 0
                    || maxDate is not null && string.CompareOrdinal(date, maxDate) > 0)
                    ThrowInvalid(field.Key, "rangeInvalid");
                break;

            case WfFormFieldType.Datetime:
                var datetime = ReadString(field.Key, value);
                if (!HasTimezone(datetime)
                    || !DateTimeOffset.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw Invalid(field.Key, "valueInvalid");
                var minDatetime = ReadStringProperty(field.Props, "min");
                var maxDatetime = ReadStringProperty(field.Props, "max");
                if (minDatetime is not null && parsed < ParseDateTime(minDatetime)
                    || maxDatetime is not null && parsed > ParseDateTime(maxDatetime))
                    ThrowInvalid(field.Key, "rangeInvalid");
                break;

            case WfFormFieldType.Select:
                var selected = ReadString(field.Key, value);
                if (!OptionValues(field).Contains(selected)) ThrowInvalid(field.Key, "optionInvalid");
                break;

            case WfFormFieldType.MultiSelect:
                ValidateMultiSelect(field, value);
                break;

            case WfFormFieldType.User:
                ValidateIds(field, value, multiple: ReadBool(field.Props, "multiple"), maxSelected: ReadInt(field.Props, "maxSelected") ?? 20, null, previousIds);
                break;

            case WfFormFieldType.Attachment:
                var multiple = ReadBool(field.Props, "multiple");
                ValidateIds(field, value, multiple, multiple ? ReadInt(field.Props, "maxCount") ?? 20 : 1, attachmentRefs, previousIds);
                break;
        }
    }

    private static void ValidateMultiSelect(WfFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) ThrowInvalid(field.Key, "optionInvalid");
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !selected.Add(item.GetString()!)
                || !OptionValues(field).Contains(item.GetString()!))
                ThrowInvalid(field.Key, "optionInvalid");
        }
        var max = ReadInt(field.Props, "maxSelected") ?? OptionValues(field).Count;
        if (selected.Count > max) ThrowInvalid(field.Key, "optionInvalid");
    }

    private static void ValidateIds(
        WfFormField field,
        JsonElement value,
        bool multiple,
        int maxSelected,
        List<(WfFormField Field, long Id, bool CheckOwner)>? attachmentRefs,
        IReadOnlySet<long> previousIds)
    {
        var ids = new List<long>();
        if (multiple)
        {
            if (value.ValueKind != JsonValueKind.Array) ThrowInvalid(field.Key, "idInvalid");
            foreach (var item in value.EnumerateArray()) ids.Add(ReadId(field.Key, item));
        }
        else ids.Add(ReadId(field.Key, value));

        if (ids.Count == 0 || ids.Count > maxSelected || ids.Distinct().Count() != ids.Count)
            ThrowInvalid(field.Key, "idInvalid");
        if (attachmentRefs is not null && field.Type == WfFormFieldType.Attachment)
            attachmentRefs.AddRange(ids.Select(id => (field, id, !previousIds.Contains(id))));
    }

    private static IReadOnlySet<long> AttachmentIds(JsonElement value, WfFormField field)
    {
        var ids = new HashSet<long>();
        if (field.Type != WfFormFieldType.Attachment) return ids;
        if (field.Props is not null && ReadBool(field.Props, "multiple") && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (TryReadId(item, out var id)) ids.Add(id);
        }
        else if (TryReadId(value, out var single))
        {
            ids.Add(single);
        }
        return ids;
    }

    private static HashSet<string> OptionValues(WfFormField field)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (field.Props?.TryGetValue("options", out var options) != true
            || options.ValueKind != JsonValueKind.Array) return result;
        foreach (var option in options.EnumerateArray())
            if (option.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                result.Add(value.GetString()!);
        return result;
    }

    private static long ReadId(string field, JsonElement value) =>
        TryReadId(value, out var id)
            ? id
            : throw Invalid(field, "idInvalid");

    private static bool TryReadId(JsonElement value, out long id)
    {
        id = 0;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt64(out id) && id > 0;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var raw = value.GetString();
        return raw is not null
            && raw.Length > 0
            && raw[0] != '0'
            && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }

    private static decimal ReadNumber(string field, JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : throw Invalid(field, "valueInvalid");

    private static string ReadString(string field, JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid(field, "valueInvalid");

    private static bool IsEmpty(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())
        || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement>? props, string key) =>
        props?.TryGetValue(key, out var value) == true && value.TryGetInt32(out var number) ? number : null;

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonElement>? props, string key) =>
        props?.TryGetValue(key, out var value) == true && value.TryGetDecimal(out var number) ? number : null;

    private static bool ReadBool(IReadOnlyDictionary<string, JsonElement>? props, string key) =>
        props?.TryGetValue(key, out var value) == true && value.ValueKind == JsonValueKind.True;

    private static string? ReadStringProperty(IReadOnlyDictionary<string, JsonElement>? props, string key) =>
        props?.TryGetValue(key, out var value) == true && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool HasTimezone(string value) =>
        value.EndsWith('Z')
        || value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':';

    private static DateTimeOffset ParseDateTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw Invalid(null, "schemaDateInvalid");

    private static void ThrowInvalid(string? field, string reason) => throw Invalid(field, reason);

    private static AdminException Invalid(string? field, string reason) =>
        WorkflowErrorCode.Exception(
            WorkflowErrorCode.FormValueInvalid,
            new Dictionary<string, object?> { ["field"] = field, ["reason"] = reason });
}
