using System.IO.Compression;
using System.Net.Http.Headers;
using System.Xml.Linq;
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

    /// <summary>
    /// 向导真实链路:Preview → 把**返回的行**原样回传 Validate → Commit。
    /// Preview 会把字典 label 就地换成 value 再返回,所以后两步拿到的是 value 而不是 label;
    /// 这一步必须幂等,否则任何带字典列的档案在预览通过后都会在「重新校验」和「提交」上被判 46006。
    /// <para>
    /// 上面那条 <see cref="Dict_Bidirectional_ExportLabel_ImportValue_RejectsUnknown"/> 抓不到这个 ——
    /// 它每次都手工造带 label 的行,永远走不到第二遍。缺陷是浏览器实走向导时发现的。
    /// </para>
    /// 变异:去掉 ImportRunner 里「raw 已是合法字典 value 就幂等接受」那一段 → 本条红(Gender 报 46006)。
    /// </summary>
    [Fact]
    public async Task PreviewRows_FedBackTo_ValidateAndCommit_AreIdempotentOnDictColumns()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var writer = new MiniExcelWriter();

        // 造一份和用户真上传等价的 xlsx:性别写 label「男」
        await using var sheet = await writer.WriteAsync(new ExportSheet
        {
            SheetName = "数据",
            Columns =
            [
                new ExportColumn { Key = "Account", Title = "登录账号" },
                new ExportColumn { Key = "Name", Title = "姓名" },
                new ExportColumn { Key = "Gender", Title = "性别" },
                new ExportColumn { Key = "OrgName", Title = "所属机构" },
            ],
            Rows =
            [
                new Dictionary<string, object?>
                {
                    ["Account"] = "rt-dict-1", ["Name"] = "往返一", ["Gender"] = "男", ["OrgName"] = "技术部",
                },
            ],
        });
        using var upload = new MemoryStream();
        await sheet.CopyToAsync(upload);
        upload.Position = 0;

        var preview = await runner.PreviewAsync(upload, null, profile);
        Assert.Empty(preview.ColumnErrors);
        Assert.Equal(0, preview.ErrorRows);
        // 预览已把 label 换成 value,前端拿到并回传的就是这个
        Assert.Equal("1", preview.Rows[0].Cells["Gender"]);

        // 第二遍:原样回传(前端「重新校验」)
        var again = await runner.ValidateAsync(preview.Rows, profile);
        Assert.Equal(0, again.ErrorRows);
        Assert.DoesNotContain(again.Rows.SelectMany(r => r.Errors), e => e.Code == ErrorCode.ImportCellDictInvalid);

        // 第三遍:提交(坑 6 会再全量校验一次)
        var commit = await runner.CommitAsync(again.Rows, profile, DuplicateStrategy.Skip);
        Assert.Equal(1, commit.Inserted);
        Assert.Equal("1", (await users.AsQueryable().FirstAsync(u => u.Account == "rt-dict-1")).Gender);

        // 幂等不等于放行垃圾:非法值仍要被拦
        var junk = await runner.ValidateAsync(
            [Row(1, "rt-dict-junk", "往返坏", gender: "9")], profile);
        Assert.Contains(junk.Rows.SelectMany(r => r.Errors), e => e.Code == ErrorCode.ImportCellDictInvalid);
    }

    /// <summary>
    /// 判重查询必须分批送进档案:一次塞太多业务键会撞数据库的语句参数上限。
    /// <para>
    /// 档案的常规写法(内核的 <c>UserImportProfile</c> 与消费者指南的样板都是)是
    /// <c>Where(x =&gt; keys.Contains(x.Key))</c> → <c>IN (@p0, @p1, …)</c>,<b>一个键一个参数</b>。
    /// SQL Server 单语句参数上限 2100(硬限)、老版 SQLite 999,而 <c>MaxImportRows</c> 默认 5000 ——
    /// 不分批的话在 SqlServer 上导入两千多行就必然抛异常,且默认配置主动允许到五千行。
    /// 分批放在 Runner 而非各档案,消费者照抄的档案才能一并免疫。
    /// </para>
    /// 变异:把 <c>FindExistingKeysBatchedAsync</c> 换回直接 <c>profile.FindExistingKeysAsync(keys, …)</c>
    /// → 单批 1200 &gt; 500 → 本条红。
    /// </summary>
    [Fact]
    public async Task ExistingKeyLookup_IsBatched_BelowDatabaseParameterLimit()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
        using var scope = f.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IImportRunner>();

        // 三个「库里已存在」的键,刻意分落在第 1、2、3 批,用来钉住合并结果没丢
        var profile = new BatchProbeProfile(
            new HashSet<string>(StringComparer.Ordinal) { "k0003", "k0600", "k1150" });
        var rows = Enumerable.Range(1, 1200)
            .Select(i => new ImportRow
            {
                Index = i,
                Cells = new Dictionary<string, string?> { ["K"] = $"k{i:D4}" },
                Errors = [],
            })
            .ToList();

        var preview = await runner.ValidateAsync(rows, profile);

        Assert.True(profile.BatchSizes.Count > 1,
            $"1200 个键必须分多批送进档案,实际只调了 {profile.BatchSizes.Count} 次");
        Assert.All(profile.BatchSizes, n => Assert.True(n <= 500,
            $"单批 {n} 个键超过 500,会撞 SqlServer 的 2100 参数上限"));
        Assert.Equal(1200, profile.BatchSizes.Sum());

        // 合并结果:三批里各自命中的都要算上,一个都不能漏
        var dup = preview.Rows
            .Where(r => r.Errors.Any(e => e.Code == ErrorCode.ImportDuplicateInDb))
            .Select(r => r.Cells["K"]!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new HashSet<string> { "k0003", "k0600", "k1150" }, dup);
    }

    /// <summary>只为上面那条服务:记录每次被送进来多少个键,并按预置集合报「已存在」。</summary>
    private sealed class BatchProbeProfile(IReadOnlySet<string> existing) : IImportProfile
    {
        public List<int> BatchSizes { get; } = [];

        public string Code => "batch-probe";

        public IReadOnlyList<string> BusinessKeys { get; } = ["K"];

        public IReadOnlyList<ImportColumn> Columns { get; } =
        [
            new() { Key = "K", Title = "键", Required = true },
        ];

        public Task<IReadOnlyList<CellError>> ValidateRowAsync(
            ImportRow row, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CellError>>([]);

        public Task<IReadOnlySet<string>> FindExistingKeysAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(keys.Count);
            return Task.FromResult<IReadOnlySet<string>>(
                keys.Where(existing.Contains).ToHashSet(StringComparer.Ordinal));
        }

        public Task CommitRowAsync(ImportRow row, bool overwrite, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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

    /// <summary>
    /// MaxImportRows 对 Validate/Commit 的 JSON 入口同样生效(不经 xlsx 流)。
    /// 变异:只在 PreviewAsync 数行、Validate/Commit 不调 EnsureWithinRowLimit → 本条红。
    /// </summary>
    [Fact]
    public async Task ValidateAndCommit_ExceedsMaxImportRows_Throws()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["TenonAdmin:Excel:MaxImportRows"] = "2",
            },
        };
        using var scope = f.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IImportRunner>();
        var profile = scope.ServiceProvider.GetRequiredService<UserImportProfile>();
        var tooMany = new[] { Row(1, "lim-a", "A"), Row(2, "lim-b", "B"), Row(3, "lim-c", "C") };

        var validateEx = await Assert.ThrowsAsync<AdminException>(() =>
            runner.ValidateAsync(tooMany, profile));
        Assert.Equal(ErrorCode.ImportRowLimitExceeded, validateEx.Code);

        var commitEx = await Assert.ThrowsAsync<AdminException>(() =>
            runner.CommitAsync(tooMany, profile, DuplicateStrategy.Skip));
        Assert.Equal(ErrorCode.ImportRowLimitExceeded, commitEx.Code);
    }

    /// <summary>
    /// 覆盖导入且 RoleNames 留空:不得把已有角色清掉(UpdateAsync 全量重设,空列表=清空)。
    /// 变异:CommitRowAsync 覆盖分支把 roleIds 默认 [] 传进 UpdateAsync → 本条红。
    /// </summary>
    [Fact]
    public async Task Overwrite_BlankRoleNames_KeepsExistingRoles()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var userSvc = sp.GetRequiredService<IUserService>();
        var rbac = sp.GetRequiredService<IRbacService>();
        const long seedRoleId = 2; // 全部数据

        var created = await userSvc.AddAsync(new AddUserInput
        {
            Account = "keep-roles", Password = "Keep@123456", Name = "原名",
            Enabled = true, OrgId = 3, RoleIds = [seedRoleId],
        });

        var over = await runner.CommitAsync(
            [Row(1, "keep-roles", "覆盖后名")],
            profile, DuplicateStrategy.Overwrite);
        Assert.Equal(1, over.Updated);

        var kept = await rbac.GetUserRoleIdsAsync(created.Id);
        Assert.Contains(seedRoleId, kept);
        Assert.Equal("覆盖后名",
            (await sp.GetRequiredService<IRepository<SysUser>>()
                .GetFirstAsync(u => u.Account == "keep-roles"))!.Name);
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

    // ── §12 第 11 轮:启用状态是闭合二态,模板/向导都该出下拉 ────────────────

    /// <summary>
    /// 用户导入模板里「启用状态」列必须带下拉,候选值是 common_status 的两个 label。
    /// 表单里这个字段是开关,导入却给自由文本框,是口径不一致(用户实测反馈)。
    /// 变异:把 UserImportProfile 的 Enabled 列上的 DictTypeCode 去掉 → 该列不再有
    /// dataValidation → 本条必须红(性别那列的下拉在别的列号上,兜不住)。
    /// </summary>
    [Fact]
    public async Task ImportTemplate_EnabledColumn_HasCommonStatusDropdown()
    {
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken("superAdmin", "Test@123456"));

        var resp = await client.GetAsync("/api/v1/sys/user/import/template");
        Assert.True(resp.IsSuccessStatusCode,
            $"template HTTP {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // 1) 在带表头的那张 sheet 上定位「启用状态」的列字母(不假设列顺序)
        string? enabledLetter = null;
        XDocument? dataSheet = null;
        foreach (var entry in zip.Entries.Where(e =>
                     e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var es = entry.Open();
            var doc = XDocument.Load(es);
            var header = doc.Descendants(main + "row")
                .FirstOrDefault(r => (string?)r.Attribute("r") == "1");
            var cell = header?.Descendants(main + "c").FirstOrDefault(c =>
                c.Descendants(main + "t").Any(t => t.Value == "启用状态"));
            if (cell is null) continue;

            enabledLetter = new string(((string?)cell.Attribute("r") ?? "")
                .TakeWhile(char.IsLetter).ToArray());
            dataSheet = doc;
            break;
        }

        Assert.False(string.IsNullOrEmpty(enabledLetter), "模板表头里没找到「启用状态」列");

        // 2) 该列上必须挂着 dataValidation(sqref 形如 K2:K1001)
        var dv = dataSheet!.Descendants(main + "dataValidation").FirstOrDefault(v =>
            ((string?)v.Attribute("sqref") ?? "").StartsWith($"{enabledLetter}", StringComparison.Ordinal));
        Assert.True(dv is not null,
            $"「启用状态」({enabledLetter} 列)没有 dataValidation —— 模板里它还是自由文本框");

        // 3) 候选值确实是 common_status 的两个 label
        var allText = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e =>
            {
                using var es = e.Open();
                return XDocument.Load(es).Descendants(main + "t").Select(t => t.Value).ToList();
            })
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("启用", allText);
        Assert.Contains("停用", allText);
    }

    /// <summary>
    /// 启用状态挂上字典后整条链要真通:label「停用」→ value「0」→ ParseEnabled → Enabled=false。
    /// <para>
    /// 光看落库结果不够:ParseEnabled 本来就认「停用」,去掉字典也会绿(假绿)。所以先在
    /// ValidateAsync 的返回行上断言 label 已被换成 "1"/"0" —— 这才是「真走了字典」的证据。
    /// (Commit/Validate 都会深拷贝入参,坑 6 的防篡改设计,所以只能从返回值上看。)
    /// </para>
    /// 变异:去掉 UserImportProfile 里 Enabled 列的 DictTypeCode → 单元格仍是「启用」→ 本条必须红。
    /// </summary>
    [Fact]
    public async Task Commit_EnabledLabel_TranslatedToDictValue_ThenParsed()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var runner = sp.GetRequiredService<IImportRunner>();
        var profile = sp.GetRequiredService<UserImportProfile>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        var on = Row(1, "import-enabled-on", "启用用户", extra: new() { ["Enabled"] = "启用" });
        var off = Row(2, "import-enabled-off", "停用用户", extra: new() { ["Enabled"] = "停用" });

        var preview = await runner.ValidateAsync([on, off], profile);
        Assert.All(preview.Rows, r => Assert.Empty(r.Errors));
        Assert.Equal("1", preview.Rows[0].Cells["Enabled"]);
        Assert.Equal("0", preview.Rows[1].Cells["Enabled"]);

        var result = await runner.CommitAsync([on, off], profile, DuplicateStrategy.Error);
        Assert.Equal(2, result.Inserted);

        var uOn = await users.GetFirstAsync(u => u.Account == "import-enabled-on");
        var uOff = await users.GetFirstAsync(u => u.Account == "import-enabled-off");
        Assert.True(uOn!.Enabled);
        Assert.False(uOff!.Enabled);
    }
}
