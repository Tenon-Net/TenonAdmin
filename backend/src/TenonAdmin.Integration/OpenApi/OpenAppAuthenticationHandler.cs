using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>接入应用认证方案选项。</summary>
public class OpenAppAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>凭据请求头名(默认 <see cref="OpenAppAuthenticationDefaults.HeaderName"/>)。</summary>
    public string HeaderName { get; set; } = OpenAppAuthenticationDefaults.HeaderName;
}

/// <summary>
/// 接入应用认证处理器(实现契约 §4.4)。只从请求头读取凭据,交给 <see cref="IOpenAppCredentialValidator"/>;
/// 通过即构造不带用户 claim 的系统身份主体。
/// <list type="bullet">
/// <item>凭据出现在查询串里:直接拒绝(不校验、不落库原文),杜绝凭据进入 URL、访问日志与链路追踪;</item>
/// <item>认证失败过多:在查库前拒绝(429 + <see cref="IntegrationErrorCode.AuthenticationThrottled"/>),计数分区见 <see cref="ThrottleKey"/>;</item>
/// <item>其余失败统一 401 + <see cref="IntegrationErrorCode.CredentialInvalid"/>,对外不区分原因,原因只进调用记录。</item>
/// </list>
/// 本处理器从不记录凭据原文。
/// </summary>
public class OpenAppAuthenticationHandler(
    IOptionsMonitor<OpenAppAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOpenAppCredentialValidator validator,
    IOpenAppRateLimiter rateLimiter,
    TimeProvider time)
    : AuthenticationHandler<OpenAppAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var state = OpenApiRequestState.GetOrCreate(Context, time);
        if (state.Validation is { IsValid: true } done)   // 同一请求内重复认证(多方案合并等)复用结果
            return AuthenticateResult.Success(new AuthenticationTicket(done.Identity!.ToPrincipal(Scheme.Name), Scheme.Name));

        if (QueryCarriesCredential())
        {
            state.CredentialInUrl = true;
            state.Validation = OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Malformed);
            return AuthenticateResult.Fail("接入凭据不能放在 URL 里。");
        }

        var values = Request.Headers[Options.HeaderName];
        var throttleKey = ThrottleKey(values.Count == 1 ? values[0] : null);
        var throttle = await rateLimiter.CheckAuthenticationAsync(throttleKey, Context.RequestAborted);
        if (!throttle.Allowed)
        {
            state.AuthThrottledRetryAfter = throttle.RetryAfterSeconds;
            state.Validation = OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Missing);
            return AuthenticateResult.Fail("认证失败过多,暂时拒绝。");
        }

        var validation = values.Count switch
        {
            0 => OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Missing),
            1 => await validator.ValidateAsync(values[0], Context.RequestAborted),
            _ => OpenAppCredentialValidation.Fail(OpenAppCredentialFailure.Malformed),   // 多值头:拒绝猜测用哪一个
        };
        state.Validation = validation;

        if (!validation.IsValid)
        {
            await rateLimiter.RecordAuthenticationFailureAsync(throttleKey, Context.RequestAborted);
            return AuthenticateResult.Fail("接入凭据无效。");   // 固定文案:不带原因、不带凭据
        }

        var principal = validation.Identity!.ToPrincipal(Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var state = OpenApiRequestState.GetOrCreate(Context, time);
        var recorder = Context.RequestServices.GetRequiredService<OpenApiCallRecorder>();
        OpenApiResponseHeaders.Apply(Context, time);

        if (state.AuthThrottledRetryAfter is { } retryAfter)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            await Response.WriteAsJsonAsync(Result<object>.Fail((ErrorCode)IntegrationErrorCode.AuthenticationThrottled,
                new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter }, "认证失败次数过多,请稍后再试。"));
            await recorder.RecordAuthenticationFailureAsync(Context, StatusCodes.Status429TooManyRequests,
                IntegrationErrorCode.AuthenticationThrottled, "auth_throttled");
            return;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"ApiKey header=\"{Options.HeaderName}\"";
        await Response.WriteAsJsonAsync(Result<object>.Fail(
            (ErrorCode)IntegrationErrorCode.CredentialInvalid, null, "接入凭据无效或已失效。"));
        await recorder.RecordAuthenticationFailureAsync(Context, StatusCodes.Status401Unauthorized,
            IntegrationErrorCode.CredentialInvalid, state.CredentialInUrl ? "credential_in_url" : null);
    }

    /// <inheritdoc />
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        OpenApiResponseHeaders.Apply(Context, time);
        await Response.WriteAsJsonAsync(Result<object>.Fail(
            (ErrorCode)IntegrationErrorCode.PermissionDenied, null, "接入应用无权访问该接口。"));
        await Context.RequestServices.GetRequiredService<OpenApiCallRecorder>()
            .RecordDeniedAsync(Context, StatusCodes.Status403Forbidden, IntegrationErrorCode.PermissionDenied,
                InboundCallOutcome.Forbidden, "forbidden");
    }

    /// <summary>来源:客户端 IP(反代后须由内核转发头配置还原真实 IP)。</summary>
    protected virtual string ClientKey() => Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// 认证失败的计数分区:格式正确的凭据按(来源, 凭据标识)计——对某个标识猜秘密只会暂时锁住这个标识;
    /// 缺失、格式不对或多值的按来源计——刷格式错误的请求不会连累同一来源(如同一出口 IP)的合法凭据。
    /// </summary>
    protected virtual string ThrottleKey(string? presentedKey) =>
        OpenAppApiKey.TryParse(presentedKey, out var keyId, out _) ? $"{ClientKey()}#{keyId}" : ClientKey();

    /// <summary>查询串里是否带了形似接入凭据的值(按固定前缀识别,不管参数名)。</summary>
    protected virtual bool QueryCarriesCredential()
    {
        foreach (var (_, values) in Request.Query)
            foreach (var value in values)
                if (value is not null && value.Contains(OpenAppApiKey.Prefix, StringComparison.Ordinal))
                    return true;
        return false;
    }
}

/// <summary>开放响应的追踪头:<c>X-Trace-Id</c> 总是写;调用方给了合法 <c>X-Request-Id</c> 时原样回显。</summary>
internal static class OpenApiResponseHeaders
{
    public static void Apply(HttpContext http, TimeProvider time)
    {
        var state = OpenApiRequestState.GetOrCreate(http, time);
        if (http.Response.HasStarted) return;
        http.Response.Headers[OpenApiHeaders.TraceId] = state.TraceId;
        if (state.ClientRequestId is { } requestId)
            http.Response.Headers[OpenApiHeaders.RequestId] = requestId;
    }
}
