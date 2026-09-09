using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// OpenAI-compatible AI Decision HTTP adapter 的协议和失效关闭契约。所有 transport 都是本地
/// <see cref="HttpMessageHandler"/>，不访问网络也不使用真实凭据。
/// </summary>
public class WfOpenAiCompatibleAiDecisionProviderTests
{
    private const string ApiKey = "tenon-test-openai-api-key";
    private const string RawVariablesSecret = "tenon-test-raw-variables-secret";
    private const string EndpointQuerySecret = "tenon-test-endpoint-query-secret";
    private const string BusinessKey = "tenon-test-business-key";
    private const string ProposalJson =
        "{\"schemaVersion\":\"1.0\",\"recommendation\":\"approve\",\"confidence\":0.95,\"reasonCodes\":[\"POLICY_MATCH\"],\"rationale\":\"proposal only\",\"evidence\":[],\"riskFlags\":[]}";

    [Fact]
    public async Task Sends_the_strict_chat_completions_contract_without_sensitive_input()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, Completion(ProposalJson))));
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.Endpoint =
            $"https://compatible.example.test/v1/chat/completions?access_token={EndpointQuerySecret}"));

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Proposal, result.Type);
        Assert.Equal(ProposalJson, result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            $"https://compatible.example.test/v1/chat/completions?access_token={EndpointQuerySecret}",
            handler.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(ApiKey, handler.AuthorizationParameter);

        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var root = body.RootElement;
        Assert.Equal("compatibility-model", root.GetProperty("model").GetString());
        var messages = root.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var system = messages[0].GetProperty("content").GetString();
        Assert.Contains("proposal", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shadow", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no commands", system, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        var user = messages[1].GetProperty("content").GetString();
        Assert.NotNull(user);
        using var userIdentity = JsonDocument.Parse(user);
        Assert.Equal("execution-identity", userIdentity.RootElement.GetProperty("executionKey").GetString());
        Assert.Equal(101L, userIdentity.RootElement.GetProperty("instanceId").GetInt64());
        Assert.Equal(202L, userIdentity.RootElement.GetProperty("tokenId").GetInt64());
        Assert.Equal("ai-node", userIdentity.RootElement.GetProperty("nodeId").GetString());
        Assert.Equal(303L, userIdentity.RootElement.GetProperty("definitionVersionId").GetInt64());
        Assert.Equal(505L, userIdentity.RootElement.GetProperty("orgId").GetInt64());
        Assert.Equal(2, userIdentity.RootElement.GetProperty("attempt").GetInt32());
        Assert.False(userIdentity.RootElement.TryGetProperty("starterUserId", out _));
        Assert.False(userIdentity.RootElement.TryGetProperty("businessKey", out _));
        Assert.False(userIdentity.RootElement.TryGetProperty("variablesJson", out _));

        AssertStrictProposalSchema(root.GetProperty("response_format"));

        var serializedResult = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(ApiKey, handler.RequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain(RawVariablesSecret, handler.RequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain(BusinessKey, handler.RequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain(EndpointQuerySecret, handler.RequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, serializedResult, StringComparison.Ordinal);
        Assert.DoesNotContain(RawVariablesSecret, serializedResult, StringComparison.Ordinal);
        Assert.DoesNotContain(EndpointQuerySecret, serializedResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blank_api_key_omits_the_authorization_header()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, Completion(ProposalJson))));
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.ApiKey = "   "));

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Proposal, result.Type);
        Assert.Null(handler.AuthorizationScheme);
        Assert.Null(handler.AuthorizationParameter);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task A_disabled_adapter_fails_closed_without_sending()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("transport must not be touched"));
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.Enabled = false));

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, result.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task A_valid_content_string_is_returned_byte_exactly()
    {
        const string proposal = "{\"schemaVersion\":\"1.0\",\"recommendation\":\"manual\",\"confidence\":0,\"reasonCodes\":[\"POLICY_MATCH\"],\"rationale\":\"  keep whitespace  \",\"evidence\":[],\"riskFlags\":[]}";
        var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, Completion(proposal))));
        using var client = NewClient(handler);
        var provider = NewProvider(client);

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Proposal, result.Type);
        Assert.Equal(proposal, result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
    }

    [Theory]
    [MemberData(nameof(InvalidCompletionEnvelopes))]
    public async Task Refusal_incomplete_or_malformed_completion_envelopes_fail_closed(string completion)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, completion)));
        using var client = NewClient(handler);
        var provider = NewProvider(client);

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, result.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidCompletionEnvelopes()
    {
        yield return ["{\"choices\":[{\"message\":{\"content\":\"ignored\",\"refusal\":\"policy refused\"},\"finish_reason\":\"stop\"}]} "];
        yield return ["{\"choices\":[{\"message\":{\"content\":\"ignored\"},\"finish_reason\":\"length\"}]} "];
        yield return ["{\"choices\":[{\"message\":{\"content\":\"ignored\"},\"finish_reason\":\"content_filter\"}]} "];
        yield return ["{\"choices\":[]}"];
        yield return ["{\"choices\":[{}]}"];
        yield return [JsonSerializer.Serialize(new { choices = new[] { new { message = new { } } } })];
        yield return ["{\"choices\":[{\"message\":{\"content\":\"   \"}}]}"];
        yield return ["{\"choices\":[{\"message\":{\"content\":123}}]}"];
        yield return ["not-json"];
    }

    [Fact]
    public async Task A_chunked_response_larger_than_the_cap_fails_before_parsing()
    {
        var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(new string('x', 1025)), chunkSize: 137);
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        }));
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.ResponseByteCap = 1024));

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, result.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_success_statuses_fail_closed_without_retaining_the_error_body(HttpStatusCode status)
    {
        var secretBody = $"provider failure {ApiKey} {RawVariablesSecret}";
        var handler = new RecordingHandler(_ => Task.FromResult(Response(status, secretBody)));
        using var client = NewClient(handler);
        var provider = NewProvider(client);
        AiDecisionProviderResult? result = null;

        var exception = await Record.ExceptionAsync(async () => result = await provider.ProposeAsync(Request(), CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(AiDecisionProviderResultType.Failed, result!.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(RawVariablesSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_and_response_stream_failures_fail_closed_without_retry_or_leakage()
    {
        var throwingHandler = new RecordingHandler(_ => throw new HttpRequestException($"transport {ApiKey}"));
        using var throwingClient = NewClient(throwingHandler);
        var throwingProvider = NewProvider(throwingClient);

        var transportResult = await throwingProvider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, transportResult.Type);
        Assert.Equal(1, throwingHandler.SendCount);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(transportResult), StringComparison.Ordinal);

        var stream = new ThrowingReadStream(new IOException($"stream {ApiKey}"));
        var streamHandler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        }));
        using var streamClient = NewClient(streamHandler);
        var streamProvider = NewProvider(streamClient);

        var streamResult = await streamProvider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, streamResult.Type);
        Assert.Equal(1, streamHandler.SendCount);
        Assert.True(stream.IsDisposed);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(streamResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_internal_timeout_returns_typed_timeout()
    {
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async cancellationToken =>
        {
            sendStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response(HttpStatusCode.OK, Completion(ProposalJson));
        });
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.TimeoutSeconds = 1));

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.True(sendStarted.Task.IsCompleted);
        Assert.Equal(AiDecisionProviderResultType.TimedOut, result.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task A_pre_cancelled_outer_token_propagates_without_send()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("transport must not be touched"));
        using var client = NewClient(handler);
        var provider = NewProvider(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => provider.ProposeAsync(Request(), cancellation.Token));

        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task A_mid_flight_outer_token_propagates_its_original_token()
    {
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async cancellationToken =>
        {
            sendStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response(HttpStatusCode.OK, Completion(ProposalJson));
        });
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.TimeoutSeconds = 120));
        using var cancellation = new CancellationTokenSource();

        var pending = provider.ProposeAsync(Request(), cancellation.Token);
        await sendStarted.Task;
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => pending);

        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task An_outer_cancellation_wins_when_a_handler_completes_after_it()
    {
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(_ =>
        {
            sendStarted.TrySetResult(true);
            return completeResponse.Task;
        });
        using var client = NewClient(handler);
        var provider = NewProvider(client, Options(options => options.TimeoutSeconds = 120));
        using var cancellation = new CancellationTokenSource();

        var pending = provider.ProposeAsync(Request(), cancellation.Token);
        await sendStarted.Task;
        cancellation.Cancel();
        completeResponse.SetResult(Response(HttpStatusCode.OK, Completion(ProposalJson)));

        var exception = await Record.ExceptionAsync(() => pending);

        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public void Options_bind_validate_and_never_include_api_keys_in_errors()
    {
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Endpoint"] = "https://compatible.example.test/v1/chat/completions?token=server-secret",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "compatibility-model",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:ApiKey"] = ApiKey,
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:TimeoutSeconds"] = "77",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:ResponseByteCap"] = "65536",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:AllowInsecureHttp"] = "true",
        });
        var services = new ServiceCollection();

        services.AddTenonAdminWorkflow(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<WorkflowOptions>().AiDecision.OpenAiCompatible;
        Assert.True(options.Enabled);
        Assert.Equal("https://compatible.example.test/v1/chat/completions?token=server-secret", options.Endpoint);
        Assert.Equal("compatibility-model", options.Model);
        Assert.Equal(ApiKey, options.ApiKey);
        Assert.Equal(77, options.TimeoutSeconds);
        Assert.Equal(65536, options.ResponseByteCap);
        Assert.True(options.AllowInsecureHttp);

        var invalid = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Endpoint"] = "ftp://not-allowed.example.test/",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:ApiKey"] = ApiKey,
        });
        var invalidException = Record.Exception(() => new ServiceCollection().AddTenonAdminWorkflow(invalid));
        Assert.IsType<InvalidOperationException>(invalidException);
        Assert.DoesNotContain(ApiKey, invalidException!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://compatible.example.test/v1/chat/completions", false)]
    [InlineData("https://user:password@compatible.example.test/v1/chat/completions", false)]
    [InlineData("https://compatible.example.test/v1/chat/completions#fragment", false)]
    [InlineData("/v1/chat/completions", false)]
    [InlineData("ftp://compatible.example.test/v1/chat/completions", false)]
    [InlineData("http://compatible.example.test/v1/chat/completions", true)]
    public void Endpoint_validation_requires_a_safe_absolute_http_or_https_uri(string endpoint, bool allowInsecureHttp)
    {
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Endpoint"] = endpoint,
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "compatibility-model",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:AllowInsecureHttp"] = allowInsecureHttp.ToString(),
        });

        var exception = Record.Exception(() => new ServiceCollection().AddTenonAdminWorkflow(configuration));

        if (allowInsecureHttp)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Timeout_bounds_are_validated(int timeoutSeconds)
    {
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:TimeoutSeconds"] = timeoutSeconds.ToString(),
        });

        Assert.IsType<InvalidOperationException>(
            Record.Exception(() => new ServiceCollection().AddTenonAdminWorkflow(configuration)));
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(1048577)]
    public void Response_byte_cap_bounds_are_validated(int responseByteCap)
    {
        var configuration = ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:ResponseByteCap"] = responseByteCap.ToString(),
        });

        Assert.IsType<InvalidOperationException>(
            Record.Exception(() => new ServiceCollection().AddTenonAdminWorkflow(configuration)));
    }

    [Fact]
    public async Task Dependency_injection_selects_fail_closed_or_openai_compatible_without_overriding_consumers()
    {
        var disabledServices = new ServiceCollection();
        disabledServices.AddTenonAdminWorkflow();
        await using var disabledProvider = disabledServices.BuildServiceProvider();
        await using var disabledScope = disabledProvider.CreateAsyncScope();
        Assert.IsType<FailClosedAiDecisionProvider>(disabledScope.ServiceProvider.GetRequiredService<IAiDecisionProvider>());

        var jobs = new AdminJobsOptions();
        using var jobHttpClient = new JobHttpClient(jobs);
        var enabledServices = new ServiceCollection();
        enabledServices.AddSingleton(jobs);
        enabledServices.AddSingleton(jobHttpClient);
        enabledServices.AddTenonAdminWorkflow(ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "compatibility-model",
        }));
        await using var enabledProvider = enabledServices.BuildServiceProvider();
        await using var enabledScope = enabledProvider.CreateAsyncScope();
        Assert.IsType<OpenAiCompatibleAiDecisionProvider>(enabledScope.ServiceProvider.GetRequiredService<IAiDecisionProvider>());

        var consumerServices = new ServiceCollection();
        consumerServices.AddScoped<IAiDecisionProvider, FakeAiDecisionProvider>();
        consumerServices.AddTenonAdminWorkflow(ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "compatibility-model",
        }));
        await using var consumerProvider = consumerServices.BuildServiceProvider();
        await using var consumerScope = consumerProvider.CreateAsyncScope();
        Assert.IsType<FakeAiDecisionProvider>(consumerScope.ServiceProvider.GetRequiredService<IAiDecisionProvider>());
    }

    private static OpenAiCompatibleAiDecisionProvider NewProvider(
        HttpClient client,
        OpenAiCompatibleAiDecisionOptions? options = null,
        TimeProvider? timeProvider = null) => new(
            client,
            options ?? Options(),
            timeProvider ?? TimeProvider.System);

    private static HttpClient NewClient(HttpMessageHandler handler) => new(handler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static OpenAiCompatibleAiDecisionOptions Options(Action<OpenAiCompatibleAiDecisionOptions>? configure = null)
    {
        var options = new OpenAiCompatibleAiDecisionOptions
        {
            Enabled = true,
            Endpoint = "https://compatible.example.test/v1/chat/completions",
            Model = "compatibility-model",
            ApiKey = ApiKey,
            TimeoutSeconds = 30,
            ResponseByteCap = 64 * 1024,
        };
        configure?.Invoke(options);
        return options;
    }

    private static AiDecisionProviderRequest Request() => new()
    {
        ExecutionKey = "execution-identity",
        InstanceId = 101,
        TokenId = 202,
        NodeVisitId = 203,
        NodeId = "ai-node",
        DefinitionVersionId = 303,
        OrgId = 505,
        StarterUserId = 404,
        BusinessKey = BusinessKey,
        Attempt = 2,
        DeadlineAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static HttpResponseMessage Response(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static string Completion(string content) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test",
        model = "compatibility-model",
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content },
                finish_reason = "stop",
            },
        },
        usage = new
        {
            prompt_tokens = 7,
            completion_tokens = 11,
            total_tokens = 18,
        },
    });

    private static IConfiguration ConfigurationFor(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static void AssertStrictProposalSchema(JsonElement responseFormat)
    {
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var schemaEnvelope = responseFormat.GetProperty("json_schema");
        Assert.Equal("tenon_ai_decision_proposal", schemaEnvelope.GetProperty("name").GetString());
        Assert.True(schemaEnvelope.GetProperty("strict").GetBoolean());
        var schema = schemaEnvelope.GetProperty("schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "schemaVersion", "recommendation", "confidence", "reasonCodes", "rationale", "evidence", "riskFlags" },
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray());

        var properties = schema.GetProperty("properties");
        Assert.Equal(AiDecisionProposalParser.SupportedSchemaVersion,
            properties.GetProperty("schemaVersion").GetProperty("enum")[0].GetString());
        Assert.Equal(new[] { "approve", "reject", "manual" },
            properties.GetProperty("recommendation").GetProperty("enum").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(0m, properties.GetProperty("confidence").GetProperty("minimum").GetDecimal());
        Assert.Equal(1m, properties.GetProperty("confidence").GetProperty("maximum").GetDecimal());

        var reasonCodes = properties.GetProperty("reasonCodes");
        Assert.Equal(1, reasonCodes.GetProperty("minItems").GetInt32());
        Assert.Equal(AiDecisionProposalParser.MaximumReasonCodeCount, reasonCodes.GetProperty("maxItems").GetInt32());
        Assert.Equal(AiDecisionProposalParser.MaximumReasonCodeCharacters,
            reasonCodes.GetProperty("items").GetProperty("maxLength").GetInt32());
        Assert.Equal("^[A-Z](?:[A-Z0-9]|_[A-Z0-9])*$",
            reasonCodes.GetProperty("items").GetProperty("pattern").GetString());

        Assert.Equal(AiDecisionProposalParser.MaximumRationaleCharacters,
            properties.GetProperty("rationale").GetProperty("maxLength").GetInt32());
        var evidence = properties.GetProperty("evidence");
        Assert.Equal(AiDecisionProposalParser.MaximumEvidenceCount, evidence.GetProperty("maxItems").GetInt32());
        var evidenceItem = evidence.GetProperty("items");
        Assert.Equal("object", evidenceItem.GetProperty("type").GetString());
        Assert.False(evidenceItem.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "id", "source", "contentHash" },
            evidenceItem.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray());
        var evidenceProperties = evidenceItem.GetProperty("properties");
        Assert.Equal(AiDecisionProposalParser.MaximumEvidenceIdCharacters,
            evidenceProperties.GetProperty("id").GetProperty("maxLength").GetInt32());
        Assert.Equal(AiDecisionProposalParser.MaximumEvidenceSourceCharacters,
            evidenceProperties.GetProperty("source").GetProperty("maxLength").GetInt32());
        Assert.Equal($"^sha256:[0-9a-f]{{{AiDecisionProposalParser.Sha256DigestCharacters}}}$",
            evidenceProperties.GetProperty("contentHash").GetProperty("pattern").GetString());

        var riskFlags = properties.GetProperty("riskFlags");
        Assert.Equal(AiDecisionProposalParser.MaximumRiskFlagCount, riskFlags.GetProperty("maxItems").GetInt32());
        Assert.Equal(AiDecisionProposalParser.MaximumRiskFlagCharacters,
            riskFlags.GetProperty("items").GetProperty("maxLength").GetInt32());
    }

    private sealed class RecordingHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send = send;

        public int SendCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? ContentType { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _send(cancellationToken);
        }
    }

    private sealed class ChunkedReadStream(byte[] data, int chunkSize) : Stream
    {
        private readonly byte[] _data = data;
        private readonly int _chunkSize = chunkSize;
        private int _offset;

        public bool IsDisposed { get; private set; }
        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_offset == _data.Length)
                return ValueTask.FromResult(0);

            var count = Math.Min(Math.Min(buffer.Length, _chunkSize), _data.Length - _offset);
            _data.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        private readonly Exception _exception = exception;

        public bool IsDisposed { get; private set; }
        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw _exception;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(_exception);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
