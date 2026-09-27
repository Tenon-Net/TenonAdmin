using System.Net;
using System.Text;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>M3b-0 Task 07A：AI 节点只按显式顶层白名单发送有界、脱敏、不可变的输入快照。</summary>
public class WfAiDecisionSafeInputTests
{
    private const string Instructions = "Review the selected fields. Keep this shadow-only.";
    private const string ProposalJson =
        "{\"schemaVersion\":\"1.0\",\"recommendation\":\"manual\",\"confidence\":1,\"reasonCodes\":[\"POLICY_MATCH\"],\"rationale\":\"manual review\",\"evidence\":[],\"riskFlags\":[]}";

    [Fact]
    public async Task Exact_top_level_whitelist_builds_a_typed_snapshot_without_interpreting_dot_paths()
    {
        var provider = new RecordingProvider();
        var props = Props("amount", "customer.name", "Amount", "missing");
        var context = Context(props,
            """{"amount":12,"Amount":99,"customer":{"name":"nested-secret"},"customer.name":"literal","ignored":"not-sent"}""");

        var result = await NewHandler(provider).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(Instructions, request.Instructions);
        var inputs = request.Inputs;
        Assert.Equal(new[] { "Amount", "amount", "customer.name" }, inputs.Keys);
        Assert.Equal(99, inputs["Amount"].GetInt32());
        Assert.Equal(12, inputs["amount"].GetInt32());
        Assert.Equal("literal", inputs["customer.name"].GetString());
        Assert.DoesNotContain("nested-secret", JsonSerializer.Serialize(request), StringComparison.Ordinal);
        Assert.DoesNotContain("not-sent", JsonSerializer.Serialize(request), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whitelist_matching_is_ordinal_and_does_not_match_a_different_case()
    {
        var provider = new RecordingProvider();

        _ = await NewHandler(provider).ExecuteAsync(
            Context(Props("Amount"), "{\"amount\":12}"), CancellationToken.None);

        Assert.Empty(Assert.Single(provider.Requests).Inputs);
    }

    [Fact]
    public async Task Sensitive_names_and_recognizable_sensitive_values_are_redacted_after_selection()
    {
        const string bearer = "Bearer top-secret-value";
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature";
        const string basic = "Authorization: Basic dXNlcjpwYXNz";
        const string email = "person@example.test";
        const string phone = "+86 138-0013-8000";
        var provider = new RecordingProvider();
        var variables = JsonSerializer.Serialize(new
        {
            password = "plain-password",
            note = bearer,
            nested = new { apiToken = "nested-token", safe = "visible" },
            jwt,
            basic,
            email,
            phone,
            ignoredSecret = "never-selected",
        });

        _ = await NewHandler(provider).ExecuteAsync(
            Context(Props("password", "note", "nested", "jwt", "basic", "email", "phone"), variables),
            CancellationToken.None);

        var request = Assert.Single(provider.Requests);
        var inputs = request.Inputs;
        Assert.Equal("***", inputs["password"].GetString());
        Assert.Equal("***", inputs["note"].GetString());
        Assert.Equal("***", inputs["jwt"].GetString());
        Assert.Equal("***", inputs["basic"].GetString());
        Assert.Equal("***", inputs["email"].GetString());
        Assert.Equal("***", inputs["phone"].GetString());
        Assert.Equal("***", inputs["nested"].GetProperty("apiToken").GetString());
        Assert.Equal("visible", inputs["nested"].GetProperty("safe").GetString());
        var serialized = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("plain-password", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(bearer, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(basic, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(email, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(phone, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("never-selected", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("not-json")]
    [InlineData("{\"amount\":1,\"amount\":2}")]
    [InlineData("{\"amount\":{\"nested\":1,\"nested\":2}}")]
    public async Task Invalid_variable_roots_or_duplicate_properties_fail_closed_before_provider_call(string variablesJson)
    {
        var provider = new RecordingProvider();

        var result = await NewHandler(provider).ExecuteAsync(
            Context(Props("amount"), variablesJson), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Equal(AiDecisionFallbackReason.ProviderFailure, result.AiDecision?.FallbackReason);
        Assert.Empty(provider.Requests);
        Assert.DoesNotContain(variablesJson, result.Summary ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_whitelist_entries_fail_closed_and_missing_keys_do_not_expand_the_projection()
    {
        var duplicateProvider = new RecordingProvider();
        var duplicateResult = await NewHandler(duplicateProvider).ExecuteAsync(
            Context(Props("amount", "amount"), "{\"amount\":1,\"secret\":2}"), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, duplicateResult.Type);
        Assert.Empty(duplicateProvider.Requests);

        var missingProvider = new RecordingProvider();
        _ = await NewHandler(missingProvider).ExecuteAsync(
            Context(Props("amount", "missing"), "{\"amount\":1,\"secret\":2}"), CancellationToken.None);

        var request = Assert.Single(missingProvider.Requests);
        Assert.Equal(new[] { "amount" }, request.Inputs.Keys);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(request), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nested_objects_and_arrays_are_preserved_with_deterministic_ordinal_property_order()
    {
        var provider = new RecordingProvider();
        var variables = """{"payload":{"z":1,"a":[{"d":4,"b":2},true,null]}}""";

        _ = await NewHandler(provider).ExecuteAsync(Context(Props("payload"), variables), CancellationToken.None);

        var request = Assert.Single(provider.Requests);
        Assert.Equal("{\"payload\":{\"a\":[{\"b\":2,\"d\":4},true,null],\"z\":1}}",
            request.CanonicalInputsJson);
    }

    [Theory]
    [MemberData(nameof(InvalidBoundedInputs))]
    public async Task Depth_count_text_and_total_size_limits_fail_closed(string variablesJson)
    {
        var provider = new RecordingProvider();

        var result = await NewHandler(provider).ExecuteAsync(
            Context(Props("value"), variablesJson), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Empty(provider.Requests);
    }

    public static IEnumerable<object[]> InvalidBoundedInputs()
    {
        yield return [NestedArrays(8)];
        yield return [JsonSerializer.Serialize(new { value = Enumerable.Range(0, 255).ToArray() })];
        yield return [JsonSerializer.Serialize(new { value = new string('x', 4097) })];
        yield return ["{\"value\":1}" + new string(' ', 65_537 - "{\"value\":1}".Length)];
    }

    [Fact]
    public async Task Exact_depth_count_text_and_raw_size_limits_are_accepted()
    {
        var cases = new[]
        {
            NestedArrays(7),
            JsonSerializer.Serialize(new { value = Enumerable.Range(0, 254).ToArray() }),
            JsonSerializer.Serialize(new { value = new string('x', 4096) }),
            "{\"value\":1}" + new string(' ', 65_536 - "{\"value\":1}".Length),
        };

        foreach (var variablesJson in cases)
        {
            var provider = new RecordingProvider();
            _ = await NewHandler(provider).ExecuteAsync(
                Context(Props("value"), variablesJson), CancellationToken.None);
            Assert.Single(provider.Requests);
        }
    }

    [Fact]
    public async Task Canonical_input_byte_limit_accepts_the_exact_boundary_and_rejects_one_more_byte()
    {
        static string JsonWithLastStringLength(int length) => JsonSerializer.Serialize(new
        {
            value = Enumerable.Repeat(new string('x', 4096), 7).Append(new string('x', length)).ToArray(),
        });
        var atLimit = JsonWithLastStringLength(4061);
        var aboveLimit = JsonWithLastStringLength(4062);
        Assert.Equal(32_768, Encoding.UTF8.GetByteCount(atLimit));
        Assert.Equal(32_769, Encoding.UTF8.GetByteCount(aboveLimit));

        var validProvider = new RecordingProvider();
        _ = await NewHandler(validProvider).ExecuteAsync(Context(Props("value"), atLimit), CancellationToken.None);
        var invalidProvider = new RecordingProvider();
        _ = await NewHandler(invalidProvider).ExecuteAsync(Context(Props("value"), aboveLimit), CancellationToken.None);

        Assert.Single(validProvider.Requests);
        Assert.Empty(invalidProvider.Requests);
    }

    [Fact]
    public void Public_request_construction_cannot_set_or_replace_the_safe_projection()
    {
        foreach (var name in new[] { "Instructions", "Inputs", "CanonicalInputsJson", "InputHash" })
        {
            var property = Assert.IsAssignableFrom<System.Reflection.PropertyInfo>(typeof(AiDecisionProviderRequest).GetProperty(name));
            Assert.NotNull(property.SetMethod);
            Assert.False(property.SetMethod!.IsPublic);
        }
    }

    [Fact]
    public async Task Instruction_field_name_and_whitelist_count_limits_are_closed()
    {
        var validFields = Enumerable.Range(0, 32).Select(index => $"f{index:00}").ToArray();
        var validProvider = new RecordingProvider();
        var validProps = Props(validFields);
        validProps.AiInstructions = new string('i', 2000);
        validProps.AiInputFields![0] = new string('k', 64);
        _ = await NewHandler(validProvider).ExecuteAsync(
            Context(validProps, "{\"k\":1}"), CancellationToken.None);
        Assert.Single(validProvider.Requests);

        foreach (var invalidProps in new[]
                 {
                     new WfNodeProps { AiInstructions = new string('i', 2001), AiInputFields = ["value"] },
                     new WfNodeProps { AiInstructions = Instructions, AiInputFields = [new string('k', 65)] },
                     new WfNodeProps
                     {
                         AiInstructions = Instructions,
                         AiInputFields = [.. Enumerable.Range(0, 33).Select(index => $"f{index:00}")],
                     },
                 })
        {
            var provider = new RecordingProvider();
            _ = await NewHandler(provider).ExecuteAsync(
                Context(invalidProps, "{\"value\":1}"), CancellationToken.None);
            Assert.Empty(provider.Requests);
        }
    }

    [Fact]
    public async Task Snapshot_is_immutable_and_hash_is_stable_for_equivalent_safe_input_but_changes_with_safe_content()
    {
        var props = Props("b", "a");
        var firstProvider = new RecordingProvider();
        _ = await NewHandler(firstProvider).ExecuteAsync(
            Context(props, "{\"b\":2,\"a\":1}"), CancellationToken.None);
        var first = Assert.Single(firstProvider.Requests);

        props.AiInputFields![0] = "unselected";
        var equivalentProvider = new RecordingProvider();
        _ = await NewHandler(equivalentProvider).ExecuteAsync(
            Context(Props("a", "b"), "{\"a\":1,\"b\":2}"), CancellationToken.None);
        var changedProvider = new RecordingProvider();
        _ = await NewHandler(changedProvider).ExecuteAsync(
            Context(Props("a", "b"), "{\"a\":1,\"b\":3}"), CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, first.Inputs.Keys);
        var firstHash = first.InputHash;
        Assert.Equal(firstHash, Assert.Single(equivalentProvider.Requests).InputHash);
        Assert.NotEqual(firstHash, Assert.Single(changedProvider.Requests).InputHash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", firstHash);
        var snapshot = first.Inputs;
        Assert.False(snapshot is IDictionary<string, JsonElement> mutable && !mutable.IsReadOnly);

        var instructionProvider = new RecordingProvider();
        var instructionProps = Props("a", "b");
        instructionProps.AiInstructions = Instructions + " changed";
        _ = await NewHandler(instructionProvider).ExecuteAsync(
            Context(instructionProps, "{\"a\":1,\"b\":2,\"ignored\":9}"), CancellationToken.None);
        Assert.NotEqual(firstHash, Assert.Single(instructionProvider.Requests).InputHash);

        var ignoredProvider = new RecordingProvider();
        _ = await NewHandler(ignoredProvider).ExecuteAsync(
            Context(Props("a", "b"), "{\"a\":1,\"b\":2,\"ignored\":10}"), CancellationToken.None);
        Assert.Equal(firstHash, Assert.Single(ignoredProvider.Requests).InputHash);

        var redactedProvider = new RecordingProvider();
        _ = await NewHandler(redactedProvider).ExecuteAsync(
            Context(Props("a", "b", "password"), "{\"a\":1,\"b\":2,\"password\":\"first-secret\"}"), CancellationToken.None);
        var anotherRedactedProvider = new RecordingProvider();
        _ = await NewHandler(anotherRedactedProvider).ExecuteAsync(
            Context(Props("a", "b", "password"), "{\"a\":1,\"b\":2,\"password\":\"second-secret\"}"), CancellationToken.None);
        Assert.Equal(Assert.Single(redactedProvider.Requests).InputHash,
            Assert.Single(anotherRedactedProvider.Requests).InputHash);
    }

    [Fact]
    public async Task Http_body_contains_only_the_restricted_instruction_safe_projection_and_existing_allowed_identity()
    {
        var transport = new RecordingHttpHandler(HttpStatusCode.OK, Completion(ProposalJson));
        using var client = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
        var httpProvider = new OpenAiCompatibleAiDecisionProvider(client, Options(), TimeProvider.System);
        var result = await NewHandler(httpProvider).ExecuteAsync(
            Context(Props("amount", "password"),
                "{\"amount\":12,\"password\":\"must-not-leak\",\"unselected\":\"hidden\"}"),
            CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        using var body = JsonDocument.Parse(Assert.IsType<string>(transport.Body));
        var messages = body.RootElement.GetProperty("messages");
        var user = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
        Assert.Equal(Instructions, user.RootElement.GetProperty("instructions").GetString());
        Assert.Equal(12, user.RootElement.GetProperty("inputs").GetProperty("amount").GetInt32());
        Assert.Equal("***", user.RootElement.GetProperty("inputs").GetProperty("password").GetString());
        Assert.Matches("^sha256:[0-9a-f]{64}$", user.RootElement.GetProperty("inputHash").GetString()!);
        Assert.False(user.RootElement.TryGetProperty("variablesJson", out _));
        Assert.DoesNotContain("must-not-leak", transport.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", transport.Body!, StringComparison.Ordinal);
        Assert.Contains("shadow", messages[0].GetProperty("content").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_failure_and_external_cancellation_do_not_leak_safe_input_or_change_the_original_token()
    {
        const string selectedValue = "visible-to-provider-only";
        var failingTransport = new RecordingHttpHandler(HttpStatusCode.BadRequest, selectedValue);
        using var failingClient = new HttpClient(failingTransport) { Timeout = Timeout.InfiniteTimeSpan };
        var failed = await NewHandler(new OpenAiCompatibleAiDecisionProvider(failingClient, Options(), TimeProvider.System))
            .ExecuteAsync(Context(Props("value"), JsonSerializer.Serialize(new { value = selectedValue })), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, failed.Type);
        Assert.DoesNotContain(selectedValue, JsonSerializer.Serialize(failed), StringComparison.Ordinal);

        var lateProvider = new LateProvider();
        using var cancellation = new CancellationTokenSource();
        var pending = NewHandler(lateProvider).ExecuteAsync(
            Context(Props("value"), JsonSerializer.Serialize(new { value = selectedValue })), cancellation.Token);
        await lateProvider.Started.Task;
        cancellation.Cancel();
        lateProvider.Complete();

        var exception = await Record.ExceptionAsync(() => pending);
        Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
    }

    [Fact]
    public async Task Outer_cancellation_rethrows_the_original_token_when_provider_throws_a_foreign_cancellation()
    {
        var provider = new LateProvider();
        using var outer = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        var pending = NewHandler(provider).ExecuteAsync(
            Context(Props("value"), "{\"value\":1}"), outer.Token);
        await provider.Started.Task;

        outer.Cancel();
        provider.CancelWith(foreign.Token);

        var exception = await Record.ExceptionAsync(() => pending);
        Assert.Equal(outer.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
    }

    [Fact]
    public async Task Outer_cancellation_wins_when_an_ignoring_provider_late_throws_a_non_cancellation_exception()
    {
        var provider = new LateProvider();
        using var outer = new CancellationTokenSource();
        var pending = NewHandler(provider).ExecuteAsync(
            Context(Props("value"), "{\"value\":1}"), outer.Token);
        await provider.Started.Task;

        outer.Cancel();
        provider.Fail(new InvalidOperationException("late provider detail must not escape"));

        var exception = await Record.ExceptionAsync(() => pending);
        Assert.Equal(outer.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
    }

    [Theory]
    [InlineData("person@example.test")]
    [InlineData("+86 138-0013-8000")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature")]
    [InlineData("Authorization: Bearer credential")]
    [InlineData("password=credential")]
    public async Task Nested_property_names_that_look_like_sensitive_values_fail_closed(string propertyName)
    {
        var provider = new RecordingProvider();
        var variables = JsonSerializer.Serialize(new
        {
            payload = new Dictionary<string, string> { [propertyName] = "safe" },
        });

        var result = await NewHandler(provider).ExecuteAsync(
            Context(Props("payload"), variables), CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Empty(provider.Requests);
        Assert.DoesNotContain(propertyName, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_nested_property_name_prevents_any_http_send()
    {
        var transport = new RecordingHttpHandler(HttpStatusCode.OK, Completion(ProposalJson));
        using var client = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OpenAiCompatibleAiDecisionProvider(client, Options(), TimeProvider.System);

        var result = await NewHandler(provider).ExecuteAsync(
            Context(Props("payload"), "{\"payload\":{\"person@example.test\":\"safe\"}}"),
            CancellationToken.None);

        Assert.Equal(WfNodeExecutionResultType.ManualFallback, result.Type);
        Assert.Equal(0, transport.SendCount);
        Assert.Null(transport.Body);
    }

    private static AiDecisionNodeHandler NewHandler(IAiDecisionProvider provider) => new(
        provider,
        new AiDecisionProposalParser(),
        new AiDecisionPolicyEvaluator(new AiDecisionPolicyOptions { AllowedReasonCodes = ["POLICY_MATCH"] }));

    private static WfNodeProps Props(params string[] fields) => new()
    {
        AiInstructions = Instructions,
        AiInputFields = [.. fields],
    };

    private static WfNodeExecutionContext Context(WfNodeProps props, string variablesJson) => new()
    {
        ExecutionKey = "safe-input-execution",
        InstanceId = 101,
        TokenId = 202,
        NodeVisitId = 203,
        NodeId = "ai-node",
        NodeType = WfNodeType.AiDecision,
        DefinitionVersionId = 303,
        OrgId = 505,
        StarterUserId = 404,
        BusinessKey = "must-not-be-sent",
        NodeProps = props,
        VariablesJson = variablesJson,
        Attempt = 1,
        DeadlineAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static OpenAiCompatibleAiDecisionOptions Options() => new()
    {
        Enabled = true,
        Endpoint = "https://compatible.example.test/v1/chat/completions",
        Model = "compatibility-model",
        TimeoutSeconds = 30,
        ResponseByteCap = 64 * 1024,
    };

    private static string Completion(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content }, finish_reason = "stop" } },
    });

    private static string NestedArrays(int depth) =>
        "{\"value\":" + new string('[', depth) + "0" + new string(']', depth) + "}";

    private sealed class RecordingProvider : IAiDecisionProvider
    {
        public List<AiDecisionProviderRequest> Requests { get; } = [];

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(AiDecisionProviderResult.Proposal(ProposalJson));
        }
    }

    private sealed class LateProvider : IAiDecisionProvider
    {
        private readonly TaskCompletionSource<AiDecisionProviderResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiDecisionProviderResult> ProposeAsync(
            AiDecisionProviderRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult(AiDecisionProviderResult.Proposal(ProposalJson));

        public void CancelWith(CancellationToken cancellationToken) =>
            _completion.TrySetException(new OperationCanceledException(cancellationToken));

        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class RecordingHttpHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
