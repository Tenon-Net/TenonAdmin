using Microsoft.Extensions.Configuration;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public abstract record LiveProviderSetup
{
    private LiveProviderSetup()
    {
    }

    public sealed record Skipped(string Reason) : LiveProviderSetup;

    public sealed record Ready(
        IAiDecisionProvider Provider,
        AiDecisionPolicyOptions Policy,
        string Model,
        int TimeoutSeconds,
        bool HttpApiKeyOptIn,
        IDisposable Lifetime) : LiveProviderSetup;
}

public static class ExpenseEvalProviders
{
    public static int LiveConstructions { get; private set; }

    /// <summary>仅供自检回归使用。为 true 时解析真实 Provider 会失败，避免自检误打到网络。</summary>
    public static bool RejectResolve { get; set; }

    public static LiveProviderSetup Resolve(IConfiguration configuration, HttpMessageHandler? handler)
    {
        if (RejectResolve)
        {
            throw new ExpenseEvalException("自检不应创建 Provider。");
        }

        ArgumentNullException.ThrowIfNull(configuration);
        WorkflowOptions options;
        try
        {
            var section = configuration.GetSection("TenonAdmin:Workflow");
            options = section.Get<WorkflowOptions>() ?? new WorkflowOptions();
            BindPolicyArrays(section, options);
        }
        catch (Exception)
        {
            return new LiveProviderSetup.Skipped("provider-configuration-invalid");
        }

        var openAi = options.AiDecision?.OpenAiCompatible;
        var policy = options.AiDecision?.Policy;
        if (openAi is null || policy is null)
        {
            return new LiveProviderSetup.Skipped("provider-configuration-invalid");
        }

        if (!openAi.Enabled)
        {
            return new LiveProviderSetup.Skipped("provider-not-enabled");
        }

        var httpApiKey = TakeExplicitHttpApiKey(configuration, openAi);
        if (httpApiKey is null && HasRejectedHttpApiKey(configuration, openAi))
        {
            return new LiveProviderSetup.Skipped("provider-configuration-invalid");
        }

        try
        {
            policy.Validate();
            openAi.Validate();
        }
        catch (Exception)
        {
            return new LiveProviderSetup.Skipped("provider-configuration-invalid");
        }

        var disposeHandler = handler is null;
        HttpMessageHandler transport = handler ?? new SocketsHttpHandler();
        if (httpApiKey is not null)
        {
            transport = new EvalHttpApiKeyHandler(transport, httpApiKey);
        }

        var client = new HttpClient(transport, disposeHandler) { Timeout = Timeout.InfiniteTimeSpan };
        LiveConstructions++;
        return new LiveProviderSetup.Ready(
            new OpenAiCompatibleAiDecisionProvider(client, openAi, TimeProvider.System),
            policy,
            openAi.Model,
            openAi.TimeoutSeconds,
            httpApiKey is not null,
            client);
    }

    /// <summary>
    /// 评测进程的显式例外：生产校验仍拒绝 HTTP 携带 ApiKey。
    /// 只有配置同时打开 AllowInsecureHttp 和 AllowHttpApiKey 时，才把密钥从选项里拿掉，
    /// 改由评测自己的请求头附加。密钥不进入报告。
    /// </summary>
    private static string? TakeExplicitHttpApiKey(IConfiguration configuration, OpenAiCompatibleAiDecisionOptions openAi)
    {
        if (!configuration.GetValue("TenonAdmin:Workflow:AiDecision:OpenAiCompatible:AllowHttpApiKey", false)
            || !openAi.AllowInsecureHttp
            || !IsHttp(openAi.Endpoint)
            || string.IsNullOrWhiteSpace(openAi.ApiKey))
        {
            return null;
        }

        var apiKey = openAi.ApiKey;
        openAi.ApiKey = null;
        return apiKey;
    }

    private static bool HasRejectedHttpApiKey(IConfiguration configuration, OpenAiCompatibleAiDecisionOptions openAi) =>
        !configuration.GetValue("TenonAdmin:Workflow:AiDecision:OpenAiCompatible:AllowHttpApiKey", false)
        && IsHttp(openAi.Endpoint)
        && !string.IsNullOrWhiteSpace(openAi.ApiKey);

    private static bool IsHttp(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static void BindPolicyArrays(IConfigurationSection section, WorkflowOptions options)
    {
        var policyConfiguration = section.GetSection("AiDecision:Policy");
        var policy = options.AiDecision?.Policy;
        if (policy is null || !policyConfiguration.Exists())
        {
            return;
        }

        var highRiskFlags = policyConfiguration.GetSection(nameof(AiDecisionPolicyOptions.HighRiskFlags));
        if (highRiskFlags.Exists())
        {
            policy.HighRiskFlags = highRiskFlags.Get<string[]>() ?? [];
        }

        var allowedReasonCodes = policyConfiguration.GetSection(nameof(AiDecisionPolicyOptions.AllowedReasonCodes));
        if (allowedReasonCodes.Exists())
        {
            policy.AllowedReasonCodes = allowedReasonCodes.Get<string[]>() ?? [];
        }
    }

    private sealed class EvalHttpApiKeyHandler : DelegatingHandler
    {
        private readonly string _apiKey;

        public EvalHttpApiKeyHandler(HttpMessageHandler inner, string apiKey)
            : base(inner)
        {
            _apiKey = apiKey;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
