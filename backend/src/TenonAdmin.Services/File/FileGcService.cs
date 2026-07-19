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
    ILogger<FileGcService> logger,
    ICacheProvider? cache = null) : BackgroundService   // 可选参数:本类是 public 可继承,加必需参数即源码破坏性变更
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
                if (!await TryAcquireTickAsync(stoppingToken)) continue;   // 这一轮已被别的副本领走

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

    /// <summary>
    /// 领这一轮的活:同一个时间桶内<b>只有一个副本</b>能领到。
    /// <para>GC 注册在每个副本上,不设租约则 N 个副本同时扫同一批文件——逐行 try/catch + <c>File.Exists</c> 兜底
    /// 使它<b>不会出错</b>,但会重复 I/O 并刷一堆"跳过文件"的告警。用一次原子自增当租约:
    /// 时间桶做键,只有拿到 1 的那个副本干活。内存缓存(单实例)恒为 1 → 行为不变;Redis 下才真正互斥。</para>
    /// <para>public virtual:消费者可换成自己的选主机制(如 k8s Lease)。</para>
    /// <para>// ponytail: 各副本时钟若差到跨桶,两边可能都领到——GC 本来就是幂等的(删过的文件 File.Exists 为假、
    /// 硬删按 Id 幂等),多跑一趟只是浪费 I/O,不值得为此上真正的分布式锁。</para>
    /// </summary>
    public virtual async Task<bool> TryAcquireTickAsync(CancellationToken cancellationToken = default)
    {
        if (cache is null) return true;   // 没有缓存实现(消费者自建实例)→ 退回"人人都扫",与旧行为一致

        var intervalSeconds = (long)TimeSpan.FromHours(options.GcIntervalHours).TotalSeconds;
        if (intervalSeconds <= 0) return true;

        var bucket = time.GetUtcNow().ToUnixTimeSeconds() / intervalSeconds;
        var claims = await cache.IncrementAsync(
            CacheKeys.FileGcTick(bucket), TimeSpan.FromSeconds(intervalSeconds * 2), cancellationToken);
        return claims == 1;   // 第一个 INCR 到 1 的副本领走这一轮
    }

    /// <summary>跑一趟完整回收(软删文件 + 弃单分片),返回各自清掉的数量。测试与手动触发直接调它(<b>不过租约</b>)。</summary>
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
                // 共享判定(T-D7):秒传去重下多条独立记录可共享同一物理文件。删盘前查是否仍有他行(含未删/软删,
                // 故 ClearFilter)引用同一 StoragePath——有则只硬删本记录、保留物理文件,由最后一个引用方回收时删盘。
                var sharedByOthers = await files.AsQueryable()
                    .ClearFilter<ISoftDelete>()
                    .Where(f => f.StoragePath == file.StoragePath && f.Id != file.Id)
                    .AnyAsync();
                if (!sharedByOthers)
                    await storage.DeleteAsync(file.StoragePath, cancellationToken);   // 最后一个引用 → 删盘;越界路径在此被存储根围栏拦下
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
