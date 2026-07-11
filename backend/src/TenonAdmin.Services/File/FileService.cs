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
    AdminUploadOptions options,
    IConfigService config,
    TimeProvider timeProvider) : IFileService
{
    /// <summary>上传约束配置项分组编码(配置中心「上传策略」Tab 按此分组加载)</summary>
    internal const string GROUP = "upload";
    internal const string KEY_MAX_SIZE = "sys.upload.maxSizeMb";
    internal const string KEY_ALLOWED_EXTS = "sys.upload.allowedExtensions";

    /// <inheritdoc />
    public virtual async Task<FileUploadOutput> UploadAsync(FileUploadInput input)
    {
        AdminException.ThrowIf(input.Size <= 0, ErrorCode.FileEmpty);

        var ext = Path.GetExtension(input.FileName).ToLowerInvariant();
        // 大小上限/后缀白名单先读 SysConfig(改值即时生效),缺失或解析失败回退 Options 默认。
        // 后缀按扩展名判定,不采信可伪造的 Content-Type(§14);空白名单表示不限。
        var allowed = ParseExts(await config.GetValueByKeyAsync(KEY_ALLOWED_EXTS)) ?? options.AllowedExtensions;
        var extAllowed = allowed.Length == 0
            || allowed.Contains(ext, StringComparer.OrdinalIgnoreCase);
        AdminException.ThrowIf(!extAllowed, ErrorCode.FileExtNotAllowed,
            new Dictionary<string, object?> { ["ext"] = ext });

        var maxSizeMb = int.TryParse(await config.GetValueByKeyAsync(KEY_MAX_SIZE), out var mb) ? mb : options.MaxSizeMb;
        var maxBytes = (long)maxSizeMb * 1024 * 1024;
        AdminException.ThrowIf(input.Size > maxBytes, ErrorCode.FileTooLarge,
            new Dictionary<string, object?> { ["maxSizeMb"] = maxSizeMb });

        // 重写存储名:按日期分目录(避免单目录文件过多)+ GUIDv7 唯一名(时间有序、不可猜、无原始名成分)
        var date = timeProvider.GetUtcNow().ToString("yyyyMMdd");
        var storagePath = $"{date}/{Guid.CreateVersion7():N}{ext}";
        await storage.SaveAsync(input.Content, storagePath);

        var entity = new SysFile
        {
            OriginalName = input.FileName,
            StoragePath = storagePath,
            Extension = ext,
            ContentType = input.ContentType,
            SizeBytes = input.Size,
        };
        await files.InsertAsync(entity);   // 雪花 Id / 上传时间 / 上传人由 AOP 回填

        return new FileUploadOutput
        {
            Id = entity.Id,
            OriginalName = entity.OriginalName,
            StoragePath = entity.StoragePath,
            SizeBytes = entity.SizeBytes,
        };
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
            ContentType = file.ContentType ?? "application/octet-stream",
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
