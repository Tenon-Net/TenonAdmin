using System.Text;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>本地存储路径穿越防护(§14)——由 scratchpad t7-storage-check 转正。IDisposable 清理临时根。</summary>
public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tenon-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() => _storage = new LocalFileStorage(new AdminUploadOptions { RootPath = _root });

    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Save_read_delete_roundtrip()
    {
        var path = await _storage.SaveAsync(Bytes("hello-tenon"), "20260707/abc.txt");
        Assert.Equal("20260707/abc.txt", path);

        // 块作用域:读流须在 Delete 前释放,否则 Windows 文件锁会拒绝删除
        await using (var read = await _storage.OpenReadAsync("20260707/abc.txt"))
        {
            Assert.NotNull(read);
            Assert.Equal("hello-tenon", await new StreamReader(read!).ReadToEndAsync());
        }

        await _storage.DeleteAsync("20260707/abc.txt");
        Assert.Null(await _storage.OpenReadAsync("20260707/abc.txt"));
    }

    [Fact]
    public async Task Open_missing_returns_null() =>
        Assert.Null(await _storage.OpenReadAsync("nope/none.txt"));

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/b/../../../evil.txt")]
    // 绝对路径逃逸:用 "/evil.txt" 而非 "C:/..."——前者在两平台都是 rooted(Linux→/evil.txt、Windows→当前盘根),
    // 都越出存储根被拒;"C:/..." 在 Linux 上不是绝对路径(无盘符),会落在根内、不构成穿越,故不能作跨平台断言。
    [InlineData("/evil.txt")]
    public async Task Rejects_path_traversal(string malicious) =>
        await Assert.ThrowsAnyAsync<Exception>(() => _storage.SaveAsync(Bytes("x"), malicious));

    [Fact]
    public async Task Read_traversal_rejected() =>
        await Assert.ThrowsAnyAsync<Exception>(() => _storage.OpenReadAsync("../../secret"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 清理尽力而为 */ }
    }
}
