using Microsoft.AspNetCore.Http;

namespace TenonAdmin.Integration;

/// <summary>
/// <see cref="IOpenAppContext"/> 默认实现:读取授权过滤器写入 <c>HttpContext.Items</c> 的请求状态。
/// 只有授权全部通过的请求才被视为开放请求;其他请求访问身份或范围直接抛出,避免业务代码在错误上下文里拿到空值继续执行。
/// </summary>
public sealed class HttpContextOpenAppContext(IHttpContextAccessor accessor) : IOpenAppContext
{
    private OpenApiRequestState? State
    {
        get
        {
            var state = OpenApiRequestState.Peek(accessor.HttpContext);
            return state is { Authorized: true } ? state : null;
        }
    }

    /// <inheritdoc />
    public bool IsOpenAppRequest => State is not null;

    /// <inheritdoc />
    public OpenAppIdentity Identity => State?.Identity ?? throw NotOpenRequest();

    /// <inheritdoc />
    public OpenAppDataScope DataScope => State?.Scope ?? throw NotOpenRequest();

    /// <inheritdoc />
    public OpenApiEndpointInfo Endpoint => State?.Endpoint ?? throw NotOpenRequest();

    private static InvalidOperationException NotOpenRequest() =>
        new("当前请求不是已授权的开放接口请求:只能在带 [OpenApi] 的端点及其调用链里读取接入应用上下文。");
}
