using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// ADR 0006:禁用已拆除 MFA 邀请/重置端点对应的菜单权限锚点。
/// 种子 SyncOnUpgrade 只更新仍在种子表中的 Id,不会删除历史邀请菜单;本服务幂等关掉那些 Permission。
/// </summary>
internal sealed class RetiredSecurityMenuCleanupHostedService(
    IServiceScopeFactory scopes,
    ILogger<RetiredSecurityMenuCleanupHostedService> logger) : IHostedService
{
    private static readonly string[] RetiredPermissions =
    [
        "POST:/api/v1/sys/mfa/invite",
        "DELETE:/api/v1/sys/mfa/invite/{id:long}",
        "POST:/api/v1/sys/mfa/reset",
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var menus = scope.ServiceProvider.GetRequiredService<IRepository<SysMenu>>();
            // 拷到局部:SqlSugar 无法翻译 private static 字段 Contains(Field "RetiredPermissions" can't be private)
            var retired = RetiredPermissions.ToArray();
            // 含软删过滤外的物理行:清 Enabled 即可,角色上残留权限码不再生效于 UI(鉴权仍按码,但端点已不存在 → 404)
            var n = await menus.Db.Updateable<SysMenu>()
                .SetColumns(m => new SysMenu { Enabled = false, Visible = false })
                .Where(m => retired.Contains(m.Permission) && (m.Enabled || m.Visible))
                .ExecuteCommandAsync();
            if (n > 0)
                logger.LogInformation(
                    "TenonAdmin: disabled {Count} retired MFA invite/reset menu permission row(s) (ADR 0006).",
                    n);
        }
        catch (Exception ex)
        {
            // 启动不因清理失败而中断
            logger.LogWarning(ex, "TenonAdmin: retired security menu cleanup skipped.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
