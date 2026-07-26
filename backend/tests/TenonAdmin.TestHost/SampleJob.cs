using TenonAdmin.Core;

namespace TenonAdmin.TestHost;

/// <summary>
/// 示例"用户任务处理器"——放在宿主(非内置)程序集,验证消费者的 <see cref="IAdminJob"/> 能被
/// 默认解析器按 <see cref="IAdminJob.Name"/> 找到、能出现在 <c>GET /handlers</c> 清单里(scheduling-ledger §6/§12)。
/// <para>消费者的注册姿势就这一行(见 Program.cs):
/// <c>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IAdminJob, SampleJob&gt;());</c></para>
/// </summary>
public class SampleJob : ISampleJobMarker, IAdminJob
{
    /// <summary>本次执行拿到的属性包(测试断言用;进程内静态,集成测试单线程读写足够)</summary>
    public static IReadOnlyDictionary<string, string?>? LastProperties { get; private set; }

    /// <summary>执行次数</summary>
    public static int Runs { get; private set; }

    /// <inheritdoc />
    public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        LastProperties = context.Properties;
        Runs++;
        context.Log?.Invoke($"SampleJob 执行:job={context.JobCode} fire={context.FireInstanceId} retry={context.RetryIndex}");
        return Task.CompletedTask;
    }
}

/// <summary>只为让测试断言"消费者可以给自己的任务加别的接口"而存在的空标记。</summary>
public interface ISampleJobMarker;
