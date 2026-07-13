using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 操作日志过滤器(设计 §4)——全局挂载,<b>默认记录一切写操作</b>(安全默认:审计不能靠人记得加特性)。
/// 在动作执行前快照脱敏入参与起始时刻,执行后落一条操作日志(耗时 + 业务码 + 操作人/IP/UA)。
/// <para>耗时用 <see cref="TimeProvider.GetTimestamp"/> 测(单调时钟,不受系统时间回拨影响)。</para>
/// <para>历史:本过滤器原为 opt-in(只记挂了 <see cref="OperationLogAttribute"/> 的动作),结果是
/// 角色授权/数据范围/系统配置/停用用户/强制下线这些<b>最需要审计的动作全部无痕</b>,而分片上传每片都留痕
/// ——审计能力形同虚设,且消费方自建的控制器默认也不留痕。现改为 opt-out:见 <see cref="ShouldLog"/>。</para>
/// </summary>
internal sealed class OperationLogFilter(ILogService logService, TimeProvider timeProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var marker = context.ActionDescriptor.EndpointMetadata.OfType<OperationLogAttribute>().FirstOrDefault();
        if (!ShouldLog(context, marker))
        {
            await next();   // 读操作/匿名端点:直通,不产生日志
            return;
        }

        // 入参必须在动作执行“前”快照——动作可能改写参数对象;脱敏后再留存
        var paramJson = SensitiveDataMasker.Mask(context.ActionArguments);
        var start = timeProvider.GetTimestamp();

        var executed = await next();

        await logService.RecordOperationAsync(new OperationLogEntry
        {
            // 未标注时用规范化路由串兜底(= 权限码),日志"操作名"与角色授权页勾的那一条天然对齐;
            // [OperationLog("新增用户")] 退化为可选的人类可读标题覆写。
            Title = marker?.Title ?? PermissionCode.Build(
                context.HttpContext.Request.Method,
                context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.HttpContext.Request.Path.Value),
            HttpMethod = context.HttpContext.Request.Method,
            Path = context.HttpContext.Request.Path,
            ParamJson = paramJson,
            ResultCode = ResolveResultCode(executed),
            ExceptionMessage = Truncate(executed.Exception?.Message),
            ElapsedMs = (long)timeProvider.GetElapsedTime(start).TotalMilliseconds,
        });   // 尽力而为:RecordOperationAsync 内部吞异常,不会影响已经产出的响应
    }

    /// <summary>
    /// 是否记录本次动作。安全默认:<b>写操作一律记</b>,只放行两类——
    /// <list type="bullet">
    /// <item>读操作(GET/HEAD/OPTIONS):无审计价值,且列表分页会瞬间把日志表刷爆。
    ///   确需留痕的读(如导出)显式挂 <see cref="OperationLogAttribute"/> 即可,标注优先于本规则。</item>
    /// <item>匿名端点(登录/刷新/验证码):已有登录日志专门留痕,入参还含明文口令,不重复记。</item>
    /// </list>
    /// </summary>
    private static bool ShouldLog(ActionExecutingContext context, OperationLogAttribute? marker)
    {
        if (marker is not null) return true;   // 显式标注:一律记(含极少数需留痕的 GET)

        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return false;

        return !context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
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
