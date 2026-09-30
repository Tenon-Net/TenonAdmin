using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放端点授权过滤器(实现契约 §4.4,由 <see cref="OpenApiConvention"/> 挂到每个开放动作上)。依次:
/// <list type="number">
/// <item>先把内核数据范围写成<b>拒绝</b>——任何提前返回路径都不会留下「未设置即不受限」的默认值;</item>
/// <item>必须是接入应用主体(用户令牌进不来);按应用限流(429 <see cref="IntegrationErrorCode.RateLimited"/>);</item>
/// <item>端点权限码必须在应用授权快照里(默认拒绝 → 403 <see cref="IntegrationErrorCode.PermissionDenied"/>);</item>
/// <item>端点声明的范围必须已绑定(未绑定 → 403 <see cref="IntegrationErrorCode.ScopeNotBound"/>),由策略解析后写入
/// <see cref="IOpenAppContext"/> 与内核 <see cref="IDataScopeContext"/>。</item>
/// </list>
/// 拒绝时直接写统一信封并记开放调用记录。
/// </summary>
internal sealed class OpenApiAuthorizationFilter(
    IOpenApiCatalog catalog,
    IOpenAppAuthorizationService authorization,
    OpenApiDataScopePolicyRegistry policies,
    IDataScopeContext dataScope,
    IOpenAppRateLimiter rateLimiter,
    IntegrationOptions options,
    OpenApiCallRecorder recorder,
    TimeProvider time) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        dataScope.Current = OpenAppDataScope.DenyKernelScope;
        var state = OpenApiRequestState.GetOrCreate(http, time);
        OpenApiResponseHeaders.Apply(http, time);

        var identity = OpenAppIdentity.FromPrincipal(http.User);
        if (identity is null)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, IntegrationErrorCode.CredentialInvalid,
                InboundCallOutcome.Unauthorized, "not_open_app_principal", "接入凭据无效或已失效。");
            return;
        }
        state.Identity = identity;

        // 按应用限流先于授权判定:对未授予端点的反复试探同样消耗额度
        var snapshot = await authorization.GetSnapshotAsync(identity, http.RequestAborted);
        var limit = snapshot.RateLimitPerMinute ?? options.OpenApi.DefaultRateLimitPerMinute;
        var rate = await rateLimiter.AcquireAsync(identity.AppId, limit, http.RequestAborted);
        if (!rate.Allowed)
        {
            http.Response.Headers.RetryAfter = rate.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await DenyAsync(context, StatusCodes.Status429TooManyRequests, IntegrationErrorCode.RateLimited,
                InboundCallOutcome.RateLimited, "app_rate_limited", "接入应用调用过于频繁,请稍后再试。",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = rate.RetryAfterSeconds });
            return;
        }

        var endpoint = catalog.Find(context.ActionDescriptor, http.Request.Method);
        if (endpoint is null)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, IntegrationErrorCode.PermissionDenied,
                InboundCallOutcome.Forbidden, "endpoint_unknown", "接入应用无权访问该接口。");
            return;
        }
        state.Endpoint = endpoint;

        if (!snapshot.Permissions.Contains(endpoint.Permission, StringComparer.Ordinal))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, IntegrationErrorCode.PermissionDenied,
                InboundCallOutcome.Forbidden, "not_granted", "接入应用无权访问该接口。");
            return;
        }

        OpenAppScopeResolution resolution;
        if (string.Equals(endpoint.ScopeKey, OpenApiDataScopes.None, StringComparison.Ordinal))
        {
            resolution = new OpenAppScopeResolution(OpenAppDataScope.None, OpenAppDataScope.DenyKernelScope);
        }
        else
        {
            var policy = policies.Find(endpoint.ScopeKey);
            var binding = snapshot.Scopes.FirstOrDefault(s => string.Equals(s.ScopeKey, endpoint.ScopeKey, StringComparison.OrdinalIgnoreCase));
            if (policy is null || binding is null)
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden, IntegrationErrorCode.ScopeNotBound,
                    InboundCallOutcome.Forbidden, policy is null ? "scope_policy_missing" : "scope_not_bound",
                    "接入应用未绑定该接口声明的数据范围。");
                return;
            }
            resolution = await policy.ResolveAsync(binding, http.RequestAborted);
        }

        state.Scope = resolution.Scope;
        dataScope.Current = resolution.KernelScope;
        state.Authorized = true;
    }

    private async Task DenyAsync(
        AuthorizationFilterContext context,
        int statusCode,
        int code,
        InboundCallOutcome outcome,
        string reason,
        string message,
        IReadOnlyDictionary<string, object?>? args = null)
    {
        context.Result = new ObjectResult(Result<object>.Fail((ErrorCode)code, args, message)) { StatusCode = statusCode };
        await recorder.RecordDeniedAsync(context.HttpContext, statusCode, code, outcome, reason);
    }
}
