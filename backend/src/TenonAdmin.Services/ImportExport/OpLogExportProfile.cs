using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 操作日志导出档案(excel-ledger §9 G3)。取数走 <see cref="ILogService.ExportOpLogsAsync"/>,
/// 与分页查询共用过滤构造,避免导出范围与列表漂移。
/// </summary>
public class OpLogExportProfile : IExportProfile
{
    /// <inheritdoc />
    public virtual string Code => "sys-op-log";

    /// <inheritdoc />
    public virtual IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new() { Key = "Title", Title = "操作名", Width = 18 },
        new() { Key = "HttpMethod", Title = "方法", Width = 10 },
        new() { Key = "Path", Title = "路径", Width = 32 },
        new() { Key = "ResultCode", Title = "结果码", Width = 10 },
        new() { Key = "Success", Title = "成功", Width = 8 },
        new() { Key = "OperatorName", Title = "操作人", Width = 14 },
        new() { Key = "Ip", Title = "IP", Width = 16 },
        new() { Key = "ElapsedMs", Title = "耗时(ms)", Width = 12 },
        new() { Key = "CreateTime", Title = "时间", Width = 20 },
        new() { Key = "ExceptionMessage", Title = "异常信息", DefaultSelected = false, Width = 28 },
    ];
}
