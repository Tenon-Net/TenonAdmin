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
    IRepository<SysExceptionLog> exceptionLogs,
    IRepository<SysUser> users,
    ICurrentUser currentUser,
    ILogger<LogService> logger,
    // 导出行数上限(excel-ledger §6.1);可选尾参,未注入时用默认 50000
    AdminExcelOptions? excel = null) : ILogService
{
    private AdminExcelOptions Excel => excel ?? new AdminExcelOptions();

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
    public virtual async Task RecordExceptionAsync(ExceptionLogEntry entry)
    {
        try
        {
            await exceptionLogs.InsertAsync(new SysExceptionLog
            {
                HttpMethod = entry.HttpMethod,
                Path = entry.Path,
                TraceId = entry.TraceId,
                ExceptionType = entry.ExceptionType,
                Message = entry.Message,
                StackTrace = entry.StackTrace,
                OperatorId = currentUser.UserId,
                Ip = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
            });
        }
        catch (Exception ex)
        {
            // ponytail: 同操作/登录日志——异常日志写入失败只告警,绝不在"处理崩溃"的路径上再抛一次盖掉原始异常。
            logger.LogWarning(ex, "异常日志写入失败:{Path}", entry.Path);
        }
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysOpLog>> PageOperationAsync(OpLogPageInput input)
    {
        var page = await BuildOpListQuery(input).ToPagedListAsync(input.Current, input.Size);
        await FillOperatorNamesAsync(page.Items);
        return page;
    }

    /// <inheritdoc />
    // 坑 1:不得走 PageOperationAsync/ToPagedListAsync——MAX_SIZE=200 会静默截断导出。
    public virtual async Task<IReadOnlyList<SysOpLog>> ExportOpLogsAsync(OpLogPageInput input)
    {
        var max = Excel.MaxExportRows;
        var items = await BuildOpListQuery(input).Take(max + 1).ToListAsync();
        AdminException.ThrowIf(items.Count > max, ErrorCode.ExportRowLimitExceeded);
        await FillOperatorNamesAsync(items);
        return items;
    }

    /// <summary>
    /// 操作日志列表/导出共用查询骨架。与 <see cref="PageOperationAsync"/> 共用,避免过滤条件漂移。
    /// </summary>
    protected virtual ISugarQueryable<SysOpLog> BuildOpListQuery(OpLogPageInput input) =>
        opLogs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Title), x => x.Title.Contains(input.Title!))
            .WhereIF(input.Success.HasValue, x => x.Success == input.Success!.Value)
            .WhereIF(input.OperatorId.HasValue, x => x.OperatorId == input.OperatorId!.Value)
            .WhereIF(!string.IsNullOrEmpty(input.Path), x => x.Path.Contains(input.Path!))
            .WhereIF(input.StartTime.HasValue, x => x.CreateTime >= input.StartTime!.Value)
            .WhereIF(input.EndTime.HasValue, x => x.CreateTime <= input.EndTime!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc);   // 雪花 Id 时间有序,降序 = 最新在前

    /// <summary>
    /// 按本页出现的操作人 Id 去重批量查一次姓名回填(避免逐行 N+1)。
    /// <para><b>必须 ClearFilter&lt;ISoftDelete&gt;</b>:用户是软删的,不清过滤器则"离职那个人走之前删了什么"这类审计查询里,
    /// 操作人一栏会赫然是一串雪花 Id——恰恰是最需要看清是谁的时候看不见。日志是历史事实,不随人员在职状态消失。
    /// 只取 Id+Name 两列,不泄漏其他字段(SysUser 继承 BaseEntity 非 IOrgScoped,不涉数据范围)。</para>
    /// </summary>
    protected virtual async Task FillOperatorNamesAsync(IReadOnlyList<SysOpLog> items)
    {
        var ids = items.Where(x => x.OperatorId.HasValue).Select(x => x.OperatorId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var rows = await users.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToListAsync();
        var nameById = rows.ToDictionary(u => u.Id, u => u.Name);
        foreach (var item in items)
            if (item.OperatorId.HasValue && nameById.TryGetValue(item.OperatorId.Value, out var name))
                item.OperatorName = name;
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysLoginLog>> PageLoginAsync(LoginLogPageInput input)
    {
        var page = await loginLogs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Account), x => x.Account.Contains(input.Account!))
            .WhereIF(input.Success.HasValue, x => x.Success == input.Success!.Value)
            .WhereIF(input.StartTime.HasValue, x => x.CreateTime >= input.StartTime!.Value)
            .WhereIF(input.EndTime.HasValue, x => x.CreateTime <= input.EndTime!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);
        await FillUserNamesAsync(page.Items);
        return page;
    }

    /// <summary>
    /// 按本页出现的用户 Id 去重批量查一次姓名回填(避免逐行 N+1)。登录失败行 UserId 为 null → 跳过;
    /// 同 <see cref="FillOperatorNamesAsync"/>,清软删过滤器,已离职用户的历史登录记录仍显示姓名。
    /// </summary>
    protected virtual async Task FillUserNamesAsync(IReadOnlyList<SysLoginLog> items)
    {
        var ids = items.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var rows = await users.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToListAsync();
        var nameById = rows.ToDictionary(u => u.Id, u => u.Name);
        foreach (var item in items)
            if (item.UserId.HasValue && nameById.TryGetValue(item.UserId.Value, out var name))
                item.Name = name;
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<SysExceptionLog>> PageExceptionAsync(ExceptionLogPageInput input)
    {
        var page = await exceptionLogs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Path), x => x.Path.Contains(input.Path!))
            .WhereIF(!string.IsNullOrEmpty(input.ExceptionType), x => x.ExceptionType.Contains(input.ExceptionType!))
            .WhereIF(input.StartTime.HasValue, x => x.CreateTime >= input.StartTime!.Value)
            .WhereIF(input.EndTime.HasValue, x => x.CreateTime <= input.EndTime!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Current, input.Size);
        await FillExceptionOperatorNamesAsync(page.Items);
        return page;
    }

    /// <summary>
    /// 按本页出现的触发人 Id 去重批量查一次姓名回填(避免逐行 N+1)。匿名端点异常 OperatorId 为 null → 跳过;
    /// 同 <see cref="FillOperatorNamesAsync"/>,清软删过滤器,离职用户触发的历史异常仍显示姓名。
    /// </summary>
    protected virtual async Task FillExceptionOperatorNamesAsync(IReadOnlyList<SysExceptionLog> items)
    {
        var ids = items.Where(x => x.OperatorId.HasValue).Select(x => x.OperatorId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var rows = await users.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToListAsync();
        var nameById = rows.ToDictionary(u => u.Id, u => u.Name);
        foreach (var item in items)
            if (item.OperatorId.HasValue && nameById.TryGetValue(item.OperatorId.Value, out var name))
                item.OperatorName = name;
    }

    /// <inheritdoc />
    // ponytail: 硬删清空(日志无软删语义,软删只会让表越堆越大)。数据量大时应改按时间分批/归档后再删。
    //           走 Db 逃生舱口而非仓储 DeleteAsync(后者是软删);Where 恒真以满足 SqlSugar 的防误删全表保护。
    public virtual Task ClearOperationAsync() =>
        opLogs.Db.Deleteable<SysOpLog>().Where(x => x.Id > 0).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task ClearLoginAsync() =>
        loginLogs.Db.Deleteable<SysLoginLog>().Where(x => x.Id > 0).ExecuteCommandAsync();

    /// <inheritdoc />
    public virtual Task ClearExceptionAsync() =>
        exceptionLogs.Db.Deleteable<SysExceptionLog>().Where(x => x.Id > 0).ExecuteCommandAsync();
}
