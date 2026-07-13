using System.Security.Cryptography;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// 分片上传临时存储(设计 §4 分片上传)。分片落本地临时区(存储根下的 <c>.chunks</c> 隐藏目录),
/// 按 uploadId 归组;合并时<b>单遍</b>算 SHA-256 供完整性校验,不额外多读一遍。
/// <para>ponytail: 分片临时区固定用本地磁盘——即便最终存储换 OSS/Minio,分片装配用本地临时也是常规做法;
/// 需多实例共享分片临时区时再抽 <c>IChunkStorage</c> 扩展点。uploadId 派生自内容哈希(hex),
/// 这里再做一次字符白名单净化,兜底防路径穿越。</para>
/// </summary>
public sealed class ChunkStorage(AdminUploadOptions options)
{
    // 分片临时根:存储根下的隐藏子目录,与最终文件区隔离,便于整目录清理。
    private readonly string _tempRoot = Path.Combine(Path.GetFullPath(options.RootPath), ".chunks");

    private string PartsDir(string uploadId) => Path.Combine(_tempRoot, Sanitize(uploadId));
    private static string PartPath(string dir, int index) => Path.Combine(dir, $"{index}.part");

    /// <summary>净化 uploadId:只留字母数字(派生自 hex 哈希,本就安全;此处兜底防越界)。空则拒。</summary>
    private static string Sanitize(string uploadId)
    {
        var clean = new string(uploadId.Where(char.IsLetterOrDigit).ToArray());
        AdminException.ThrowIf(clean.Length == 0, ErrorCode.ChunkMissing);
        return clean;
    }

    /// <summary>落一个分片(幂等:重传覆盖,天然支持断点续传)。</summary>
    public async Task SaveChunkAsync(string uploadId, int index, Stream content, CancellationToken cancellationToken = default)
    {
        var dir = PartsDir(uploadId);
        Directory.CreateDirectory(dir);
        // FileMode.Create=已存在则覆盖:重传同一分片幂等,不因 CreateNew 撞名而失败。
        await using var fs = new FileStream(PartPath(dir, index), FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fs, cancellationToken);
    }

    /// <summary>已收分片下标集合(升序);断点续传时客户端据此跳过已传分片。会话不存在则空集。</summary>
    public Task<IReadOnlyCollection<int>> GetReceivedIndexesAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        var dir = PartsDir(uploadId);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyCollection<int>>([]);
        var indexes = Directory.EnumerateFiles(dir, "*.part")
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var i) ? i : -1)
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<int>>(indexes);
    }

    /// <summary>
    /// 按序合并分片 <c>0..chunkCount-1</c> 成一个可读临时流(<see cref="FileOptions.DeleteOnClose"/>,调用方释放即删),
    /// 同时<b>单遍</b>算合并内容的 SHA-256(hex,小写)。缺任一分片抛 <see cref="ErrorCode.ChunkMissing"/>。
    /// </summary>
    public async Task<MergedChunks> MergeAsync(string uploadId, int chunkCount, CancellationToken cancellationToken = default)
    {
        var dir = PartsDir(uploadId);
        for (var i = 0; i < chunkCount; i++)
            AdminException.ThrowIf(!File.Exists(PartPath(dir, i)), ErrorCode.ChunkMissing,
                new Dictionary<string, object?> { ["index"] = i });

        Directory.CreateDirectory(_tempRoot);
        var mergedPath = Path.Combine(_tempRoot, Sanitize(uploadId) + ".merged");

        long size = 0;
        string hex;
        using (var sha = SHA256.Create())
        {
            await using var outFs = new FileStream(mergedPath, FileMode.Create, FileAccess.Write);
            var buffer = new byte[81920];
            for (var i = 0; i < chunkCount; i++)
            {
                await using var part = new FileStream(PartPath(dir, i), FileMode.Open, FileAccess.Read);
                int read;
                while ((read = await part.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await outFs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    size += read;
                }
            }
            sha.TransformFinalBlock([], 0, 0);
            hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        } // outFs 在此释放(写完关闭),下面再以只读方式打开,避免共享写句柄冲突

        // DeleteOnClose:调用方 using 释放该流时,合并临时文件自动删除。
        var readStream = new FileStream(mergedPath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.DeleteOnClose);
        return new MergedChunks(readStream, hex, size);
    }

    /// <summary>清理某 uploadId 的全部分片(合并完成或放弃时)。合并临时文件走 DeleteOnClose 自清,不在此处。</summary>
    public Task DiscardAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        var dir = PartsDir(uploadId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清扫超龄的分片临时物,返回清掉的项数(<see cref="FileGcService"/> 周期调用)。
    /// <para>没有任何分片会话表可查——<c>ChunkStorage</c> 是无状态的,既不记开始时间也不记预期片数。
    /// 所以判定只能靠<b>最后写入时间</b>:一个会话在 <paramref name="ttl"/> 内没有任何分片被写过,
    /// 就认定客户端不会再回来了(关页面、断网、取消)。续传中的会话每收一片就刷新时间,不会被误扫。</para>
    /// <para>两类残留都收:① 弃单的分片目录;② <c>.merged</c> 合并临时文件——它平时靠 <c>DeleteOnClose</c> 自清,
    /// 但那只在进程活着时成立,进程被杀(OOM/kill -9)就会留下来。</para>
    /// <para>只在 <c>.chunks</c> 临时根内操作。存储根下的正式文件区(<c>{日期}/</c>)绝不触碰——
    /// 那需要的是"盘上有、库里无"的反向核对,不是超时清理。</para>
    /// </summary>
    public Task<int> SweepStaleAsync(TimeSpan ttl, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_tempRoot)) return Task.FromResult(0);

        var cutoff = (now - ttl).UtcDateTime;
        var swept = 0;

        foreach (var dir in Directory.EnumerateDirectories(_tempRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LastTouchUtc(dir) > cutoff) continue;
            Directory.Delete(dir, recursive: true);
            swept++;
        }

        foreach (var merged in Directory.EnumerateFiles(_tempRoot, "*.merged"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(merged) > cutoff) continue;
            File.Delete(merged);
            swept++;
        }

        return Task.FromResult(swept);
    }

    /// <summary>
    /// 目录的"最后被碰过"时间 = 目录自身与其中分片文件的最晚写入时间。
    /// <para>只看目录 mtime 不够:重传一个<b>已存在</b>的分片是覆盖写,只刷新文件 mtime、不刷新目录 mtime——
    /// 那样一个正在续传的会话会被误判为弃单。</para>
    /// </summary>
    private static DateTime LastTouchUtc(string dir)
    {
        var newest = Directory.GetLastWriteTimeUtc(dir);
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var touched = File.GetLastWriteTimeUtc(file);
            if (touched > newest) newest = touched;
        }
        return newest;
    }
}

/// <summary>合并结果:可读流(DeleteOnClose,调用方负责释放)+ 内容 SHA-256(hex)+ 字节数。</summary>
public sealed record MergedChunks(Stream Content, string Sha256, long Size);
