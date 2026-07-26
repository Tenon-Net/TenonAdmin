using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 内置 SQL 任务处理器(HandlerKind=Sql,scheduling-ledger §7.2)。属性包键:<c>sql</c>(必)。
/// <para><b>总闸默认关</b>(<c>Jobs:Sql:Enabled=false</c>):关着时存量行触发记 Failed(47008 语义);
/// <b>开启即承认:任务编辑权限 = DBA 权限</b>。只执行、不查询结果集(要报表另有正路);影响行数进记录。</para>
/// </summary>
public class SqlAdminJob(ISqlSugarClient db, AdminJobsOptions options) : IAdminJob
{
    /// <inheritdoc />
    public virtual async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!options.Sql.Enabled)
            throw new AdminException(ErrorCode.JobSqlDisabled, fallbackMessage: "SQL 任务未启用(Jobs:Sql:Enabled=false)");
        if (!context.Properties.TryGetValue("sql", out var sql) || string.IsNullOrWhiteSpace(sql))
            throw new AdminException(ErrorCode.JobPropsInvalid, new Dictionary<string, object?> { ["key"] = "sql" }, "属性包缺少必填键:sql");

        cancellationToken.ThrowIfCancellationRequested();
        // ponytail: 不传取消令牌进 Ado——SqlSugar 的命令级取消支持因方言而异;超时兜底靠任务 TimeoutSeconds 闭合记录。
        var affected = await db.Ado.ExecuteCommandAsync(sql!);
        context.Log?.Invoke($"SQL 执行完成,影响行数:{affected}");
    }
}
