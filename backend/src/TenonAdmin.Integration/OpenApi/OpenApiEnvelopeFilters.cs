using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放端点的未处理异常出口:转成 500 + 统一信封(<see cref="ErrorCode.SystemError"/>,带追踪标识),不向第三方返回堆栈或 HTML。
/// <para><b>Order = int.MinValue</b>:异常过滤器按「展开」顺序执行(排序靠后的先跑),最小 Order 保证它最后执行——
/// 内核的异常留痕(<c>ExceptionLogFilter</c>)与业务异常信封(<c>AdminExceptionFilter</c>)先照常处理,这里只接住剩下的程序缺陷。</para>
/// </summary>
internal sealed class OpenApiExceptionFilter(ILogger<OpenApiExceptionFilter> logger, TimeProvider time) : IAsyncExceptionFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.ExceptionHandled || context.Exception is AdminException) return Task.CompletedTask;

        var state = OpenApiRequestState.GetOrCreate(context.HttpContext, time);
        state.ExceptionType = context.Exception.GetType().Name;
        logger.LogError(context.Exception, "开放接口未处理异常。Path={Path} TraceId={TraceId}",
            context.HttpContext.Request.Path.Value, state.TraceId);

        context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.SystemError,
            new Dictionary<string, object?> { ["traceId"] = state.TraceId }, "服务内部错误,请携带追踪标识联系管理员。"))
        {
            StatusCode = StatusCodes.Status500InternalServerError,
        };
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 开放端点的框架级 4xx(模型校验失败等 <see cref="ProblemDetails"/>)改写为统一信封:
/// 状态码不变,业务码 <see cref="IntegrationErrorCode.RequestInvalid"/>,<c>args.errors</c> 带字段错误。
/// 第三方调用方因此只需处理一种错误形状。
/// </summary>
internal sealed class OpenApiProblemEnvelopeFilter : IAsyncAlwaysRunResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem } result)
        {
            var status = result.StatusCode ?? problem.Status ?? StatusCodes.Status400BadRequest;
            var args = new Dictionary<string, object?> { ["status"] = status };
            if (problem is ValidationProblemDetails validation)
                args["errors"] = validation.Errors;
            context.Result = new ObjectResult(Result<object>.Fail((ErrorCode)IntegrationErrorCode.RequestInvalid, args, "请求参数不合法。"))
            {
                StatusCode = status,
            };
        }
        return next();
    }
}
