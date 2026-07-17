using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IFileService"/> 默认实现。上传三道关(设计 §14):
/// <list type="number">
///   <item>非空校验 + <b>后缀白名单</b>(按原始名后缀,不信 Content-Type)</item>
///   <item><b>大小上限</b>(超 <c>MaxSizeMb</c> 拒收)</item>
///   <item><b>文件名重写</b>为 <c>{日期}/{GUIDv7}{后缀}</c> —— 原始名绝不进物理路径,天然免路径穿越;
///         存储层 <c>LocalFileStorage</c> 再做一次根目录围栏兜底(纵深防御)</item>
/// </list>
/// </summary>
public class FileService(
    IRepository<SysFile> files,
    IFileStorage storage,
    ChunkStorage chunks,
    AdminUploadOptions options,
    IConfigService config,
    TimeProvider timeProvider,
    ICurrentUser? currentUser = null) : IFileService   // 可选参数:默认 DI 注入;消费者子类省略也能编译(§5.3,同 FileGcService 成法)
{
    /// <summary>上传约束配置项分组编码(配置中心「上传策略」Tab 按此分组加载)</summary>
    internal const string GROUP = "upload";
    internal const string KEY_MAX_SIZE = "sys.upload.maxSizeMb";
    internal const string KEY_ALLOWED_EXTS = "sys.upload.allowedExtensions";

    /// <inheritdoc />
    public virtual async Task<FileUploadOutput> UploadAsync(FileUploadInput input)
    {
        var ext = Path.GetExtension(input.FileName).ToLowerInvariant();
        await ValidateUploadAsync(ext, input.Size);
        // 单文件上传不计算内容哈希(IFormFile 流单向不可回读);hash 留 null,秒传主要惠及分片(大)文件。
        return await PersistAsync(input.Content, input.FileName, ext, input.ContentType, input.Size, hash: null);
    }

    /// <summary>
    /// 上传三道关的前两关(设计 §14):空文件 + 后缀白名单(按扩展名,不信 Content-Type)+ 大小上限。
    /// 单文件与分片完成共用同一处强制点。白名单/上限先读 SysConfig(改值即时生效),缺失回退 Options。
    /// </summary>
    protected virtual async Task ValidateUploadAsync(string ext, long size)
    {
        AdminException.ThrowIf(size <= 0, ErrorCode.FileEmpty);

        var allowed = ParseExts(await config.GetValueByKeyAsync(KEY_ALLOWED_EXTS)) ?? options.AllowedExtensions;
        var extAllowed = allowed.Length == 0 || allowed.Contains(ext, StringComparer.OrdinalIgnoreCase);
        AdminException.ThrowIf(!extAllowed, ErrorCode.FileExtNotAllowed,
            new Dictionary<string, object?> { ["ext"] = ext });

        var maxSizeMb = int.TryParse(await config.GetValueByKeyAsync(KEY_MAX_SIZE), out var mb) ? mb : options.MaxSizeMb;
        AdminException.ThrowIf(size > (long)maxSizeMb * 1024 * 1024, ErrorCode.FileTooLarge,
            new Dictionary<string, object?> { ["maxSizeMb"] = maxSizeMb });
    }

    /// <summary>
    /// 第三关 + 落存储 + 记账:重写成安全存储名(<c>{日期}/{GUIDv7}{后缀}</c>,原始名绝不进物理路径)→
    /// 交 <see cref="IFileStorage"/> 落盘 → 写 <c>sys_file</c>。单文件与分片完成共用。
    /// </summary>
    protected virtual async Task<FileUploadOutput> PersistAsync(Stream content, string fileName, string ext, string? contentType, long size, string? hash)
    {
        var date = timeProvider.GetUtcNow().ToString("yyyyMMdd");
        var storagePath = $"{date}/{Guid.CreateVersion7():N}{ext}";
        await storage.SaveAsync(content, storagePath);

        var entity = new SysFile
        {
            OriginalName = fileName,
            StoragePath = storagePath,
            Extension = ext,
            ContentType = contentType,
            SizeBytes = size,
            Hash = hash,
        };
        await files.InsertAsync(entity);   // 雪花 Id / 上传时间 / 上传人由 AOP 回填

        return ToOutput(entity);
    }

    private static FileUploadOutput ToOutput(SysFile f) => new()
    {
        Id = f.Id,
        OriginalName = f.OriginalName,
        StoragePath = f.StoragePath,
        SizeBytes = f.SizeBytes,
    };

    /// <summary>
    /// 秒传命中时取得<b>本引用方</b>(当前用户)对该内容的引用行:已有则复用、没有则新建。
    /// <para><b>为什么要幂等</b>:ChunkInit 是可重复调用的探针(重选同一文件、组件重挂、重试都会再触发)。
    /// 若每次命中都无条件新建行,未被业务引用的孤儿行(IsDelete=false)会无界堆积、永不进 GC 回收集,
    /// 且让 <c>FileGcService</c> 的 StoragePath 共享判定<b>恒为真</b> → 物理文件永不删盘,击穿秒传去重的 GC 语义。
    /// 故按 <c>(Hash, 当前用户)</c> 去重:同一用户对同一内容至多一行,跨用户仍各自独立(T-D7 隔离)。</para>
    /// <para>取不到当前用户时(消费者子类未注入 <see cref="ICurrentUser"/>)退回直接新建,行为与幂等前一致。</para>
    /// </summary>
    protected virtual async Task<FileUploadOutput> ReferenceForCallerAsync(SysFile existing, string fileName, string? contentType)
    {
        if (currentUser?.UserId is long uid)
        {
            var mine = await files.GetFirstAsync(f => f.Hash == existing.Hash && f.CreateUserId == uid);
            if (mine is not null) return ToOutput(mine);   // 幂等:本用户已有同内容引用行 → 复用,不再插
        }
        return await ReferenceExistingAsync(existing, fileName, contentType);
    }

    /// <summary>
    /// 秒传命中(T-D7):为新引用方插一条<b>独立</b>的 <c>sys_file</c> 记录(共享既有行的 <c>StoragePath</c>/大小/哈希),
    /// 而非复用既有行的 Id。这样各引用方各拥有独立记录——一方删除只软删自己那条,不牵连另一方;物理文件仅当无任何行
    /// 引用其 <c>StoragePath</c> 时才由 <c>FileGcService</c> 删除(见其 <c>ReclaimDeletedFilesAsync</c> 的共享判定)。
    /// <para>原实现直接返回既有行,两个引用方共享同一 Id/记录,一方删除即让另一方的引用悬空、GC 到期删盘后彻底丢失。</para>
    /// </summary>
    protected virtual async Task<FileUploadOutput> ReferenceExistingAsync(SysFile existing, string fileName, string? contentType)
    {
        var entity = new SysFile
        {
            OriginalName = fileName,                       // 引用方各自的原始名
            StoragePath = existing.StoragePath,            // 共享同一物理文件
            Extension = existing.Extension,
            ContentType = contentType ?? existing.ContentType,
            SizeBytes = existing.SizeBytes,
            Hash = existing.Hash,
        };
        await files.InsertAsync(entity);   // 新雪花 Id / 上传时间 / 上传人(= 当前引用方)由 AOP 回填
        return ToOutput(entity);
    }

    /// <inheritdoc />
    public virtual async Task<ChunkInitOutput> ChunkInitAsync(ChunkInitInput input)
    {
        // 秒传:同内容哈希已存在则免传。为本引用方取/建独立记录(共享物理文件,不复用他人行 Id,T-D7);
        // 探针可重复调用,故按 (Hash, 当前用户) 幂等,避免重复探测泄漏孤儿行(见 ReferenceForCallerAsync)。
        var existing = await files.GetFirstAsync(f => f.Hash == input.FileHash);
        if (existing is not null)
            return new ChunkInitOutput { Uploaded = true, File = await ReferenceForCallerAsync(existing, input.FileName, input.ContentType) };

        // uploadId 直接用 FileHash;返回已收分片供断点续传。
        var received = await chunks.GetReceivedIndexesAsync(input.FileHash);
        return new ChunkInitOutput { Uploaded = false, UploadId = input.FileHash, ReceivedIndexes = received };
    }

    /// <inheritdoc />
    public virtual Task SaveChunkAsync(ChunkSaveInput input) =>
        chunks.SaveChunkAsync(input.UploadId, input.Index, input.Content);

    /// <inheritdoc />
    public virtual async Task<FileUploadOutput> ChunkCompleteAsync(ChunkCompleteInput input)
    {
        // 秒传兜底:完成时再查一次(并发下他人可能刚传完同 hash),命中则弃分片、为本引用方取/建独立记录(T-D7,幂等)。
        var existing = await files.GetFirstAsync(f => f.Hash == input.FileHash);
        if (existing is not null)
        {
            await chunks.DiscardAsync(input.UploadId);
            return await ReferenceForCallerAsync(existing, input.FileName, input.ContentType);
        }

        var ext = Path.GetExtension(input.FileName).ToLowerInvariant();
        // 缺片抛 ChunkMissing —— 这时<b>不能</b>清分片:会话还能续传,清了等于让客户端从头再传一遍。
        var merged = await chunks.MergeAsync(input.UploadId, input.ChunkCount);
        try
        {
            await using var content = merged.Content;   // DeleteOnClose:用完即删合并临时文件

            // 完整性:服务端单遍重算的 SHA-256 必须与客户端声明一致(防漏片/传输损坏)。
            AdminException.ThrowIf(!string.Equals(merged.Sha256, input.FileHash, StringComparison.OrdinalIgnoreCase),
                ErrorCode.ChunkHashMismatch);
            await ValidateUploadAsync(ext, merged.Size);   // 复用三道关(大小/后缀)

            var output = await PersistAsync(content, input.FileName, ext, input.ContentType, merged.Size, input.FileHash);
            await chunks.DiscardAsync(input.UploadId);
            return output;
        }
        catch (AdminException)
        {
            // 合并之后才做的三道校验(哈希不符/超大/后缀不许)属于<b>永久性拒绝</b>:同样的分片再传一次还是被拒,
            // 留着它们就是纯粹的泄漏——每一次被拒的上传都漏一份。清掉。
            // (只认 AdminException:落库失败之类的暂时性故障保留分片,客户端可以只重试 complete,不必重传整个文件;
            //  就算它再也不回来,弃单清扫也会按 TTL 兜底。)
            await chunks.DiscardAsync(input.UploadId);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task<FileDownload> DownloadAsync(long id)
    {
        var file = await files.GetByIdAsync(id);
        AdminException.ThrowIf(file is null, ErrorCode.FileNotFound);

        var stream = await storage.OpenReadAsync(file!.StoragePath);
        AdminException.ThrowIf(stream is null, ErrorCode.FileNotFound);   // 记录在、物理丢了也算不存在

        return new FileDownload
        {
            Content = stream!,
            OriginalName = file.OriginalName,
            // 空白也要兜底,不只是 null:multipart 部件不带 Content-Type 头时 IFormFile.ContentType 是<b>空串</b>,
            // 原样落库、原样回传,ASP.NET 解析媒体类型时会直接抛(500)。
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
        };
    }

    /// <inheritdoc />
    public virtual Task<PagedList<SysFile>> PageAsync(FilePageInput input) =>
        files.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.FileName), f => f.OriginalName.Contains(input.FileName!))
            .OrderBy(f => f.Id, OrderByType.Desc)   // 雪花 Id 时间有序,最新在前
            .ToPagedListAsync(input.Current, input.Size);

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var file = await files.GetByIdAsync(id);
        AdminException.ThrowIf(file is null, ErrorCode.FileNotFound);
        await files.DeleteAsync(id);   // 软删记录;物理回收留清理任务(ponytail:v1 不删盘)
    }

    /// <inheritdoc />
    public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
    {
        // 逐个软删,复用仓储单删(不存在的 Id 影响 0 行,无害);物理文件同样保留(v1 不删盘)。
        foreach (var id in ids) await files.DeleteAsync(id);
    }

    // 后缀白名单以逗号分隔字符串落库;规范化为「含点、小写」。空/全空白 → null(回退 Options 默认)。
    // ponytail: 逐次解析足够——上传是低频写路径,不缓存解析结果。
    private static string[]? ParseExts(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var exts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
            .ToArray();
        return exts.Length == 0 ? null : exts;
    }
}
