using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Excel;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 导入导出领域/HTTP 测试(excel-ledger §9 G5)。每条上方注释写清「变异什么会让它红」。
/// 数据范围招牌能力见 <see cref="ImportExportScopeTests"/>;演示模式/操作日志见同文件后半。
/// </summary>
public class ImportExportTests
{
    private static void UseRealExcelCodecs(IServiceCollection s)
    {
        s.Replace(ServiceDescriptor.Singleton<IExcelReader, MiniExcelReader>());
        s.Replace(ServiceDescriptor.Singleton<IExcelWriter, MiniExcelWriter>());
        s.Replace(ServiceDescriptor.Singleton<IExcelTemplateBuilder, OpenXmlTemplateBuilder>());
    }

    private static ImportRow Row(int index, string account, string name, string orgName = "技术部",
        string? gender = null, Dictionary<string, string?>? extra = null)
    {
        var cells = new Dictionary<string, string?>
        {
            ["Account"] = account,
            ["Name"] = name,
            ["OrgName"] = orgName,
        };
        if (gender is not null) cells["Gender"] = gender;
        if (extra is not null)
            foreach (var (k, v) in extra) cells[k] = v;
        return new ImportRow { Index = index, Cells = cells, Errors = [] };
    }

    // ── §5.2 / 清单 13:导出不被信封包裹(G4 已有) ──────────────────────────

    /// <summary>
    /// §5.2:导出 xlsx 端点返回文件流,不进 Result&lt;T&gt; 信封。
    /// 变异:改成 return Result.Ok(...) 或裸 DTO → body 变 JSON 信封、Content-Type 非 spreadsheet → 本条红。
    /// </summary>
    [Fact]
    public async Task Export_ReturnsXlsxStream_NotResultEnvelope()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
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
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        Assert.NotEqual((byte)'{', bytes[0]);
    }

    // ── 清单 7:篡改 Errors 无效(G3 已有) ──────────────────────────────────

    /// <summary>
    /// 坑 6:CommitAsync 不得信任前端送来的 Errors。
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
            Errors = [],
        };

        var result = await runner.CommitAsync([row], profile, DuplicateStrategy.Skip);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.True(result.Failed >= 1, "应有失败行");
        Assert.Contains(result.Failures, r =>
            r.Errors.Any(e => e.Code == ErrorCode.ImportCellDictInvalid));

        var exists = await users.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .AnyAsync(u => u.Account == account);
        Assert.False(exists, "非法行不得落库");
    }

    // ── 坑 1:导出不截断 200(G3 已有) ──────────────────────────────────────

    /// <summary>
    /// 坑 1:ExportAsync 不得被 PagedListExtensions.MAX_SIZE=200 截断。
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

        Assert.True(exported.Count > 200, $"导出不得被 200 截断,实际 {exported.Count}");
        Assert.Equal(total, exported.Count);
    }

    // ── 清单 3:字典双向 ───────────────────────────────────────────────────

    /// <summary>
    /// 导出 Gender value「1」→ 单元格 label「男」;导入 label「男」→ 库 value「1」;「男性」→ ImportCellDictInvalid。
    /// 变异:DictTextResolver.ToLabelAsync 原样回传 value / ToValueAsync 恒返回 null → 本条红。
    /// </summary>
    [Fact]
    public async Task Dict_Bidirectional_ExportLabel_ImportValue_RejectsUnknown()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IRepository<SysUser>>();
            var hasher = sp.GetRequiredService<IPasswordHasher>();
            await repo.InsertAsync(new SysUser
            {
                Account = "dict-export-male",
                Password = hasher.Hash("Dict@Test1"),
                Name = "字典导出男",
                Gender = "1",
                Enabled = true,
                IsSuperAdmin = false,
            });
        }

        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", "Test@123456"));

        // 导出:value→label
        var resp = await client.GetAsync("/api/v1/sys/user/export?Account=dict-export-male&Columns=Account,Gender");
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        await using var exportStream = new MemoryStream(await resp.Content.ReadAsByteArrayAsync());
        var reader = new MiniExcelReader();
        var headers = await reader.ReadHeadersAsync(exportStream);
        Assert.Contains("性别", headers);
        if (exportStream.CanSeek) exportStream.Position = 0;
        var map = new Dictionary<string, string> { ["登录账号"] = "Account", ["性别"] = "Gender" };
        var exportRows = new List<IReadOnlyDictionary<string, string?>>();
        await foreach (var r in reader.ReadRowsAsync(exportStream, map))
            exportRows.Add(r);
        Assert.Contains(exportRows, r => r.GetValueOrDefault("Account") == "dict-export-male"
                                         && r.GetValueOrDefault("Gender") == "男");

        // 导入:label→value
        using var importScope = f.Services.CreateScope();
        var isp = importScope.ServiceProvider;
        var runner = isp.GetRequiredService<IImportRunner>();
        var profile = isp.GetRequiredService<UserImportProfile>();
        var users = isp.GetRequiredService<IRepository<SysUser>>();

        var ok = await runner.CommitAsync(
            [Row(1, "dict-import-male", "字典导入男", gender: "男")],
            profile, DuplicateStrategy.Skip);
        Assert.Equal(1, ok.Inserted);
        var saved = await users.AsQueryable().FirstAsync(u => u.Account == "dict-import-male");
        Assert.Equal("1", saved!.Gender);

        var bad = await runner.CommitAsync(
            [Row(1, "dict-import-bad", "字典导入坏", gender: "男性")],
            profile, DuplicateStrategy.Skip);
        Assert.Equal(0, bad.Inserted);
        Assert.Contains(bad.Failures, r => r.Errors.Any(e => e.Code == ErrorCode.ImportCellDictInvalid));
        Assert.False(await users.AsQueryable().AnyAsync(u => u.Account == "dict-import-bad"));
    }

    // ── 清单 4:按名查外键 ─────────────────────────────────────────────────

    /// <summary>
    /// 机构名「技术部」→ OrgId=3;「不存在的机构」→ ImportCellRefNotFound。
    /// 变异:UserImportProfile.ValidateRowAsync 去掉机构存在性检查 → 本条红。
    /// </summary>
    [Fact]
    public async Task Import_OrgName_ResolvesId_Or_RefNotFound()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        var ok = await runner.CommitAsync(
            [Row(1, "fk-org-ok", "外键机构对", "技术部")],
            profile, DuplicateStrategy.Skip);
        Assert.Equal(1, ok.Inserted);
        var saved = await users.AsQueryable().FirstAsync(u => u.Account == "fk-org-ok");
        Assert.Equal(3, saved!.OrgId); // DefaultOrgSeed: 技术部 Id=3

        var bad = await runner.CommitAsync(
            [Row(1, "fk-org-miss", "外键机构错", "不存在的机构XYZ")],
            profile, DuplicateStrategy.Skip);
        Assert.Equal(0, bad.Inserted);
        Assert.Contains(bad.Failures, r => r.Errors.Any(e => e.Code == ErrorCode.ImportCellRefNotFound));
        Assert.False(await users.AsQueryable().AnyAsync(u => u.Account == "fk-org-miss"));
    }

    // ── 清单 6:不建超管 ───────────────────────────────────────────────────

    /// <summary>
    /// 导入行即便塞 IsSuperAdmin 列,落库用户 IsSuperAdmin 仍为 false(AddUserInput 无此字段 + AddAsync 恒 false)。
    /// 变异:UserService.AddAsync 从某处读入参设 IsSuperAdmin=true → 本条红。
    /// </summary>
    [Fact]
    public async Task Import_CannotCreateSuperAdmin()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        var row = Row(1, "import-no-sadm", "不可超管", extra: new Dictionary<string, string?>
        {
            ["IsSuperAdmin"] = "true",
            ["isSuperAdmin"] = "1",
        });
        var result = await runner.CommitAsync([row], profile, DuplicateStrategy.Skip);
        Assert.Equal(1, result.Inserted);

        var saved = await users.AsQueryable().FirstAsync(u => u.Account == "import-no-sadm");
        Assert.False(saved!.IsSuperAdmin);
    }

    // ── 清单 8:三种重复策略 ───────────────────────────────────────────────

    /// <summary>
    /// 库内已有同 Account 时:Skip→Skipped;Overwrite→Updated 改名;Error→Failed。
    /// 变异:CommitAsync 里 switch(strategy) 三条分支写反/写死 → 计数断言红。
    /// </summary>
    [Fact]
    public async Task Commit_DuplicateStrategies_Skip_Overwrite_Error()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var userSvc = sp.GetRequiredService<IUserService>();

        await userSvc.AddAsync(new AddUserInput
        {
            Account = "dup-strategy", Password = "Dup@123456", Name = "原始名",
            Enabled = true, OrgId = 3, RoleIds = [],
        });

        var skipRow = Row(1, "dup-strategy", "跳过名");
        var skip = await runner.CommitAsync([skipRow], profile, DuplicateStrategy.Skip);
        Assert.Equal(0, skip.Inserted);
        Assert.Equal(1, skip.Skipped);
        Assert.Equal(0, skip.Failed);
        Assert.Equal("原始名", (await users.AsQueryable().FirstAsync(u => u.Account == "dup-strategy"))!.Name);

        var overRow = Row(1, "dup-strategy", "覆盖名");
        var over = await runner.CommitAsync([overRow], profile, DuplicateStrategy.Overwrite);
        Assert.Equal(0, over.Inserted);
        Assert.Equal(1, over.Updated);
        Assert.Equal("覆盖名", (await users.AsQueryable().FirstAsync(u => u.Account == "dup-strategy"))!.Name);

        var errRow = Row(1, "dup-strategy", "错误名");
        var err = await runner.CommitAsync([errRow], profile, DuplicateStrategy.Error);
        Assert.Equal(0, err.Inserted);
        Assert.Equal(0, err.Updated);
        Assert.Equal(1, err.Failed);
        Assert.Equal("覆盖名", (await users.AsQueryable().FirstAsync(u => u.Account == "dup-strategy"))!.Name);
    }

    // ── 清单 9:部分提交 ───────────────────────────────────────────────────

    /// <summary>
    /// 10 行里 3 行有硬错 → Inserted=7、Failed=3,且 7 个账号真能查到。
    /// 变异:CommitAsync 遇错全批中止 / 把硬错当跳过 → 计数或库内行数红。
    /// </summary>
    [Fact]
    public async Task Commit_Partial_SevenOfTen_Inserted()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        var rows = new List<ImportRow>();
        for (var i = 1; i <= 10; i++)
        {
            // 第 2/5/8 行:非法字典,硬错
            var gender = i is 2 or 5 or 8 ? "男性" : "男";
            rows.Add(Row(i, $"partial-{i:D2}", $"部分提交{i}", gender: gender));
        }

        var result = await runner.CommitAsync(rows, profile, DuplicateStrategy.Skip);
        var failDump = string.Join(" | ", result.Failures.Select(r =>
            $"#{r.Index}:{r.Cells.GetValueOrDefault("Account")}:[{string.Join(",", r.Errors.Select(e => e.Code))}]"));
        Assert.True(result.Inserted == 7 && result.Failed == 3,
            $"期望 Inserted=7 Failed=3,实际 Inserted={result.Inserted} Failed={result.Failed} Failures={failDump}");

        var accounts = await users.AsQueryable().ClearFilter<ISoftDelete>()
            .Where(u => u.Account.StartsWith("partial-"))
            .Select(u => u.Account).ToListAsync();
        for (var i = 1; i <= 10; i++)
        {
            var acc = $"partial-{i:D2}";
            var exists = accounts.Contains(acc);
            if (i is 2 or 5 or 8) Assert.False(exists, $"不该入库 {acc}; 库内={string.Join(",", accounts)}");
            else Assert.True(exists, $"应入库 {acc}; 库内={string.Join(",", accounts)}; Failures={failDump}");
        }
    }

    // ── 清单 10:导入行数上限 ─────────────────────────────────────────────

    /// <summary>
    /// MaxImportRows=2 时 Preview 读到第 3 行抛 ImportRowLimitExceeded。
    /// 变异:ImportRunner.PreviewAsync 去掉 index &gt; MaxImportRows 判断 → 本条红。
    /// </summary>
    [Fact]
    public async Task Preview_ExceedsMaxImportRows_Throws()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Excel:MaxImportRows"] = "2",
            },
            Overrides = UseRealExcelCodecs,
        };
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var writer = sp.GetRequiredService<IExcelWriter>();

        await using var xlsx = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "数据",
            Columns =
            [
                new ExportColumn { Key = "Account", Title = "登录账号" },
                new ExportColumn { Key = "Name", Title = "姓名" },
                new ExportColumn { Key = "OrgName", Title = "所属机构" },
            ],
            Rows =
            [
                new Dictionary<string, object?> { ["Account"] = "lim1", ["Name"] = "A", ["OrgName"] = "技术部" },
                new Dictionary<string, object?> { ["Account"] = "lim2", ["Name"] = "B", ["OrgName"] = "技术部" },
                new Dictionary<string, object?> { ["Account"] = "lim3", ["Name"] = "C", ["OrgName"] = "技术部" },
            ],
        });
        if (xlsx.CanSeek) xlsx.Position = 0;

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            runner.PreviewAsync(xlsx, null, profile));
        Assert.Equal(ErrorCode.ImportRowLimitExceeded, ex.Code);
    }

    // ── 清单 11:导出上限 ─────────────────────────────────────────────────

    /// <summary>
    /// MaxExportRows=1 且库内 ≥2 用户 → ExportAsync 抛 ExportRowLimitExceeded。
    /// 变异:UserService.ExportAsync 去掉 count &gt; max 判断 → 本条红。
    /// </summary>
    [Fact]
    public async Task Export_ExceedsMaxExportRows_Throws()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Excel:MaxExportRows"] = "1",
            },
        };
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IRepository<SysUser>>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var hash = hasher.Hash("Lim@Test1");
        // 超管已 1 行;再插 1 行保证 ≥2
        await repo.InsertAsync(new SysUser
        {
            Account = "export-limit-2",
            Password = hash,
            Name = "导出上限二",
            Enabled = true,
            IsSuperAdmin = false,
        });
        var total = await repo.AsQueryable().CountAsync();
        Assert.True(total >= 2, $"前置:用户数应 ≥2,实际 {total}");

        var userService = sp.GetRequiredService<IUserService>();
        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            userService.ExportAsync(new UserPageInput()));
        Assert.Equal(ErrorCode.ExportRowLimitExceeded, ex.Code);
    }

    // ── 清单 14:演示模式 ─────────────────────────────────────────────────

    /// <summary>
    /// DemoMode 开:import/commit 403+41002;export(GET) 仍 200。
    /// 变异:DemoModeFilter 放行全部 POST / 连 GET 也拦 → 本条红。
    /// </summary>
    [Fact]
    public async Task DemoMode_BlocksImportCommit_AllowsExport()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?> { ["TenonAdmin:DemoMode"] = "true" },
            Overrides = UseRealExcelCodecs,
        };
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", "Test@123456"));

        var commit = await client.PostJson("/api/v1/sys/user/import/commit", new
        {
            strategy = 0,
            rows = new[]
            {
                new
                {
                    index = 1,
                    cells = new Dictionary<string, string?>
                    {
                        ["Account"] = "demo-block",
                        ["Name"] = "演示挡",
                        ["OrgName"] = "技术部",
                    },
                    errors = Array.Empty<object>(),
                },
            },
        });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, commit.StatusCode);
        var body = await commit.ReadEnvelope();
        Assert.Equal((int)ErrorCode.DemoModeReadOnly, body.GetProperty("code").GetInt32());

        var export = await client.GetAsync("/api/v1/sys/user/export");
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            export.Content.Headers.ContentType?.MediaType);
    }

    // ── 清单 15:操作日志 ─────────────────────────────────────────────────

    /// <summary>
    /// commit 与 export 后,op/page 能按路径捞到对应条目。
    /// 变异:摘掉 [OperationLog] 或过滤器漏记 GET 导出 → 本条红。
    /// </summary>
    [Fact]
    public async Task CommitAndExport_WriteOperationLogs()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", "Test@123456"));

        var commit = await client.PostJson("/api/v1/sys/user/import/commit", new
        {
            strategy = 0,
            rows = new[]
            {
                new
                {
                    index = 1,
                    cells = new Dictionary<string, string?>
                    {
                        ["Account"] = "oplog-import-u",
                        ["Name"] = "日志导入",
                        ["OrgName"] = "技术部",
                        ["Gender"] = "男",
                    },
                    errors = Array.Empty<object>(),
                },
            },
        });
        Assert.True(commit.IsSuccessStatusCode, await commit.Content.ReadAsStringAsync());
        var commitEnv = await commit.ReadEnvelope();
        Assert.Equal(0, commitEnv.GetProperty("code").GetInt32());
        Assert.Equal(1, commitEnv.GetProperty("data").GetProperty("inserted").GetInt32());

        var export = await client.GetAsync("/api/v1/sys/user/export");
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());

        var byCommit = await (await client.GetAsync(
            "/api/v1/sys/log/op/page?Current=1&Size=50&Path=/api/v1/sys/user/import/commit")).ReadEnvelope();
        Assert.NotEmpty(byCommit.GetProperty("data").GetProperty("items").EnumerateArray());

        var byExport = await (await client.GetAsync(
            "/api/v1/sys/log/op/page?Current=1&Size=50&Path=/api/v1/sys/user/export")).ReadEnvelope();
        Assert.NotEmpty(byExport.GetProperty("data").GetProperty("items").EnumerateArray());
    }
}
