using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IDashboardService"/> 默认实现。直接吃泛型仓储(不经各模块 Service:这里只要计数,
/// 走 Service 反而要为每个模块开一个"给我个总数"的口子)。软删 + 数据范围全局过滤器自动生效。
/// <para>每一步拆成独立 <c>virtual</c> 方法,消费者想换掉某一个数字(比如"待办"改成自己的工单表),
/// 覆写一个方法即可,不必抄整个 <see cref="SummaryAsync"/>。</para>
/// </summary>
public class DashboardService(
    IRepository<SysRole> roles,
    IRepository<SysUser> users,
    IRepository<SysMenu> menus,
    IRepository<SysSession> sessions,
    IRepository<SysLoginLog> loginLogs,
    TimeProvider time) : IDashboardService
{
    /// <summary>趋势天数(含今天)</summary>
    protected const int TrendDayCount = 7;

    /// <inheritdoc />
    public virtual async Task<DashboardSummaryOutput> SummaryAsync()
    {
        var (days, logins, activeUsers) = await TrendAsync();
        return new DashboardSummaryOutput(
            await RoleCountAsync(),
            await UserCountAsync(),
            await PermCountAsync(),
            await OnlineSessionCountAsync(),
            days, logins, activeUsers);
    }

    /// <summary>角色数</summary>
    protected virtual Task<int> RoleCountAsync() => roles.AsQueryable().CountAsync();

    /// <summary>用户数</summary>
    protected virtual Task<int> UserCountAsync() => users.AsQueryable().CountAsync();

    /// <summary>权限点数 = 带权限码的菜单节点(权限码即路由;目录无权限码,故天然被排除)</summary>
    protected virtual Task<int> PermCountAsync() =>
        menus.AsQueryable().Where(m => m.Permission != null && m.Permission != "").CountAsync();

    /// <summary>在线会话数。口径与会话管理页(<see cref="SessionService.ListOnlineAsync"/>)一致:未吊销且未过期。</summary>
    protected virtual Task<int> OnlineSessionCountAsync()
    {
        var now = time.GetUtcNow().UtcDateTime;   // 会话的 ExpiresAt 写的是 UTC(见 SessionService)
        return sessions.AsQueryable().Where(s => s.RevokedAt == null && s.ExpiresAt > now).CountAsync();
    }

    /// <summary>近 7 日登录趋势:每天的登录成功次数与去重活跃用户数(无登录的日子补 0)。</summary>
    protected virtual async Task<(List<string> Days, List<int> Logins, List<int> ActiveUsers)> TrendAsync()
    {
        // 审计字段 CreateTime 由 AOP 写的是**本地时间**(SqlSugarSetup 用 GetLocalNow),所以按本地日历分天。
        var today = time.GetLocalNow().Date;
        var from = today.AddDays(-(TrendDayCount - 1));

        // ponytail: 把 7 天的行拉到内存里分组,不在 SQL 端按天 GROUP BY —— 按天分组要用日期函数,
        // 而 SQLite/MySQL/SqlServer/PostgreSQL 四种库写法各不相同,内核四种都要跑得通,不值得为一张首页图写四份方言。
        // 上限:一次拉 7 天的登录成功行。日志量大到这个窗口都扛不住时,再把分组下推到 SQL(那时也该按库分方言了)。
        var rows = await loginLogs.AsQueryable()
            .Where(l => l.Success && l.CreateTime >= from && l.CreateTime < today.AddDays(1))
            .Select(l => new { l.CreateTime, l.UserId })
            .ToListAsync();

        var byDay = rows.GroupBy(r => r.CreateTime.Date).ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<string>(TrendDayCount);
        var logins = new List<int>(TrendDayCount);
        var actives = new List<int>(TrendDayCount);
        for (var i = 0; i < TrendDayCount; i++)
        {
            var day = from.AddDays(i);
            byDay.TryGetValue(day, out var hits);
            days.Add(day.ToString("MM-dd"));
            logins.Add(hits?.Count ?? 0);
            // 活跃用户 = 当天登录成功的去重用户。UserId 仅在成功时有值,这里已只取成功行。
            actives.Add(hits?.Where(h => h.UserId != null).Select(h => h.UserId!.Value).Distinct().Count() ?? 0);
        }
        return (days, logins, actives);
    }
}
