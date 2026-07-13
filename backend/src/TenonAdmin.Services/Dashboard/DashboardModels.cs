namespace TenonAdmin.Services;

/// <summary>
/// 工作台首页统计(设计 §4)。形状直接对齐前端图表组件:
/// 折线图收 <c>categories: string[]</c> + 两条 <c>number[]</c>,饼图的"资源分布"就是前三个计数,前端自行拼装。
/// </summary>
/// <param name="Roles">角色数</param>
/// <param name="Users">用户数</param>
/// <param name="Perms">权限点数(菜单里带权限码的节点:目录无权限码)</param>
/// <param name="OnlineSessions">在线会话数(未吊销且未过期)</param>
/// <param name="TrendDays">近 7 日日期(MM-dd,含今天)</param>
/// <param name="TrendLogins">近 7 日每天登录成功次数</param>
/// <param name="TrendActiveUsers">近 7 日每天登录成功的去重用户数</param>
public record DashboardSummaryOutput(
    int Roles,
    int Users,
    int Perms,
    int OnlineSessions,
    List<string> TrendDays,
    List<int> TrendLogins,
    List<int> TrendActiveUsers);
