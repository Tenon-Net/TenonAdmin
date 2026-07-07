using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例"用户自定义控制器",占用内置字典模块的路由。集成测试里内置 Dict 模块被禁用(Api:DisabledModules=["Dict"]),
/// 本控制器即接管该路由(§8 CustomController 用例)。裸返回 dto,由兜底过滤器包信封。
/// </summary>
[ApiController]
[Route("api/v1/sys/dict")]
[AllowAnonymous]
public class CustomDictController : ControllerBase
{
    [HttpGet("type/page")]
    public object TypePage() => new { source = "custom-dict" };
}
