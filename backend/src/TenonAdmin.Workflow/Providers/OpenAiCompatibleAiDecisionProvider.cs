using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// 只实现 OpenAI-compatible Chat Completions v0 wire contract 的事务外 AI Provider。
/// 它只请求结构化 proposal，仅发送 handler 已构造的安全输入，不发送原始变量、业务键、发起人身份或推进命令。
/// </summary>
public class OpenAiCompatibleAiDecisionProvider : IAiDecisionProvider
{
    private const string ProviderName = "openai-compatible";
    private const string PromptVersion = "v0";
    private const string SystemInstruction =
        "Return a proposal only for a shadow-only workflow review. Issue no commands. " +
        "Do not change workflow state, approve, reject, or direct any action.";

    private static readonly string[] ProposalRequiredProperties =
    [
        "schemaVersion",
        "recommendation",
        "confidence",
        "reasonCodes",
        "rationale",
        "evidence",
        "riskFlags",
    ];

    private static readonly string[] EvidenceRequiredProperties =
    [
        "id",
        "source",
        "contentHash",
    ];

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeProvider _timeProvider;
    private readonly bool _enabled;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly TimeSpan _configuredTimeout;
    private readonly int _responseByteCap;

    /// <summary>使用注入的共享 HTTP client、有效 Provider 配置和时钟构造 adapter。</summary>
    public OpenAiCompatibleAiDecisionProvider(
        HttpClient httpClient,
        OpenAiCompatibleAiDecisionOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();

        _httpClient = httpClient;
        _endpoint = options.CreateValidatedEndpoint();
        _timeProvider = timeProvider;
        _enabled = options.Enabled;
        _model = options.Model;
        _apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey;
        _configuredTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _responseByteCap = options.ResponseByteCap;
    }

    /// <inheritdoc />
    public virtual async Task<AiDecisionProviderResult> ProposeAsync(
        AiDecisionProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiDecisionProviderRequestValidator.Validate(request);
        if (!_enabled)
        {
            return Failed();
        }

        var deadlineRemaining = request.DeadlineAtUtc - _timeProvider.GetUtcNow();
        var effectiveTimeout = deadlineRemaining < _configuredTimeout ? deadlineRemaining : _configuredTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            ThrowIfExternalCancellationRequested(cancellationToken);
            return TimedOut();
        }

        using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCancellation.CancelAfter(effectiveTimeout);
        var callToken = callCancellation.Token;
        HttpRequestMessage? requestMessage = null;
        Task<HttpResponseMessage>? sendTask = null;
        var sendResponseObserved = false;

        try
        {
            requestMessage = CreateRequestMessage(request);
            sendTask = _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, callToken);
            using var response = await sendTask.WaitAsync(callToken);
            sendResponseObserved = true;
            ThrowIfExternalCancellationRequested(cancellationToken);
            if (callCancellation.IsCancellationRequested)
            {
                return TimedOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failed();
            }

            var responseBytes = await ReadResponseBytesAsync(response, callToken);
            ThrowIfExternalCancellationRequested(cancellationToken);
            if (callCancellation.IsCancellationRequested)
            {
                return TimedOut();
            }

            if (responseBytes is null
                || !TryReadProposalContent(responseBytes, out var proposalJson, out var usage))
            {
                return Failed();
            }

            ThrowIfExternalCancellationRequested(cancellationToken);
            return Proposal(proposalJson, usage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (callCancellation.IsCancellationRequested)
        {
            return TimedOut();
        }
        catch (OperationCanceledException)
        {
            return Failed();
        }
        catch (HttpRequestException)
        {
            ThrowIfExternalCancellationRequested(cancellationToken);
            return callCancellation.IsCancellationRequested
                ? TimedOut()
                : Failed();
        }
        catch (IOException)
        {
            ThrowIfExternalCancellationRequested(cancellationToken);
            return callCancellation.IsCancellationRequested
                ? TimedOut()
                : Failed();
        }
        catch (JsonException)
        {
            ThrowIfExternalCancellationRequested(cancellationToken);
            return callCancellation.IsCancellationRequested
                ? TimedOut()
                : Failed();
        }
        finally
        {
            if (requestMessage is not null)
            {
                if (sendTask is not null && !sendResponseObserved)
                {
                    _ = ObserveLateSendAndDisposeAsync(sendTask, requestMessage);
                }
                else
                {
                    requestMessage.Dispose();
                }
            }
        }
    }

    /// <summary>构造不携带 workflow 原始变量或业务关联键的最小固定协议请求。</summary>
    protected virtual HttpRequestMessage CreateRequestMessage(AiDecisionProviderRequest request)
    {
        var userMessage = JsonSerializer.Serialize(new
        {
            executionKey = request.ExecutionKey,
            instanceId = request.InstanceId,
            tokenId = request.TokenId,
            nodeVisitId = request.NodeVisitId,
            nodeId = request.NodeId,
            definitionVersionId = request.DefinitionVersionId,
            orgId = request.OrgId,
            attempt = request.Attempt,
            instructions = request.Instructions,
            inputs = request.Inputs,
            inputHash = request.InputHash,
        });
        var requestBody = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = SystemInstruction },
                new { role = "user", content = userMessage },
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "tenon_ai_decision_proposal",
                    strict = true,
                    schema = CreateProposalSchema(),
                },
            },
        });

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(requestBody, Encoding.UTF8),
        };
        requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (_apiKey is not null)
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return requestMessage;
    }

    private static object CreateProposalSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = ProposalRequiredProperties,
        properties = new
        {
            schemaVersion = new
            {
                type = "string",
                @enum = new[] { AiDecisionProposalParser.SupportedSchemaVersion },
            },
            recommendation = new
            {
                type = "string",
                @enum = new[] { "approve", "reject", "manual" },
            },
            confidence = new
            {
                type = "number",
                minimum = 0,
                maximum = 1,
            },
            reasonCodes = new
            {
                type = "array",
                minItems = 1,
                maxItems = AiDecisionProposalParser.MaximumReasonCodeCount,
                items = new
                {
                    type = "string",
                    minLength = 1,
                    maxLength = AiDecisionProposalParser.MaximumReasonCodeCharacters,
                    pattern = "^[A-Z](?:[A-Z0-9]|_[A-Z0-9])*$",
                },
            },
            rationale = new
            {
                type = "string",
                minLength = 1,
                maxLength = AiDecisionProposalParser.MaximumRationaleCharacters,
            },
            evidence = new
            {
                type = "array",
                minItems = 0,
                maxItems = AiDecisionProposalParser.MaximumEvidenceCount,
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = EvidenceRequiredProperties,
                    properties = new
                    {
                        id = new
                        {
                            type = "string",
                            minLength = 1,
                            maxLength = AiDecisionProposalParser.MaximumEvidenceIdCharacters,
                        },
                        source = new
                        {
                            type = "string",
                            minLength = 1,
                            maxLength = AiDecisionProposalParser.MaximumEvidenceSourceCharacters,
                        },
                        contentHash = new
                        {
                            type = "string",
                            pattern = $"^sha256:[0-9a-f]{{{AiDecisionProposalParser.Sha256DigestCharacters}}}$",
                        },
                    },
                },
            },
            riskFlags = new
            {
                type = "array",
                minItems = 0,
                maxItems = AiDecisionProposalParser.MaximumRiskFlagCount,
                items = new
                {
                    type = "string",
                    minLength = 1,
                    maxLength = AiDecisionProposalParser.MaximumRiskFlagCharacters,
                },
            },
        },
    };

    private async Task<byte[]?> ReadResponseBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken);
        using var bytes = new MemoryStream(Math.Min(_responseByteCap, 8192));
        var buffer = new byte[Math.Min(_responseByteCap + 1, 8192)];
        var total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask().WaitAsync(cancellationToken);
            if (count == 0)
            {
                return bytes.ToArray();
            }

            total += count;
            if (total > _responseByteCap)
            {
                return null;
            }

            await bytes.WriteAsync(buffer.AsMemory(0, count), cancellationToken).AsTask().WaitAsync(cancellationToken);
        }
    }

    private static bool TryReadProposalContent(
        byte[] responseBytes,
        out string proposalJson,
        out (long? PromptTokens, long? CompletionTokens, long? TotalTokens) usage)
    {
        proposalJson = "";
        usage = default;
        using var document = JsonDocument.Parse(responseBytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return false;
        }

        var choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object
            || !choice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !choice.TryGetProperty("finish_reason", out var finishReason)
            || finishReason.ValueKind != JsonValueKind.String
            || !string.Equals(finishReason.GetString(), "stop", StringComparison.Ordinal))
        {
            return false;
        }

        if (message.TryGetProperty("refusal", out var refusal)
            && refusal.ValueKind != JsonValueKind.Null
            && (refusal.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(refusal.GetString())))
        {
            return false;
        }

        if (!message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
        {
            return false;
        }

        if (!TryReadUsage(root, out usage))
        {
            return false;
        }

        proposalJson = content.GetString()!;
        return true;
    }

    private static bool TryReadUsage(
        JsonElement root,
        out (long? PromptTokens, long? CompletionTokens, long? TotalTokens) usage)
    {
        usage = default;
        if (!root.TryGetProperty("usage", out var usageElement)
            || usageElement.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (usageElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryReadToken(usageElement, "prompt_tokens", out var promptTokens)
            || !TryReadToken(usageElement, "completion_tokens", out var completionTokens)
            || !TryReadToken(usageElement, "total_tokens", out var totalTokens))
        {
            return false;
        }

        usage = (promptTokens, completionTokens, totalTokens);
        return true;
    }

    private static bool TryReadToken(JsonElement usage, string name, out long? value)
    {
        value = null;
        if (!usage.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var parsed)
            || parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private AiDecisionProviderResult Failed() =>
        AiDecisionProviderResult.Failed(null, ProviderName, AuditModel, PromptVersion);

    private AiDecisionProviderResult TimedOut() =>
        AiDecisionProviderResult.TimedOut(ProviderName, AuditModel, PromptVersion);

    private AiDecisionProviderResult Proposal(
        string proposalJson,
        (long? PromptTokens, long? CompletionTokens, long? TotalTokens) usage) =>
        AiDecisionProviderResult.Proposal(
            proposalJson,
            ProviderName,
            AuditModel,
            PromptVersion,
            usage.PromptTokens,
            usage.CompletionTokens,
            usage.TotalTokens);

    private string? AuditModel => string.IsNullOrWhiteSpace(_model) ? null : _model;

    private static async Task ObserveLateSendAndDisposeAsync(
        Task<HttpResponseMessage> sendTask,
        HttpRequestMessage requestMessage)
    {
        try
        {
            using var response = await sendTask;
        }
        catch
        {
            // WaitAsync 已向调用方交付取消；这里仅观察迟到 transport 异常，避免未观察的 task fault。
        }
        finally
        {
            requestMessage.Dispose();
        }
    }

    private static void ThrowIfExternalCancellationRequested(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
