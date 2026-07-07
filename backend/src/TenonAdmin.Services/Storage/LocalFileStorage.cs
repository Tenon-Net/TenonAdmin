using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IFileStorage"/> 的本地磁盘实现(设计 §5/§14)。所有相对路径都经 <see cref="Resolve"/>
/// 解析并<b>校验落在存储根内</b>——纵深防御路径穿越(<c>../</c>、绝对路径、软链等),不信任调用方传入的路径。
/// </summary>
public sealed class LocalFileStorage(AdminUploadOptions options) : IFileStorage
{
    // 存储根的绝对规范路径(一次算好)。末尾统一带分隔符,便于前缀判定不误伤同名兄弟目录。
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));

    /// <inheritdoc />
    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken cancellationToken = default)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // FileMode.CreateNew:目标已存在则抛(存储名是雪花/GUID,撞名意味着上层生成逻辑出错,应大声失败而非覆盖)
        await using var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(fs, cancellationToken);
        return relativePath;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = Resolve(relativePath);
        Stream? stream = File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read) : null;
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = Resolve(relativePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把相对路径解析成绝对路径,并断言它仍在存储根之内——否则视为路径穿越攻击,直接拒绝(§14)。
    /// <para><see cref="Path.GetFullPath(string)"/> 会把 <c>..</c> 归一化掉,再比对前缀即可堵死逃逸;
    /// 传入绝对路径时 <see cref="Path.Combine(string,string)"/> 会丢弃根、直接返回该绝对路径,同样被前缀校验拦下。</para>
    /// </summary>
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var withinRoot = full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!withinRoot)
            throw new InvalidOperationException($"非法存储路径(越出存储根):{relativePath}");
        return full;
    }
}
