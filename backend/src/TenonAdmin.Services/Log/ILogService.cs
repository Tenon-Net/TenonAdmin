using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 日志服务(设计 §4)——操作日志 + 登录日志的写入与查询。
/// <para>两个 <c>Record*Async</c> 是<b>尽力而为</b>:内部吞掉自身异常(只告警),
/// 绝不因为"日志没写上"而破坏正常的业务请求或登录流程。</para>
/// </summary>
public interface ILogService
{
    /// <summary>写一条操作日志(由操作日志过滤器调用)。失败只告警,不抛。</summary>
    Task RecordOperationAsync(OperationLogEntry entry);

    /// <summary>写一条登录日志(由 <c>AuthService</c> 成功/失败路径调用)。失败只告警,不抛。</summary>
    Task RecordLoginAsync(LoginLogEntry entry);

    /// <summary>写一条异常日志(由 <c>ExceptionLogFilter</c> 在未捕获异常冒泡时调用)。失败只告警,不抛。</summary>
    Task RecordExceptionAsync(ExceptionLogEntry entry);

    /// <summary>分页查询操作日志(按时间倒序)。</summary>
    Task<PagedList<SysOpLog>> PageOperationAsync(OpLogPageInput input);

    /// <summary>
    /// 导出用操作日志全量列表(与 <see cref="PageOperationAsync"/> 共用过滤)。
    /// 不走分页 <c>MAX_SIZE=200</c> 截断;超过 <c>TenonAdmin:Excel:MaxExportRows</c> 抛
    /// <see cref="ErrorCode.ExportRowLimitExceeded"/>。
    /// </summary>
    Task<IReadOnlyList<SysOpLog>> ExportOpLogsAsync(OpLogPageInput input);

    /// <summary>分页查询登录日志(按时间倒序)。</summary>
    Task<PagedList<SysLoginLog>> PageLoginAsync(LoginLogPageInput input);

    /// <summary>分页查询异常日志(按时间倒序)。</summary>
    Task<PagedList<SysExceptionLog>> PageExceptionAsync(ExceptionLogPageInput input);

    /// <summary>清空全部操作日志(硬删)。</summary>
    Task ClearOperationAsync();

    /// <summary>清空全部登录日志(硬删)。</summary>
    Task ClearLoginAsync();

    /// <summary>清空全部异常日志(硬删)。</summary>
    Task ClearExceptionAsync();
}
