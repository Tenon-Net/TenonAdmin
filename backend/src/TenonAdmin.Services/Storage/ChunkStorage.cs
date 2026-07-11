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
}

/// <summary>合并结果:可读流(DeleteOnClose,调用方负责释放)+ 内容 SHA-256(hex)+ 字节数。</summary>
public sealed record MergedChunks(Stream Content, string Sha256, long Size);
