using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 操作日志过滤器(设计 §4)——全局挂载,但<b>只对带 <see cref="OperationLogAttribute"/> 的动作</b>记录。
/// 在动作执行前快照脱敏入参与起始时刻,执行后落一条操作日志(耗时 + 业务码 + 操作人/IP/UA)。
/// <para>耗时用 <see cref="TimeProvider.GetTimestamp"/> 测(单调时钟,不受系统时间回拨影响)。</para>
/// </summary>
internal sealed class OperationLogFilter(ILogService logService, TimeProvider timeProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var marker = context.ActionDescriptor.EndpointMetadata.OfType<OperationLogAttribute>().FirstOrDefault();
        if (marker is null)
        {
            await next();   // 未标注:直通,不产生日志
            return;
        }

        // 入参必须在动作执行“前”快照——动作可能改写参数对象;脱敏后再留存
        var paramJson = SensitiveDataMasker.Mask(context.ActionArguments);
        var start = timeProvider.GetTimestamp();

        var executed = await next();

        await logService.RecordOperationAsync(new OperationLogEntry
        {
            Title = marker.Title,
            HttpMethod = context.HttpContext.Request.Method,
            Path = context.HttpContext.Request.Path,
            ParamJson = paramJson,
            ResultCode = ResolveResultCode(executed),
            ExceptionMessage = Truncate(executed.Exception?.Message),
            ElapsedMs = (long)timeProvider.GetElapsedTime(start).TotalMilliseconds,
        });   // 尽力而为:RecordOperationAsync 内部吞异常,不会影响已经产出的响应
    }

    /// <summary>异常消息截断上限(防个别超长消息把日志表撑大)。</summary>
    private static string? Truncate(string? msg) =>
        msg is null || msg.Length <= 2000 ? msg : msg[..2000];

    /// <summary>
    /// 解析业务结果码:优先看异常(业务异常记其码、其他异常记系统错误码),
    /// 正常返回则读统一信封 <see cref="IResultEnvelope"/> 的 Code(免反射);非信封返回视为成功。
    /// </summary>
    private static int ResolveResultCode(ActionExecutedContext executed)
    {
        if (executed.Exception is AdminException admin) return (int)admin.Code;
        if (executed.Exception is not null) return (int)ErrorCode.SystemError;
        if (executed.Result is ObjectResult { Value: IResultEnvelope envelope }) return envelope.Code;
        return 0;
    }
}
