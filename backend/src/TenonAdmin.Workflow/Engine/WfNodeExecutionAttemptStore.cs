using System.Security.Cryptography;
using System.Text;
using SqlSugar;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_node_execution_attempt</c> 的 append-only 写入(M3a-1 Task 4)。<b>只暴露 <see cref="AppendAsync"/>
/// 一个方法,不提供 Update/Delete</b>——与唯一索引 <c>uk_wf_node_exec_attempt_no</c> 一硬一软两道保证 append-only。
/// <c>public</c> 而不是 <c>internal</c>——全仓无 <c>InternalsVisibleTo</c>,与 <see cref="WfNodeExecutionStore"/>
/// 同为 <c>public static</c>。<b>零 DI 注册</b>,调用方(Task 6 的 dispatcher)直接经 <c>ISqlSugarClient</c> 调用,
/// 事务由调用方起。
/// <para><b>签名里没有 <c>attemptNo</c> 形参是刻意的</b>——差一只可能来自「调用方自己算 attempt 号」,拿掉这个
/// 入口就拿掉了这一类 bug;<see cref="WfNodeExecutionAttempt.AttemptNo"/> 与
/// <see cref="WfNodeExecutionAttempt.ExecutionId"/> 取自<b>同一个 <c>execution</c> 对象</b>
/// (见 <see cref="AppendAsync"/> 方法体),也就杜绝了「A 的 Id 配 B 的 count」这类错配。</para>
/// <para><b>本方法不碰 <c>wf_node_execution</c> 的任何列</b>:结果回写(<c>Status</c>/<c>NextRetryAtUtc</c>/
/// <c>CompletedTimeUtc</c>/…)归 <b>Task 6</b> 的 fence CAS 短事务(<c>WHERE Fence == @myFence</c>),两者将在
/// 同一个短事务里提交(§4.6「结果、变量、历史和 outbox 在同一短事务提交」),但代码归属不同——本类只负责
/// attempt 那一行。</para>
/// <para><b>唯一索引撞了原样抛出,不写 try/catch</b>——与 <see cref="WfNodeExecutionStore.EnsureAsync"/> 同款
/// 理由:半吊子的 catch 在 PostgreSQL 上更糟(事务已 aborted,<c>25P02</c>)。撞唯一键意味着「同一 attempt 号
/// 写了两次」,那是调用方的 bug,必须炸出来。</para>
/// <para><b>摘要截断在 C# 侧</b>,四库一致(SqlServer/PostgreSQL 超长直接抛、MySQL 非严格模式静默截断、SQLite
/// 照单全收);handler 的摘要是外部输入,截断是 trust boundary 上的必要防护,不是可省的优化。</para>
/// </summary>
public static class WfNodeExecutionAttemptStore
{
    /// <summary>摘要列长度上限,与实体两个 512 列一致。</summary>
    public const int SummaryMaxLength = 512;

    /// <summary>
    /// 插入一行 attempt 记录并返回。<paramref name="execution"/> 必须是<b>领取读回后</b>的行(
    /// <see cref="WfNodeExecutionStore.ClaimAsync"/> 的返回值)——<see cref="WfNodeExecutionAttempt.AttemptNo"/>
    /// 直接取其 <see cref="WfNodeExecution.AttemptCount"/>,绝不 +1。
    /// </summary>
    public static async Task<WfNodeExecutionAttempt> AppendAsync(
        ISqlSugarClient db,
        WfNodeExecution execution,
        WfNodeExecutionResult result,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var succeeded = result.Type == WfNodeExecutionResultType.Succeeded;
        var row = new WfNodeExecutionAttempt
        {
            ExecutionId = execution.Id,
            AttemptNo = execution.AttemptCount, // 直接取,绝不 +1
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            ResultType = result.Type,
            OutputSummary = succeeded ? Truncate(result.Summary) : null,
            OutputHash = result.OutputJson is null
                ? null
                : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.OutputJson))),
            ErrorCode = succeeded ? null : result.ErrorCode,
            ErrorSummary = succeeded ? null : Truncate(result.Summary),
        };

        await db.Insertable(row).ExecuteCommandAsync(); // Id 由审计 AOP 填雪花
        return row;
    }

    /// <summary>
    /// 摘要截断到 <see cref="SummaryMaxLength"/>。<c>public</c> 供 Task 6 的
    /// <c>wf_node_execution.Summary</c>(同宽同规则)复用——截断规则不许在两处各写一遍(Task 5 P3-1 的教训)。
    /// </summary>
    public static string? Truncate(string? value) =>
        value is null || value.Length <= SummaryMaxLength ? value : value[..SummaryMaxLength];
}
