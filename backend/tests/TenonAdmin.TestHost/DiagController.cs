using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.TestHost;

/// <summary>
/// 测试专用诊断控制器——故意抛异常,供异常日志过滤器(<c>ExceptionLogFilter</c>)的集成用例验证:
/// 未捕获异常落一条 <c>sys_exception_log</c> 且 500 照旧;业务异常(<c>AdminException</c>)不落表且照返信封。
/// <para><c>[ActiveSession]</c>:任一登录用户可打(无需具体权限码),让异常携带触发人身份以验证操作人回填。</para>
/// </summary>
[ApiController]
[Route("api/v1/diag")]
public class DiagController : ControllerBase
{
    /// <summary>故意抛未捕获异常(程序缺陷)→ 应产生异常日志 + 500。</summary>
    [HttpGet("throw")]
    [ActiveSession]
    public IActionResult Throw() => throw new InvalidOperationException("boom-diag");

    /// <summary>故意抛业务异常 → 应转信封(200 + 业务码),且不进异常日志表。</summary>
    [HttpGet("throw-business")]
    [ActiveSession]
    public IActionResult ThrowBusiness() => throw new AdminException(ErrorCode.PasswordWrong);
}
