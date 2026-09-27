using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>M3B0-05 review regressions: Jobs HTTP policy, plaintext-key policy, terminal finish reason, and late-send disposal.</summary>
public class WfOpenAiCompatibleAiDecisionProviderReviewRegressionTests
{
    private const string ApiKey = "tenon-test-review-api-key";
    private const string EndpointQuerySecret = "tenon-test-review-endpoint-query-secret";
    private const string ProposalJson =
        "{\"schemaVersion\":\"1.0\",\"recommendation\":\"approve\",\"confidence\":0.95,\"reasonCodes\":[\"POLICY_MATCH\"],\"rationale\":\"proposal only\",\"evidence\":[],\"riskFlags\":[]}";

    [Fact]
    public async Task Built_in_provider_rejects_an_endpoint_outside_jobs_allowed_hosts_before_any_send()
    {
        var jobs = new AdminJobsOptions
        {
            Http = new AdminJobsHttpOptions
            {
                AllowedHosts = ["approved.example.test"],
            },
        };
        var handler = new CountingHandler(_ => throw new InvalidOperationException("must not send"));
        using var jobHttpClient = NewJobHttpClient(jobs, handler);
        var services = new ServiceCollection();
        services.AddSingleton(jobs);
        services.AddSingleton(jobHttpClient);
        services.AddTenonAdminWorkflow(ConfigurationFor(new Dictionary<string, string?>
        {
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Enabled"] = "true",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Endpoint"] =
                $"https://unapproved.example.test/v1/chat/completions?token={EndpointQuerySecret}",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:Model"] = "compatibility-model",
            ["TenonAdmin:Workflow:AiDecision:OpenAiCompatible:ApiKey"] = ApiKey,
        }));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var exception = Record.Exception(() => scope.ServiceProvider.GetRequiredService<IAiDecisionProvider>());

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, handler.SendCount);
        Assert.DoesNotContain(ApiKey, exception!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(EndpointQuerySecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_endpoint_rejects_a_nonblank_api_key_even_when_insecure_http_is_enabled()
    {
        var handler = new CountingHandler(_ => throw new InvalidOperationException("must not send"));
        using var client = NewClient(handler);
        var options = Options();
        options.Endpoint = $"http://compatible.example.test/v1/chat/completions?token={EndpointQuerySecret}";
        options.AllowInsecureHttp = true;

        var exception = Record.Exception(() => new OpenAiCompatibleAiDecisionProvider(client, options, TimeProvider.System));

        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(0, handler.SendCount);
        Assert.DoesNotContain(ApiKey, exception!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(EndpointQuerySecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NonTerminalCompletionEnvelopes))]
    public async Task A_missing_or_null_finish_reason_fails_closed(string completion)
    {
        var handler = new CountingHandler(_ => Task.FromResult(Response(completion)));
        using var client = NewClient(handler);
        var provider = new OpenAiCompatibleAiDecisionProvider(client, Options(), TimeProvider.System);

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Failed, result.Type);
        Assert.Null(result.ProposalJson);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task Valid_usage_metadata_reaches_the_typed_provider_result()
    {
        var completion = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { role = "assistant", content = ProposalJson },
                    finish_reason = "stop",
                },
            },
            usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 },
        });
        var handler = new CountingHandler(_ => Task.FromResult(Response(completion)));
        using var client = NewClient(handler);
        var result = await new OpenAiCompatibleAiDecisionProvider(client, Options(), TimeProvider.System)
            .ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.Proposal, result.Type);
        Assert.Equal("openai-compatible", result.Provider);
        Assert.Equal("compatibility-model", result.Model);
        Assert.Equal("v0", result.PromptVersion);
        Assert.Equal(11, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);
        Assert.Equal(18, result.TotalTokens);
    }

    public static IEnumerable<object[]> NonTerminalCompletionEnvelopes()
    {
        yield return [JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = ProposalJson } },
            },
        })];
        yield return [JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = ProposalJson }, finish_reason = (string?)null },
            },
        })];
    }

    [Fact]
    public async Task External_cancellation_observes_and_disposes_a_late_response_without_disposing_its_request_early()
    {
        var lateResponseContent = new DisposeTrackingContent();
        var handler = new CancellationIgnoringHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = lateResponseContent,
        });
        using var client = NewClient(handler);
        var provider = new OpenAiCompatibleAiDecisionProvider(client, Options(), TimeProvider.System);
        using var cancellation = new CancellationTokenSource();

        var pending = provider.ProposeAsync(Request(), cancellation.Token);
        await handler.Started.Task;
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(() => pending);
        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);

        handler.Complete();
        var requestContentWasReadable = await handler.RequestContentWasReadable.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await lateResponseContent.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(requestContentWasReadable);
    }

    [Fact]
    public async Task Internal_timeout_observes_and_disposes_a_late_response_without_disposing_its_request_early()
    {
        var lateResponseContent = new DisposeTrackingContent();
        var handler = new CancellationIgnoringHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = lateResponseContent,
        });
        using var client = NewClient(handler);
        var options = Options();
        options.TimeoutSeconds = 1;
        var provider = new OpenAiCompatibleAiDecisionProvider(client, options, TimeProvider.System);

        var result = await provider.ProposeAsync(Request(), CancellationToken.None);

        Assert.Equal(AiDecisionProviderResultType.TimedOut, result.Type);
        handler.Complete();
        var requestContentWasReadable = await handler.RequestContentWasReadable.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await lateResponseContent.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(requestContentWasReadable);
    }

    private static JobHttpClient NewJobHttpClient(AdminJobsOptions jobs, HttpMessageHandler handler)
    {
        var jobHttpClient = new JobHttpClient(jobs);
        var replacement = NewClient(handler);
        jobHttpClient.Client.Dispose();
        var backingField = typeof(JobHttpClient).GetField("<Client>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField);
        backingField!.SetValue(jobHttpClient, replacement);
        return jobHttpClient;
    }

    private static OpenAiCompatibleAiDecisionOptions Options() => new()
    {
        Enabled = true,
        Endpoint = "https://compatible.example.test/v1/chat/completions",
        Model = "compatibility-model",
        ApiKey = ApiKey,
        TimeoutSeconds = 30,
        ResponseByteCap = 64 * 1024,
    };

    private static HttpClient NewClient(HttpMessageHandler handler) => new(handler, disposeHandler: true)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static AiDecisionProviderRequest Request() => new()
    {
        ExecutionKey = "review-execution",
        InstanceId = 101,
        TokenId = 202,
        NodeId = "ai-node",
        DefinitionVersionId = 303,
        StarterUserId = 404,
        Attempt = 1,
        DeadlineAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static HttpResponseMessage Response(string completion) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(completion, Encoding.UTF8, "application/json"),
    };

    private static IConfiguration ConfigurationFor(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class CountingHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send = send;

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return _send(cancellationToken);
        }
    }

    private sealed class CancellationIgnoringHandler(HttpResponseMessage lateResponse) : HttpMessageHandler
    {
        private readonly HttpResponseMessage _lateResponse = lateResponse;
        private readonly TaskCompletionSource<bool> _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RequestContentWasReadable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => _complete.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await _complete.Task;
            try
            {
                _ = await request.Content!.ReadAsStringAsync(CancellationToken.None);
                RequestContentWasReadable.TrySetResult(true);
            }
            catch (ObjectDisposedException)
            {
                RequestContentWasReadable.TrySetResult(false);
            }

            return _lateResponse;
        }
    }

    private sealed class DisposeTrackingContent : HttpContent
    {
        public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Disposed.TrySetResult(true);
            base.Dispose(disposing);
        }
    }
}
