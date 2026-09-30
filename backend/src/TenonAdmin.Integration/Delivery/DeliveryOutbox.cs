using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IDeliveryOutbox"/> 默认实现(实现契约 §7):在注入的同一个 <see cref="ISqlSugarClient"/> 上「先查后插」——
/// 同键同内容返回既有记录,不同内容拒绝;<b>不另开连接或事务</b>,也<b>不吞唯一冲突</b>:并发同键插入由唯一索引兜底,
/// 冲突异常原样抛给调用方事务(PostgreSQL 上事务此时已中止,半吊子的捕获只会更糟,与工作流 outbox 同理)。
/// </summary>
public partial class DeliveryOutbox(
    ISqlSugarClient db,
    IEnumerable<IDeliveryAdapter> adapters,
    IIdGenerator ids,
    IntegrationOptions options,
    TimeProvider time) : IDeliveryOutbox
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonWriterOptions CompactWriter = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$")]
    private static partial Regex OperationPattern();

    [GeneratedRegex("^[\\x21-\\x7E]{1,128}$")]
    private static partial Regex DeliveryKeyPattern();

    /// <inheritdoc />
    public virtual async Task<DeliveryEnqueueResult> EnqueueAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var adapter = ResolveAdapter(request.Adapter);
        Validate(request);
        IntegrationErrorCode.ThrowIf(!request.Standalone && !db.Ado.IsAnyTran(), IntegrationErrorCode.DeliveryRequiresTransaction,
            fallbackMessage: "可靠投递须在业务事务内入队;确需独立入队请设置 Standalone。");

        var payloadJson = NormalizePayload(request);
        var payloadHash = Hash(payloadJson);
        var key = string.IsNullOrWhiteSpace(request.DeliveryKey) ? null : request.DeliveryKey.Trim();

        if (key is not null)
        {
            var existing = await db.Queryable<IntegrationDelivery>().Where(d => d.DeliveryKey == key).FirstAsync(cancellationToken);
            if (existing is not null)
            {
                var same = string.Equals(existing.Adapter, adapter.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.Operation, request.Operation, StringComparison.Ordinal)
                    && string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal);
                IntegrationErrorCode.ThrowIf(!same, IntegrationErrorCode.DeliveryKeyConflict,
                    new Dictionary<string, object?> { ["deliveryKey"] = key }, $"投递标识 {key} 已用于不同的适配器、操作或载荷。");
                return new DeliveryEnqueueResult(existing.Id, existing.DeliveryKey, Created: false);
            }
        }

        var row = BuildRecord(request, adapter, payloadJson, payloadHash, key);
        await db.Insertable(row).ExecuteCommandAsync(cancellationToken);
        return new DeliveryEnqueueResult(row.Id, row.DeliveryKey, Created: true);
    }

    /// <summary>构造待插入的记录:预生成雪花 Id(未给标识时标识即 <c>dlv_{Id}</c>),时间取整到秒。</summary>
    protected virtual IntegrationDelivery BuildRecord(
        DeliveryRequest request, IDeliveryAdapter adapter, string payloadJson, string payloadHash, string? key)
    {
        var id = ids.NextId();
        var now = IntegrationAppState.FloorSeconds(time.GetUtcNow().UtcDateTime);
        var notBefore = request.NotBefore is { } at ? IntegrationAppState.FloorSeconds(at.UtcDateTime) : now;
        var start = notBefore > now ? notBefore : now;
        var maxAge = request.MaxAge ?? TimeSpan.FromHours(options.Delivery.MaxAgeHours);
        return new IntegrationDelivery
        {
            Id = id,
            DeliveryKey = key ?? $"dlv_{id}",
            Adapter = adapter.Name,
            Operation = request.Operation,
            BusinessKey = request.BusinessKey,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Status = DeliveryStatus.Pending,
            MaxAttempts = request.MaxAttempts ?? options.Delivery.MaxAttempts,
            NextAttemptAtUtc = start,
            DeadlineAtUtc = IntegrationAppState.FloorSeconds(start + maxAge),
        };
    }

    /// <summary>按名取已注册的适配器(不区分大小写);未注册抛 49043。</summary>
    protected virtual IDeliveryAdapter ResolveAdapter(string? name)
    {
        var adapter = string.IsNullOrWhiteSpace(name) ? null : adapters.FindByName(name.Trim());
        return adapter ?? throw IntegrationErrorCode.Exception(IntegrationErrorCode.DeliveryAdapterUnknown,
            new Dictionary<string, object?> { ["adapter"] = name }, $"投递适配器未注册:{name}");
    }

    /// <summary>校验请求字段;不合法抛 49044(args.field 指明字段)。</summary>
    protected virtual void Validate(DeliveryRequest request)
    {
        Invalid(request.Operation is null || !OperationPattern().IsMatch(request.Operation), "operation");
        Invalid(!string.IsNullOrWhiteSpace(request.DeliveryKey) && !DeliveryKeyPattern().IsMatch(request.DeliveryKey.Trim()), "deliveryKey");
        Invalid(request.BusinessKey is { Length: > 128 }, "businessKey");
        Invalid(request.MaxAttempts is < 1 or > 100, "maxAttempts");
        Invalid(request.MaxAge is { } age && (age < TimeSpan.FromMinutes(1) || age > TimeSpan.FromDays(30)), "maxAge");
        Invalid(request.Payload is not null && request.PayloadJson is not null, "payload");
    }

    /// <summary>把载荷规范化为紧凑 JSON(对象与原始 JSON 走同一条写出路径,内容相同即摘要相同);超限或非法抛 49044。</summary>
    protected virtual string NormalizePayload(DeliveryRequest request)
    {
        JsonElement element;
        if (request.PayloadJson is { } raw)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                element = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw InvalidField("payload");
            }
        }
        else
        {
            element = JsonSerializer.SerializeToElement(request.Payload, WebJson);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CompactWriter))
            element.WriteTo(writer);
        Invalid(buffer.WrittenCount > options.Delivery.MaxPayloadBytes, "payload");
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>载荷摘要(SHA-256 小写 hex)。</summary>
    protected static string Hash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static void Invalid(bool condition, string field)
    {
        if (condition) throw InvalidField(field);
    }

    private static AdminException InvalidField(string field) =>
        IntegrationErrorCode.Exception(IntegrationErrorCode.DeliveryRequestInvalid,
            new Dictionary<string, object?> { ["field"] = field }, $"入队请求不合法:{field}");
}
