using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Excel;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 导入导出测试(excel-ledger §9 G3 领域层 + G4 端点信封)。完整套件在 G5。
/// 每条必须先被变异证伪(改坏→红→改回),跑绿不算数。
/// </summary>
public class ImportExportTests
{
    /// <summary>
    /// §5.2:导出 xlsx 端点返回文件流,不进 Result&lt;T&gt; 信封。
    /// Content-Type 为 spreadsheet;Content-Disposition 含 RFC 5987 的 filename*=UTF-8''。
    /// 变异:改成 return Result.Ok(...) 或裸 DTO → body 变 JSON 信封、Content-Type 非 spreadsheet → 本条红。
    /// </summary>
    [Fact]
    public async Task Export_ReturnsXlsxStream_NotResultEnvelope()
    {
        using var f = new AdminAppFactory
        {
            // TestHost 默认 MissingExcelProvider;用真 codec 写出 xlsx 字节
            Overrides = s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IExcelReader, MiniExcelReader>());
                s.Replace(ServiceDescriptor.Singleton<IExcelWriter, MiniExcelWriter>());
                s.Replace(ServiceDescriptor.Singleton<IExcelTemplateBuilder, OpenXmlTemplateBuilder>());
            },
        };
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", "Test@123456"));

        var resp = await client.GetAsync("/api/v1/sys/user/export");
        Assert.True(resp.IsSuccessStatusCode, $"export HTTP {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");

        var media = resp.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", media);

        var disposition = resp.Content.Headers.ContentDisposition?.ToString()
                          ?? resp.Headers.GetValues("Content-Disposition").FirstOrDefault()
                          ?? "";
        Assert.Contains("filename*=UTF-8''", disposition, StringComparison.OrdinalIgnoreCase);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4, $"xlsx 应有内容,实际 {bytes.Length} 字节");
        // ZIP/xlsx 魔数 PK
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        // 不是 JSON 信封(不以 { 开头)
        Assert.NotEqual((byte)'{', bytes[0]);
    }

    /// <summary>
    /// 坑 6:CommitAsync 不得信任前端送来的 Errors。
    /// 一行真实非法(字典值「男性」),Errors 显式置空直送 Commit → 未落库且 Failures 带 ImportCellDictInvalid。
    /// 变异:注释 CommitAsync 里的 ValidateAllAsync 重新校验 → 本条必须红。
    /// </summary>
    [Fact]
    public async Task CommitAsync_TamperedErrors_StillBlocked()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        const string account = "import-tamper-err";
        // 真实非法:Gender=「男性」不是字典 label(合法是 男/女/未知);OrgName 用种子机构
        var row = new ImportRow
        {
            Index = 1,
            Cells = new Dictionary<string, string?>
            {
                ["Account"] = account,
                ["Name"] = "篡改错误用户",
                ["Gender"] = "男性",
                ["OrgName"] = "技术部",
            },
            Errors = [],   // 显式置空,模拟客户端清掉服务端错误
        };

        var result = await runner.CommitAsync([row], profile, DuplicateStrategy.Skip);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.True(result.Failed >= 1, "应有失败行");
        Assert.Contains(result.Failures, r =>
            r.Errors.Any(e => e.Code == ErrorCode.ImportCellDictInvalid));

        // 库里没多出这一行
        var exists = await users.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .AnyAsync(u => u.Account == account);
        Assert.False(exists, "非法行不得落库");
    }

    /// <summary>
    /// 坑 1:ExportAsync 不得被 PagedListExtensions.MAX_SIZE=200 截断。
    /// 造 250 条以上 → 返回数 > 200 且等于实际数量。
    /// 变异:把 ExportAsync 改成走 PageAsync(Size=50000) → 得 200 行 → 本条必须红。
    /// </summary>
    [Fact]
    public async Task ExportAsync_NotTruncatedAt200()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var repo = sp.GetRequiredService<IRepository<SysUser>>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var hash = hasher.Hash("Export@Test1");

        // 批量插入 250 条(不经 AddAsync 以加速;本用例只关心 Export 取数条数)
        var batch = Enumerable.Range(0, 250).Select(i => new SysUser
        {
            Account = $"export-bulk-{i:D4}",
            Password = hash,
            Name = $"导出用户{i}",
            Enabled = true,
            IsSuperAdmin = false,
        }).ToList();
        await repo.InsertRangeAsync(batch);

        var total = await repo.AsQueryable().CountAsync();
        Assert.True(total > 200, $"前置:库内用户应 >200,实际 {total}");

        var userService = sp.GetRequiredService<IUserService>();
        var exported = await userService.ExportAsync(new UserPageInput());

        Assert.True(exported.Count > 200,
            $"导出不得被 200 截断,实际 {exported.Count}");
        Assert.Equal(total, exported.Count);
    }
}
