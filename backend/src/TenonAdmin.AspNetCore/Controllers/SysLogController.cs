using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 日志管理端点(设计 §4)——操作日志 / 登录日志的查询与清空。全部 <c>[RolePermission]</c>。
/// <para>清空是敏感高危动作,自身也挂 <c>[OperationLog]</c>:谁在什么时候清了日志同样留痕(清空后记录本次清空动作)。</para>
/// <para>导出(excel-ledger §5.1):GET + 显式 <c>[OperationLog]</c>(读默认不记);返回 xlsx 流,不进信封(§5.2)。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/log")]
[Module("Log")]   // 可经 Api:DisabledModules=["Log"] 关闭
public class SysLogController(
    ILogService logs,
    OpLogExportProfile opLogExportProfile,
    IExcelWriter writer) : ControllerBase
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>分页查询操作日志(按时间倒序)</summary>
    [HttpGet("op/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysOpLog>>> PageOperation([FromQuery] OpLogPageInput input) =>
        Result<PagedList<SysOpLog>>.Ok(await logs.PageOperationAsync(input));

    /// <summary>
    /// 导出操作日志 xlsx。query 复用 <see cref="OpLogPageInput"/> + <c>Columns</c>(逗号分隔列 Key,缺省=DefaultSelected)。
    /// 返回文件流,不进信封(§5.2);显式挂操作日志(§7)。
    /// </summary>
    [HttpGet("op/export")]
    [RolePermission]
    [OperationLog("导出操作日志")]
    public async Task<IActionResult> ExportOperation(
        [FromQuery] OpLogPageInput input,
        [FromQuery] string? columns,
        CancellationToken cancellationToken)
    {
        var selected = UserController.ResolveExportColumns(opLogExportProfile, columns);
        var items = await logs.ExportOpLogsAsync(input);

        var rows = new List<IReadOnlyDictionary<string, object?>>(items.Count);
        foreach (var log in items)
        {
            var cells = new Dictionary<string, object?>();
            foreach (var col in selected)
                cells[col.Key] = GetOpLogCell(log, col.Key);
            rows.Add(cells);
        }

        var stream = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "操作日志",
            Columns = selected,
            Rows = rows,
        }, cancellationToken);

        return File(stream, XlsxContentType, "操作日志导出.xlsx");
    }

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

    /// <summary>分页查询异常日志(按时间倒序)</summary>
    [HttpGet("exception/page")]
    [RolePermission]
    public async Task<Result<PagedList<SysExceptionLog>>> PageException([FromQuery] ExceptionLogPageInput input) =>
        Result<PagedList<SysExceptionLog>>.Ok(await logs.PageExceptionAsync(input));

    /// <summary>清空异常日志(硬删,不可恢复)</summary>
    [HttpDelete("exception")]
    [RolePermission]
    [OperationLog("清空异常日志")]
    public async Task<Result<bool>> ClearException()
    {
        await logs.ClearExceptionAsync();
        return Result<bool>.Ok(true);
    }

    private static object? GetOpLogCell(SysOpLog log, string key) => key switch
    {
        "Title" => log.Title,
        "HttpMethod" => log.HttpMethod,
        "Path" => log.Path,
        "ResultCode" => log.ResultCode,
        "Success" => log.Success ? "成功" : "失败",
        "OperatorName" => log.OperatorName,
        "Ip" => log.Ip,
        "ElapsedMs" => log.ElapsedMs,
        "CreateTime" => log.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
        "ExceptionMessage" => log.ExceptionMessage,
        _ => null,
    };
}
