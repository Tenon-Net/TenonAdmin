using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 日志管理端点(设计 §4)——操作日志 / 登录日志的查询与清空。全部 <c>[RolePermission]</c>。
/// <para>清空是敏感高危动作,自身也挂 <c>[OperationLog]</c>:谁在什么时候清了日志同样留痕(清空后记录本次清空动作)。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/log")]
[Module("Log")]   // 可经 Api:DisabledModules=["Log"] 关闭
public class SysLogController(ILogService logs) : ControllerBase
{
    /// <summary>分页查询操作日志(按时间倒序)</summary>
    [HttpGet("op/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysOpLog>>> PageOperation([FromQuery] OpLogPageInput input) =>
        Result<PagedList<SysOpLog>>.Ok(await logs.PageOperationAsync(input));

    /// <summary>清空操作日志(硬删,不可恢复)</summary>
    [HttpDelete("op")]
    [RolePermission]
    [OperationLog("清空操作日志")]
    public async Task<Result<bool>> ClearOperation()
    {
        await logs.ClearOperationAsync();
        return Result<bool>.Ok(true);
    }

    /// <summary>分页查询登录日志(按时间倒序)</summary>
    [HttpGet("login/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysLoginLog>>> PageLogin([FromQuery] LoginLogPageInput input) =>
        Result<PagedList<SysLoginLog>>.Ok(await logs.PageLoginAsync(input));

    /// <summary>清空登录日志(硬删,不可恢复)</summary>
    [HttpDelete("login")]
    [RolePermission]
    [OperationLog("清空登录日志")]
    public async Task<Result<bool>> ClearLogin()
    {
        await logs.ClearLoginAsync();
        return Result<bool>.Ok(true);
    }
}
