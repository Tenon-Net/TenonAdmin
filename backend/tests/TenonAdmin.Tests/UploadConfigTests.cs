using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 上传约束运行时可配(大小上限/后缀白名单)——聚焦 FileService 从 SysConfig 读值并强制执行的新逻辑。
/// 三例均命中拒收分支(在写盘/落库之前抛),故 repo/storage 传 null 不会被触及。
/// </summary>
public class UploadConfigTests
{
    // 只喂 GetValueByKeyAsync 的极小配置桩;其余方法本测试不触及。
    private sealed class StubConfig(Dictionary<string, string?> values) : IConfigService
    {
        public Task<string?> GetValueByKeyAsync(string key) => Task.FromResult(values.GetValueOrDefault(key));
        public Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input) => throw new NotImplementedException();
        public Task<SysConfig> GetAsync(long id) => throw new NotImplementedException();
        public Task<SiteInfoOutput> GetSiteInfoAsync() => throw new NotImplementedException();
        public Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items) => throw new NotImplementedException();
        public Task<long> AddAsync(ConfigInput input) => throw new NotImplementedException();
        public Task UpdateAsync(long id, ConfigInput input) => throw new NotImplementedException();
        public Task DeleteAsync(long id) => throw new NotImplementedException();
    }

    private static FileService Make(Dictionary<string, string?> config, AdminUploadOptions? options = null)
    {
        var opts = options ?? new AdminUploadOptions();
        // 三例均在校验阶段(写盘/落库/分片之前)抛,故 repo/storage 传 null、chunks 传实例但不触及。
        return new(null!, null!, new ChunkStorage(opts), opts, new StubConfig(config), TimeProvider.System);
    }

    private static FileUploadInput Upload(string name, long size) =>
        new() { Content = Stream.Null, FileName = name, Size = size, ContentType = null };

    // 配置键与 FileService 内部常量一致(那边 internal,测试跨程序集用字面量,同安全策略测试范式)
    private const string KEY_MAX_SIZE = "sys.upload.maxSizeMb";
    private const string KEY_ALLOWED_EXTS = "sys.upload.allowedExtensions";

    [Fact]
    public async Task Db_maxSize_is_read_and_enforced()
    {
        // DB 上限调到 1MB(默认 20MB),传 2MB 的允许后缀 → FileTooLarge,证明读到 DB 新值
        var svc = Make(new() { [KEY_MAX_SIZE] = "1" });
        var ex = await Assert.ThrowsAsync<AdminException>(() => svc.UploadAsync(Upload("a.png", 2 * 1024 * 1024)));
        Assert.Equal(ErrorCode.FileTooLarge, ex.Code);
    }

    [Fact]
    public async Task Db_extension_whitelist_is_read_and_enforced()
    {
        // DB 白名单只留 .png,传 .jpg → FileExtNotAllowed(默认 Options 是允许 .jpg 的,证明用的是 DB 值)
        var svc = Make(new() { [KEY_ALLOWED_EXTS] = ".png" });
        var ex = await Assert.ThrowsAsync<AdminException>(() => svc.UploadAsync(Upload("a.jpg", 100)));
        Assert.Equal(ErrorCode.FileExtNotAllowed, ex.Code);
    }

    [Fact]
    public async Task Falls_back_to_options_when_config_missing()
    {
        // DB 无键 → 回退 Options;把 Options 上限设 1MB,传 2MB → FileTooLarge,证明兜底路径生效
        var svc = Make(new(), new AdminUploadOptions { MaxSizeMb = 1 });
        var ex = await Assert.ThrowsAsync<AdminException>(() => svc.UploadAsync(Upload("a.png", 2 * 1024 * 1024)));
        Assert.Equal(ErrorCode.FileTooLarge, ex.Code);
    }
}
