using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 把一次开放请求的结局整理成调用记录:认证失败(挑战)、授权/范围/限流拒绝、正常完成三个出口各调一次,
/// 同一请求只记一次。认证失败且凭据不可识别(缺失、格式错、标识不存在)时不落库——这类请求对应不到任何应用,
/// 落库只会给未认证洪泛一个写放大入口;改为不含凭据的信息级日志。
/// </summary>
internal sealed class OpenApiCallRecorder(IInboundLogService logs, TimeProvider time, ILogger<OpenApiCallRecorder> logger)
{
    /// <summary>认证失败出口(401,或来源失败过多的 429)。<paramref name="reasonOverride"/> 为空时按凭据校验结果取原因。</summary>
    public Task RecordAuthenticationFailureAsync(HttpContext http, int statusCode, int resultCode, string? reasonOverride = null)
    {
        var state = OpenApiRequestState.GetOrCreate(http, time);
        var validation = state.Validation;
        var reason = reasonOverride ?? ReasonOf(validation?.Failure ?? OpenAppCredentialFailure.Missing);
        var outcome = statusCode == StatusCodes.Status429TooManyRequests ? InboundCallOutcome.RateLimited : InboundCallOutcome.Unauthorized;
        if (validation?.AppId is null)
        {
            if (!state.Recorded)
            {
                state.Recorded = true;
                logger.LogInformation("开放接口认证失败(凭据不可识别,不落库)。Reason={Reason} Path={Path} TraceId={TraceId}",
                    reason, http.Request.Path.Value, state.TraceId);
            }
            return Task.CompletedTask;
        }

        return RecordAsync(http, statusCode, resultCode, outcome, reason);
    }

    /// <summary>授权阶段的拒绝出口(403 / 429)。</summary>
    public Task RecordDeniedAsync(HttpContext http, int statusCode, int resultCode, InboundCallOutcome outcome, string reason) =>
        RecordAsync(http, statusCode, resultCode, outcome, reason);

    /// <summary>动作(含模型校验、业务异常、未处理异常)执行完毕的出口。</summary>
    public Task RecordCompletionAsync(HttpContext http, ResourceExecutedContext executed)
    {
        if (executed.Exception is { } ex && !executed.ExceptionHandled)
            return RecordAsync(http, StatusCodes.Status500InternalServerError, (int)ErrorCode.SystemError,
                InboundCallOutcome.Error, "exception:" + ex.GetType().Name);

        var status = http.Response.StatusCode;
        var code = executed.Result is ObjectResult { Value: IResultEnvelope envelope } ? envelope.Code : 0;
        var reason = OpenApiRequestState.Peek(http)?.ExceptionType is { } type ? "exception:" + type : null;
        var outcome = status switch
        {
            >= 500 => InboundCallOutcome.Error,
            StatusCodes.Status401Unauthorized => InboundCallOutcome.Unauthorized,
            StatusCodes.Status403Forbidden => InboundCallOutcome.Forbidden,
            StatusCodes.Status429TooManyRequests => InboundCallOutcome.RateLimited,
            >= 400 => InboundCallOutcome.InvalidRequest,
            _ => code == 0 ? InboundCallOutcome.Succeeded : InboundCallOutcome.BusinessFailed,
        };
        return RecordAsync(http, status, code, outcome, reason);
    }

    private async Task RecordAsync(HttpContext http, int statusCode, int resultCode, InboundCallOutcome outcome, string? reason)
    {
        var state = OpenApiRequestState.GetOrCreate(http, time);
        if (state.Recorded) return;
        state.Recorded = true;

        var identity = state.Identity;
        var validation = state.Validation;
        await logs.RecordAsync(new InboundLogEntry
        {
            AppId = identity?.AppId ?? validation?.AppId,
            AppCode = identity?.AppCode,
            CredentialId = identity?.CredentialId ?? validation?.CredentialId,
            KeyId = identity?.KeyId ?? validation?.KeyId,
            HttpMethod = http.Request.Method,
            Route = state.Endpoint?.Permission ?? "",
            Path = http.Request.Path.Value ?? "",
            StatusCode = statusCode,
            ResultCode = resultCode,
            Outcome = outcome,
            FailureReason = reason,
            ElapsedMs = (long)time.GetElapsedTime(state.StartTimestamp).TotalMilliseconds,
            TraceId = state.TraceId,
            ClientRequestId = state.ClientRequestId,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
        }, http.RequestAborted);
    }

    /// <summary>凭据失败原因 → 调用记录短码。</summary>
    public static string ReasonOf(OpenAppCredentialFailure failure) => failure switch
    {
        OpenAppCredentialFailure.Missing => "credential_missing",
        OpenAppCredentialFailure.Malformed => "credential_malformed",
        OpenAppCredentialFailure.NotFound => "credential_not_found",
        OpenAppCredentialFailure.SecretMismatch => "credential_mismatch",
        OpenAppCredentialFailure.Revoked => "credential_revoked",
        OpenAppCredentialFailure.Expired => "credential_expired",
        OpenAppCredentialFailure.AppDisabled => "app_disabled",
        OpenAppCredentialFailure.AppNotFound => "app_not_found",
        _ => "credential_invalid",
    };
}
