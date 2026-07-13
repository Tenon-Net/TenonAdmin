using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 磁盘回收后台任务(dev-plan T-D2)。上传子系统有两处只涨不消的地方,都在这里收:
/// <list type="number">
///   <item><b>软删的文件</b>——<see cref="FileService.DeleteAsync"/> 只置删除标记、不删盘(记录先于字节消失,给"删错了"留反悔余地)。
///         过了保留期才真正删盘,并把记录一并硬删(字节都没了,留着记录只会让表越堆越大)。</item>
///   <item><b>弃单的分片</b>——分片只在 <c>chunk/complete</c> 成功或秒传命中时才清。客户端关页面、断网、取消,
///         分片就永久留在盘上,没有 TTL、没有会话表、没人兜底。</item>
/// </list>
/// <para>删盘一律经 <see cref="IFileStorage"/>,天然继承存储层的存储根围栏(<c>LocalFileStorage.Resolve</c>)——
/// 记录里的路径哪怕被写成 <c>../</c>,也只会抛异常,不会删到根外面去。逐行 try/catch:一条畸形记录不能掀翻整趟回收。</para>
/// <para>ponytail: 只做"库里已删 → 删盘"这一个方向。反向的孤儿扫描("盘上有、库里无")需要给 <see cref="IFileStorage"/>
/// 加枚举能力(破坏消费者自定义存储的可替换性契约),而且会与 <c>.chunks</c> 临时区撞车——实际的泄漏源已被这一向堵住。</para>
/// </summary>
public class FileGcService(
    IServiceScopeFactory scopeFactory,
    IFileStorage storage,
    ChunkStorage chunks,
    AdminUploadOptions options,
    TimeProvider time,
    ILogger<FileGcService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.EnableGc)
        {
            logger.LogInformation("TenonAdmin 磁盘回收已关闭(Upload:EnableGc=false):软删文件与弃单分片将不会被回收。");
            return;
        }

        // 先等一个周期再扫:首启时建表/种子还在跑,没必要跟它们抢。
        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.GcIntervalHours), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var (files, sessions) = await SweepAsync(stoppingToken);
                if (files > 0 || sessions > 0)
                    logger.LogInformation("TenonAdmin 磁盘回收:清理 {Files} 个软删文件、{Sessions} 个弃单分片会话。", files, sessions);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // 宿主停机,正常退出
            }
            catch (Exception ex)
            {
                // 回收任务失败绝不能把宿主拖垮:记警告,下个周期再来。
                logger.LogWarning(ex, "TenonAdmin 磁盘回收:本轮失败,下个周期重试。");
            }
        }
    }

    /// <summary>跑一趟完整回收(软删文件 + 弃单分片),返回各自清掉的数量。测试与手动触发直接调它。</summary>
    public virtual async Task<(int Files, int ChunkSessions)> SweepAsync(CancellationToken cancellationToken = default)
    {
        var files = await ReclaimDeletedFilesAsync(cancellationToken);
        var sessions = await chunks.SweepStaleAsync(
            TimeSpan.FromHours(options.GcChunkTtlHours), time.GetUtcNow(), cancellationToken);
        return (files, sessions);
    }

    /// <summary>
    /// 回收过了保留期的软删文件:删物理文件 → 硬删记录。
    /// <para>本服务是单例,而仓储是 Scoped —— 每趟自己开一个作用域(同 <c>DatabaseInitializer</c>)。</para>
    /// </summary>
    protected virtual async Task<int> ReclaimDeletedFilesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IRepository<SysFile>>();

        // 软删行对普通查询不可见,必须显式解除软删过滤器才看得到回收对象。
        // ponytail: 只按 IsDelete 筛,保留期在内存里判——软删行是有限集(收完就没了),不值得为它跟各方言的
        // COALESCE/日期函数较劲。UpdateTime 是删除时刻(见 SqlSugarRepository.DeleteAsync);老库里在那个修复之前
        // 删的行没有这个戳,回退到 CreateTime,否则它们永远收不掉。
        var deleted = await files.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .Where(f => f.IsDelete)
            .ToListAsync();

        var cutoff = time.GetLocalNow().DateTime.AddDays(-options.GcRetentionDays);
        var due = deleted.Where(f => (f.UpdateTime ?? f.CreateTime) < cutoff).ToList();

        var removed = 0;
        foreach (var file in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await storage.DeleteAsync(file.StoragePath, cancellationToken);   // 越界路径在此被存储根围栏拦下
                await files.Db.Deleteable<SysFile>().Where(f => f.Id == file.Id).ExecuteCommandAsync();
                removed++;
            }
            catch (Exception ex)
            {
                // 一条坏记录(路径畸形、文件被占用)不该拖垮整趟:记下来,继续收下一条。
                logger.LogWarning(ex, "TenonAdmin 磁盘回收:跳过文件 Id={Id},StoragePath={Path}", file.Id, file.StoragePath);
            }
        }
        return removed;
    }
}
