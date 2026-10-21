using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenonAdmin.Core;
using TenonAdmin.AspNetCore;

namespace TenonAdmin.Tests;

/// <summary>#56：真实宿主的 JSON 边界须同时兼容既有小编号与浏览器不能精确表示的大编号。</summary>
public class LargeIdJsonTests(LargeIdJsonFixture fixture) : IClassFixture<LargeIdJsonFixture>
{
    private JsonSerializerOptions MvcOptions => fixture.Factory.Services
        .GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    [Theory]
    [InlineData(0L, false)]
    [InlineData(42L, false)]
    [InlineData(9007199254740991L, false)]
    [InlineData(-9007199254740991L, false)]
    [InlineData(9007199254740992L, true)]
    [InlineData(9007199254740993L, true)]
    [InlineData(-9007199254740992L, true)]
    [InlineData(long.MaxValue, true)]
    [InlineData(long.MinValue, true)]
    public void Mvc_long_scalar_nullable_and_collection_round_trip(long id, bool asString)
    {
        var payload = new IdPayload(id, id, [id], 48003, 12.5m);
        var json = JsonSerializer.Serialize(payload, MvcOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var kind = asString ? JsonValueKind.String : JsonValueKind.Number;
        Assert.Equal(kind, root.GetProperty("id").ValueKind);
        Assert.Equal(kind, root.GetProperty("parentId").ValueKind);
        Assert.Equal(kind, root.GetProperty("ids")[0].ValueKind);
        Assert.Equal(id.ToString(CultureInfo.InvariantCulture), root.GetProperty("id").ToString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("code").ValueKind);
        Assert.Equal(48003, root.GetProperty("code").GetInt32());
        Assert.Equal(12.5m, root.GetProperty("amount").GetDecimal());
        var restored = JsonSerializer.Deserialize<IdPayload>(json, MvcOptions)!;
        Assert.Equal(id, restored.Id);
        Assert.Equal(id, restored.ParentId);
        Assert.Equal(new[] { id }, restored.Ids);
    }

    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("\"9007199254740993\"")]
    public void Mvc_reads_existing_numeric_and_lossless_string_inputs(string json)
    {
        Assert.Equal(9007199254740993L, JsonSerializer.Deserialize<long>(json, MvcOptions));
        Assert.Null(JsonSerializer.Deserialize<long?>("null", MvcOptions));
    }

    [Theory]
    [InlineData("\"9223372036854775808\"")]
    [InlineData("\"1.5\"")]
    [InlineData("\"not-an-id\"")]
    public void Mvc_rejects_invalid_or_out_of_range_strings(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>(json, MvcOptions));

    [Fact]
    public void Mvc_dictionary_keys_and_null_values_keep_their_contract()
    {
        var values = new Dictionary<long, long?> { [long.MaxValue] = null, [long.MinValue] = long.MaxValue };
        var json = JsonSerializer.Serialize(values, MvcOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty(long.MaxValue.ToString(CultureInfo.InvariantCulture)).ValueKind);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture),
            document.RootElement.GetProperty(long.MinValue.ToString(CultureInfo.InvariantCulture)).GetString());
        var restored = JsonSerializer.Deserialize<Dictionary<long, long?>>(json, MvcOptions)!;
        Assert.Null(restored[long.MaxValue]);
        Assert.Equal(long.MaxValue, restored[long.MinValue]);
    }

    [Fact]
    public void SignalR_protocol_preserves_large_id_and_numeric_business_code()
    {
        var options = fixture.Factory.Services.GetRequiredService<IOptions<JsonHubProtocolOptions>>();
        var protocol = new JsonHubProtocol(options);
        var bytes = protocol.GetMessageBytes(new InvocationMessage("changed", [new { id = 9007199254740993L, code = 48003 }]));
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(bytes.Span).TrimEnd('\u001e'));
        var payload = json.RootElement.GetProperty("arguments")[0];
        Assert.Equal(JsonValueKind.String, payload.GetProperty("id").ValueKind);
        Assert.Equal("9007199254740993", payload.GetProperty("id").GetString());
        Assert.Equal(48003, payload.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Consumer_converters_registered_before_AddTenonAdmin_keep_priority()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new ConsumerInt64Converter()));
        builder.Services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new ConsumerInt64Converter()));
        builder.Services.AddTenonAdmin(builder.Configuration, options => options.Realtime.Enabled = true);
        using var app = builder.Build();
        var mvc = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var signalR = app.Services.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;
        Assert.Equal("\"consumer\"", JsonSerializer.Serialize(long.MaxValue, mvc));
        Assert.Equal("\"consumer\"", JsonSerializer.Serialize(long.MaxValue, signalR));
    }

    [Fact]
    public async Task Http_scalar_entity_and_string_body_keep_the_same_id()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await client.LoginToken("superAdmin", "Test@123456"));
        var added = await (await client.PostJson("/api/v1/sys/role/add", new
        {
            name = "大编号回归", code = "large-id-regression", sort = 0, enabled = true,
        })).ReadEnvelope();
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.String, added.GetProperty("data").ValueKind);
        var id = added.GetProperty("data").GetString()!;
        Assert.True(long.Parse(id, CultureInfo.InvariantCulture) > 9007199254740991L);
        var detail = await (await client.GetAsync($"/api/v1/sys/role/{id}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        Assert.Equal(id, detail.GetProperty("data").GetProperty("id").GetString());
        var assigned = await (await client.PutJson("/api/v1/sys/role/menu", new
        {
            roleId = id, menuIds = new[] { "1" },
        })).ReadEnvelope();
        Assert.Equal(0, assigned.GetProperty("code").GetInt32());
        var menus = await (await client.GetAsync($"/api/v1/sys/role/{id}/menus")).ReadEnvelope();
        Assert.Equal(1, menus.GetProperty("data")[0].GetInt64());
    }

    [Fact]
    public async Task OpenApi_path_and_payload_describe_number_or_string_long()
    {
        using var client = fixture.Factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = json.RootElement;
        var pathSchema = root.GetProperty("paths").GetProperty("/api/v1/sys/role/{id}")
            .GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id").GetProperty("schema");
        Assert.Contains("string", SchemaTypes(pathSchema));
        Assert.Contains("integer", SchemaTypes(pathSchema));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.Contains("string", SchemaTypes(schemas.GetProperty("SysRole").GetProperty("properties").GetProperty("id")));
        Assert.Contains("string", SchemaTypes(schemas.GetProperty("SetRoleMenusInput").GetProperty("properties").GetProperty("roleId")));
    }

    private static IEnumerable<string?> SchemaTypes(JsonElement schema)
    {
        var type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(value => value.GetString()).ToArray()
            : [type.GetString()];
    }

    private sealed record IdPayload(long Id, long? ParentId, long[] Ids, int Code, decimal Amount);

    private sealed class ConsumerInt64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetInt64();
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue("consumer");
    }
}

public sealed class LargeIdJsonFixture : IDisposable
{
    public AdminAppFactory Factory { get; } = new()
    {
        Settings = new Dictionary<string, string?> { ["TenonAdmin:Realtime:Enabled"] = "true" },
        Overrides = services => services.AddSingleton<IIdGenerator>(new LargeIdGenerator()),
    };

    public void Dispose() => Factory.Dispose();

    private sealed class LargeIdGenerator : IIdGenerator
    {
        private long value = 9007199254740991L;
        public long NextId() => Interlocked.Increment(ref value);
    }
}
