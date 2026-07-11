using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ILogService"/> 默认实现。写入路径尽力而为(吞异常只告警);查询按 Id 降序返回。
/// <para>操作人/IP/UA 在<b>写入时</b>从 <see cref="ICurrentUser"/> 补全——过滤器/AuthService 只管
/// 提交自己知道的字段,"当前是谁、从哪来"统一在这里落。</para>
/// </summary>
public class LogService(
    IRepository<SysOpLog> opLogs,
    IRepository<SysLoginLog> loginLogs,
    IRepository<SysUser> users,
    ICurrentUser currentUser,
    ILogger<LogService> logger) : ILogService
{
    /// <inheritdoc />
    public virtual async Task RecordOperationAsync(OperationLogEntry entry)
    {
        try
        {
            await opLogs.InsertAsync(new SysOpLog
            {
                Title = entry.Title,
                HttpMethod = entry.HttpMethod,
                Path = entry.Path,
                ParamJson = entry.ParamJson,
                ResultCode = entry.ResultCode,
                Success = entry.ResultCode == 0,
                ExceptionMessage = entry.ExceptionMessage,
                ElapsedMs = entry.ElapsedMs,
                OperatorId = currentUser.UserId,
                Ip = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
            });
        }
        catch (Exception ex)
        {
            // ponytail: 日志写入失败只告警,绝不冒泡破坏已完成的业务请求。量大可改走 IEventBus 异步落库。
            logger.LogWarning(ex, "操作日志写入失败:{Title}", entry.Title);
        }
    }

    /// <inheritdoc />
    public virtual async Task RecordLoginAsync(LoginLogEntry entry)
    {
        try
        {
            await loginLogs.InsertAsync(new SysLoginLog
            {
                Account = entry.Account,
                Success = entry.Success,
                ResultCode = entry.ResultCode,
                UserId = entry.UserId,
                Ip = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
            });
        }
        catch (Exception ex)
        {
            // ponytail: 同上,登录日志尽力而为——写不上也不能让用户登不进来。
            logger.LogWarning(ex, "登录日志写入失败:{Account}", entry.Account);
        }
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysOpLog>> PageOperationAsync(OpLogPageInput input)
    {
        var page = await opLogs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Title), x => x.Title.Contains(input.Title!))
            .WhereIF(input.Success.HasValue, x => x.Success == input.Success!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc)   // 雪花 Id 时间有序,降序 = 最新在前
            .ToPagedListAsync(input.Current, input.Size);
        await FillOperatorNamesAsync(page.Items);
        return page;
    }

    /// <summary>
    /// 按本页出现的操作人 Id 去重批量查一次姓名回填(避免逐行 N+1)。用户已软删则查不到,OperatorName 留 null、前端回落 Id。
    /// </summary>
    protected virtual async Task FillOperatorNamesAsync(IReadOnlyList<SysOpLog> items)
    {
        var ids = items.Where(x => x.OperatorId.HasValue).Select(x => x.OperatorId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var rows = await users.AsQueryable()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToListAsync();
        var nameById = rows.ToDictionary(u => u.Id, u => u.Name);
        foreach (var item in items)
            if (item.OperatorId.HasValue && nameById.TryGetValue(item.OperatorId.Value, out var name))
                item.OperatorName = name;
    }

    /// <inheritdoc />
    public virtual Task<PagedList<SysLoginLog>> PageLoginAsync(LoginLogPageInput input) =>
        loginLogs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Account), x => x.Account.Contains(input.Account!))
            .WhereIF(input.Success.HasValue, x => x.Success == input.Success!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    // ponytail: 硬删清空(日志无软删语义,软删只会让表越堆越大)。数据量大时应改按时间分批/归档后再删。
    //           走 Db 逃生舱口而非仓储 DeleteAsync(后者是软删);Where 恒真以满足 SqlSugar 的防误删全表保护。
    public virtual Task ClearOperationAsync() =>
        opLogs.Db.Deleteable<SysOpLog>().Where(x => x.Id > 0).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task ClearLoginAsync() =>
        loginLogs.Db.Deleteable<SysLoginLog>().Where(x => x.Id > 0).ExecuteCommandAsync();
}
