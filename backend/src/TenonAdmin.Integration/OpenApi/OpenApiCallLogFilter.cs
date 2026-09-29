using Microsoft.AspNetCore.Mvc.Filters;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放端点完成出口的调用记录(资源过滤器:包住模型绑定、动作、结果执行)。授权阶段的拒绝已由
/// <see cref="OpenApiAuthorizationFilter"/> 记过,这里只处理真正进入动作的请求;未处理异常照常向上抛(先记后抛)。
/// </summary>
internal sealed class OpenApiCallLogFilter(OpenApiCallRecorder recorder) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var executed = await next();
        await recorder.RecordCompletionAsync(context.HttpContext, executed);
    }
}
