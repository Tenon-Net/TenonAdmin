using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 异常日志过滤器(设计 §4)——全局挂载,把<b>未捕获异常</b>(程序缺陷)落一条 <c>sys_exception_log</c> 供排障,
/// 但<b>不吞异常</b>:不设 <see cref="ExceptionContext.ExceptionHandled"/>,让框架默认 500 流程照旧带完整堆栈处理
/// (程序缺陷该大声失败,§13.2)。本过滤器只"旁路留痕",不改变响应。
/// <para><see cref="AdminException"/> 业务异常显式跳过——那是可预期的业务分支(由 <see cref="AdminExceptionFilter"/>
/// 转信封、由操作日志的 ResultCode 表达),不是崩溃,不进异常表。判类型跳过而非依赖过滤器执行顺序,顺序无关正确性。</para>
/// <para>写入尽力而为(<see cref="ILogService.RecordExceptionAsync"/> 内部吞异常):在"处理崩溃"的路径上,
/// 落日志再失败也绝不能盖掉原始异常。</para>
/// </summary>
internal sealed class ExceptionLogFilter(ILogService logService) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is AdminException) return;   // 业务异常不进异常表

        await logService.RecordExceptionAsync(new ExceptionLogEntry
        {
            HttpMethod = context.HttpContext.Request.Method,
            Path = context.HttpContext.Request.Path,
            TraceId = context.HttpContext.TraceIdentifier,
            ExceptionType = Truncate(context.Exception.GetType().FullName ?? context.Exception.GetType().Name, 256)!,
            Message = Truncate(context.Exception.Message, 2000),
            StackTrace = Truncate(context.Exception.StackTrace, 8000),
        });
        // 刻意不设 ExceptionHandled —— 异常继续冒泡走框架 500,响应与堆栈行为不变。
    }

    /// <summary>字段级截断(防个别超长消息/堆栈把日志表撑大)。</summary>
    private static string? Truncate(string? s, int max) =>
        s is null || s.Length <= max ? s : s[..max];
}
