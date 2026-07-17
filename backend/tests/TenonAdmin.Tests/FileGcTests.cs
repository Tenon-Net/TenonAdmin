using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 磁盘回收(dev-plan T-D2)。上传子系统原本有两处只涨不消:软删的文件从不删盘、弃单的分片永远留在盘上。
/// <para>安全边界一并钉死:回收只经 <see cref="IFileStorage"/>,存储根围栏必须挡住畸形路径,且不能因此中断整趟回收。</para>
/// </summary>
public class FileGcTests : IAsyncLifetime
{
    private readonly string _id = $"filegc-{Guid.NewGuid():N}";
    private readonly string _dbFile;
    private readonly string _root;
    private readonly FixedTime _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _sp;
    private readonly AdminUploadOptions _options;

    public FileGcTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{_id}.db");
        _root = Path.Combine(Path.GetTempPath(), $"tenon-root-{_id}");
        _options = new AdminUploadOptions { RootPath = _root, GcRetentionDays = 7, GcChunkTtlHours = 24 };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(_options);
        services.AddSingleton<TimeProvider>(_clock);   // 前置注册 → 压过内核默认(TryAdd)
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(_id, _dbFile) },
            [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        _sp = services.BuildServiceProvider();

        _sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(SysFile), typeof(SysConfig));
    }

    private IFileStorage Storage => _sp.GetRequiredService<IFileStorage>();
    private FileGcService Gc => _sp.GetRequiredService<FileGcService>();
    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    /// <summary>落一个物理文件 + 一条记录,返回记录 Id。</summary>
    private async Task<long> PutFileAsync(string storagePath, string content)
    {
        await Storage.SaveAsync(Bytes(content), storagePath);
        return await InsertRowAsync(storagePath);
    }

    /// <summary>只落一条记录(物理文件可有可无——畸形路径用例故意不落盘)。</summary>
    private async Task<long> InsertRowAsync(string storagePath)
    {
        using var scope = _sp.CreateScope();
        var files = scope.ServiceProvider.GetRequiredService<IRepository<SysFile>>();
        var row = new SysFile
        {
            OriginalName = Path.GetFileName(storagePath),
            StoragePath = storagePath,
            Extension = Path.GetExtension(storagePath),
            SizeBytes = 1,
        };
        await files.InsertAsync(row);
        // 取该 StoragePath 下最新一行(= 刚插入的这条);雪花 Id 时间有序,故按 Id 降序取首行,
        // 在共享 StoragePath(秒传去重)下也能无歧义拿到本次插入的记录。
        return (await files.AsQueryable().Where(f => f.StoragePath == storagePath)
            .OrderBy(f => f.Id, OrderByType.Desc).FirstAsync()).Id;
    }

    private async Task SoftDeleteAsync(long id)
    {
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepository<SysFile>>().DeleteAsync(id);
    }

    /// <summary>连软删行一起捞(GC 之后应当连行都不剩)。</summary>
    private async Task<SysFile?> FindRowAsync(long id)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepository<SysFile>>()
            .AsQueryable().ClearFilter<ISoftDelete>().Where(f => f.Id == id).FirstAsync();
    }

    private bool OnDisk(string storagePath) => File.Exists(Path.Combine(_root, storagePath.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task FileGc_RemovesSoftDeletedPhysicalFile_AndLeavesLiveFiles()
    {
        var dead = await PutFileAsync("20260101/dead.txt", "garbage");
        var live = await PutFileAsync("20260101/live.txt", "keep-me");

        await SoftDeleteAsync(dead);
        _clock.Now = _clock.Now.AddDays(8);   // 越过 7 天保留期

        var (files, _) = await Gc.SweepAsync();

        Assert.Equal(1, files);
        Assert.False(OnDisk("20260101/dead.txt"));   // 字节回收了
        Assert.Null(await FindRowAsync(dead));       // 记录也硬删了(没有字节的记录留着只会让表越堆越大)
        Assert.True(OnDisk("20260101/live.txt"));    // 没删的文件纹丝不动
        Assert.NotNull(await FindRowAsync(live));
    }

    [Fact]
    public async Task FileGc_SharedStorage_KeepsPhysicalFileUntilLastReferenceReclaimed()
    {
        // 秒传去重(T-D7):两条独立记录共享同一物理文件(a 落盘,b 只是引用同一 StoragePath)
        const string path = "20260101/shared.txt";
        var a = await PutFileAsync(path, "shared-bytes");
        var b = await InsertRowAsync(path);

        // 删 A 越过保留期 → 回收 A 记录,但物理文件因 B 仍引用而保留
        await SoftDeleteAsync(a);
        _clock.Now = _clock.Now.AddDays(8);
        var (files1, _) = await Gc.SweepAsync();
        Assert.Equal(1, files1);
        Assert.Null(await FindRowAsync(a));       // A 记录硬删
        Assert.True(OnDisk(path));                // 物理文件还在:B 仍引用
        Assert.NotNull(await FindRowAsync(b));     // B 记录完好

        // 再删 B 越过保留期 → 最后一个引用,物理文件此时才删
        await SoftDeleteAsync(b);
        _clock.Now = _clock.Now.AddDays(8);
        var (files2, _) = await Gc.SweepAsync();
        Assert.Equal(1, files2);
        Assert.Null(await FindRowAsync(b));
        Assert.False(OnDisk(path));               // 无引用了 → 字节回收
    }

    [Fact]
    public async Task FileGc_SkipsRowsInsideRetentionWindow()
    {
        var id = await PutFileAsync("20260101/recent.txt", "oops-deleted-by-mistake");
        await SoftDeleteAsync(id);
        _clock.Now = _clock.Now.AddDays(1);   // 保留期(7 天)内

        var (files, _) = await Gc.SweepAsync();

        Assert.Equal(0, files);
        Assert.True(OnDisk("20260101/recent.txt"));                 // 还在:保留期是"删错了"的反悔窗口
        Assert.True((await FindRowAsync(id))!.IsDelete);            // 仍是软删态
    }

    [Fact]
    public async Task FileGc_TraversalPath_DoesNotTouchOutsideRoot_AndKeepsSweeping()
    {
        // 存储根之外的无辜文件(与根同级);记录里把路径写成 ../ 指向它 —— 模拟被篡改/写坏的 StoragePath
        var outside = Path.Combine(Path.GetTempPath(), $"tenon-outside-{_id}.txt");
        await File.WriteAllTextAsync(outside, "innocent");

        var evil = await InsertRowAsync($"../{Path.GetFileName(outside)}");
        var ok = await PutFileAsync("20260101/ok.txt", "collect-me");

        await SoftDeleteAsync(evil);
        await SoftDeleteAsync(ok);
        _clock.Now = _clock.Now.AddDays(8);

        var (files, _) = await Gc.SweepAsync();

        Assert.True(File.Exists(outside));            // 存储根围栏挡住了:根外的文件一根汗毛都没动
        Assert.NotNull(await FindRowAsync(evil));     // 畸形记录被跳过(记警告),不会被误当作已回收
        Assert.Equal(1, files);                       // 且没有掀翻整趟回收 ——
        Assert.False(OnDisk("20260101/ok.txt"));      // 同批的正常文件照收不误
        Assert.Null(await FindRowAsync(ok));

        File.Delete(outside);
    }

    [Fact]
    public async Task ChunkSweep_RemovesAbandonedParts_AfterTtl()
    {
        var chunks = _sp.GetRequiredService<ChunkStorage>();
        await chunks.SaveChunkAsync("abandoned01", 0, Bytes("part-0"));
        await chunks.SaveChunkAsync("resuming02", 0, Bytes("part-0"));

        var abandoned = Path.Combine(_root, ".chunks", "abandoned01");
        var resuming = Path.Combine(_root, ".chunks", "resuming02");
        Assert.True(Directory.Exists(abandoned));

        // 把弃单会话的时间戳推回 48 小时前(TTL 24 小时)。另一个会话保持"刚写过",代表续传中。
        var stale = DateTime.UtcNow.AddHours(-48);
        foreach (var f in Directory.EnumerateFiles(abandoned)) File.SetLastWriteTimeUtc(f, stale);
        Directory.SetLastWriteTimeUtc(abandoned, stale);

        var swept = await chunks.SweepStaleAsync(TimeSpan.FromHours(24), DateTimeOffset.UtcNow);

        Assert.Equal(1, swept);
        Assert.False(Directory.Exists(abandoned));   // 弃单的清了
        Assert.True(Directory.Exists(resuming));     // 续传中的没被误伤
    }

    [Fact]
    public async Task ChunkComplete_DiscardsParts_OnSuccessAndOnPermanentRejection()
    {
        using var scope = _sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFileService>();

        // ① 永久性拒绝(客户端声明的哈希与服务端重算的不符):分片是垃圾,再传一次还是被拒 → 必须清掉,否则每次被拒都漏一份
        const string lie = "0000000000000000000000000000000000000000000000000000000000000000";
        await svc.SaveChunkAsync(new ChunkSaveInput { UploadId = lie, Index = 0, Content = Bytes("real-content") });
        var partsDir = Path.Combine(_root, ".chunks", lie);
        Assert.True(Directory.Exists(partsDir));

        var ex = await Assert.ThrowsAsync<AdminException>(() => svc.ChunkCompleteAsync(new ChunkCompleteInput
        {
            UploadId = lie, FileHash = lie, FileName = "a.png", ChunkCount = 1, ContentType = "image/png",
        }));
        Assert.Equal(ErrorCode.ChunkHashMismatch, ex.Code);
        Assert.False(Directory.Exists(partsDir));   // ← 修复前:分片留在盘上

        // ② 正常完成:分片同样不该留下
        var content = "chunked-payload"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await svc.SaveChunkAsync(new ChunkSaveInput { UploadId = hash, Index = 0, Content = new MemoryStream(content) });
        await svc.ChunkCompleteAsync(new ChunkCompleteInput
        {
            UploadId = hash, FileHash = hash, FileName = "b.png", ChunkCount = 1, ContentType = "image/png",
        });
        Assert.False(Directory.Exists(Path.Combine(_root, ".chunks", hash)));
    }

    /// <summary>固定时钟(本地时区取 UTC,断言不受运行机器时区影响)。</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // 容器必须异步释放:事件总线(ChannelEventBus)只实现 IAsyncDisposable,同步 Dispose 会直接抛。
    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        TestDb.Cleanup(_id, _dbFile);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 清理尽力而为 */ }
    }
}
