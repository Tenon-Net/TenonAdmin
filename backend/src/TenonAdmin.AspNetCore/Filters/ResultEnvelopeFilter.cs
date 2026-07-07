using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 统一返回兜底过滤器(设计 §12/§6,T8)。<b>便利/兜底</b>用途:用户业务控制器直接 <c>return dto;</c>
/// 也会被自动包成统一信封 <c>Result&lt;T&gt;</c>,不必每处手写 <c>Result.Ok(...)</c>。
/// <para>内置控制器仍显式返回 <c>Result&lt;T&gt;</c>(OpenAPI 契约保真、意图清晰),本过滤器对它们是空操作
/// (值已是信封即放行)。只包<b>成功(2xx)的裸 <see cref="ObjectResult"/></b>;File/StatusCode/错误结果一律不动。</para>
/// </summary>
// public(非 internal):判定逻辑 <see cref="TryWrap"/> 是纯函数、便于单测;用户也可能要调整它在过滤器链里的次序。
public sealed class ResultEnvelopeFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (TryWrap(context.Result, out var wrapped)) context.Result = wrapped;
        return next();
    }

    /// <summary>
    /// 纯判定(可单测):裸的成功 <see cref="ObjectResult"/>(值不是 <see cref="IResultEnvelope"/>)→ 包成
    /// <c>Result&lt;object?&gt;</c>;已是信封 / 非 ObjectResult(File、StatusCode 等)/ 非 2xx(校验错、ProblemDetails)→ 返回 false。
    /// </summary>
    public static bool TryWrap(IActionResult result, out ObjectResult wrapped)
    {
        wrapped = null!;
        if (result is not ObjectResult obj) return false;              // File/Empty/StatusCode 等不是裸值返回
        if (obj.Value is IResultEnvelope) return false;                // 内置控制器已显式包好
        if (obj.StatusCode is int sc && (sc < 200 || sc >= 300)) return false; // 错误结果(400 校验/ProblemDetails 等)不包

        // StatusCode 为 null 表示尚未协商(默认 200),视为成功,照包
        wrapped = new ObjectResult(Result<object?>.Ok(obj.Value)) { StatusCode = obj.StatusCode };
        return true;
    }
}
