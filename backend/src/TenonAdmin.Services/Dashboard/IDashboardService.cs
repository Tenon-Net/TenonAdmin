namespace TenonAdmin.Services;

/// <summary>
/// 工作台首页统计(设计 §4)。计数天然受软删与数据范围全局过滤器约束——
/// 管理员看到的用户数,就是他数据范围内的用户数。
/// </summary>
public interface IDashboardService
{
    /// <summary>首页汇总:四个计数 + 近 7 日登录趋势</summary>
    Task<DashboardSummaryOutput> SummaryAsync();
}
