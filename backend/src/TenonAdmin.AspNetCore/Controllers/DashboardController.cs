using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 工作台首页统计(设计 §4)。
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Module("Dashboard")]   // 可经 Api:DisabledModules=["Dashboard"] 关闭
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <summary>首页汇总:角色/用户/权限点/在线会话数 + 近 7 日登录趋势</summary>
    // [ActiveSession] 而非 [RolePermission]:工作台是每个应用的默认首页,人人都会落在这儿。
    // 若要配权,新建的角色只要没勾这条路由,首页就是一片空白——为一张统计图给全体角色配权,不值当。
    // 数据本身不越权:计数走仓储,软删与数据范围全局过滤器照常生效。
    [HttpGet("summary")]
    [ActiveSession]
    public async Task<Result<DashboardSummaryOutput>> Summary() =>
        Result<DashboardSummaryOutput>.Ok(await dashboard.SummaryAsync());
}
