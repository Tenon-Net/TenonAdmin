using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;
using TenonAdmin.Excel;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;
using TenonAdmin.TestHost;

namespace TenonAdmin.Tests;

/// <summary>
/// 导入导出 × 数据范围(excel-ledger §9 G5 清单 5 / 12)。
/// 清单 12 是本批最重要测试:三个真实账号、不同数据范围、导出同一业务列表得不同行集。
/// <para>
/// 注:sys_user 继承 BaseEntity 不走 IOrgScoped 全局过滤(rebuild-design §6);
/// 招牌能力挂在 DataEntity 业务表上 —— 用 TestHost 的 <see cref="SampleDoc"/> 作被导列表,
/// 经真实登录管道解析范围后写出 xlsx,禁止 mock 数据范围。
/// </para>
/// </summary>
public class ImportExportScopeTests
{
    private static HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static void UseRealExcelCodecs(IServiceCollection s)
    {
        s.Replace(ServiceDescriptor.Singleton<IExcelReader, MiniExcelReader>());
        s.Replace(ServiceDescriptor.Singleton<IExcelWriter, MiniExcelWriter>());
        s.Replace(ServiceDescriptor.Singleton<IExcelTemplateBuilder, OpenXmlTemplateBuilder>());
    }

    /// <summary>
    /// 清单 5:数据范围受限账号导入范围外机构名 → ImportOrgOutOfScope,库无新行。
    /// 变异:UserImportProfile.IsOrgInScope 恒 true / 去掉 ImportOrgOutOfScope 分支 → 本条红。
    /// </summary>
    [Fact]
    public async Task Import_OrgOutOfScope_Rejected_And_NotInserted()
    {
        const long orgTech = 3;   // 技术部
        const string password = "Scope@123456";
        using var f = new AdminAppFactory();

        string account;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var menus = sp.GetRequiredService<IRepository<SysMenu>>();
            var commitMenu = new SysMenu
            {
                ParentId = 15, Type = MenuType.Button, Title = "测试-导入提交",
                Permission = "POST:/api/v1/sys/user/import/commit", Enabled = true, Visible = true,
            };
            await menus.InsertAsync(commitMenu);

            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var role = new SysRole
            {
                Name = "导入范围角色", Code = "imp-scope-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true,
            };
            await roles.InsertAsync(role);
            await rbac.SetRoleMenusAsync(role.Id, [commitMenu.Id]);
            await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org); // 仅本机构=技术部

            account = "imp-scope-" + Guid.CreateVersion7().ToString("N")[..8];
            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account, Password = password, Name = "导入范围用户",
                Enabled = true, OrgId = orgTech, RoleIds = [role.Id],
            });
        }

        var c = f.CreateClient();
        WithToken(c, await c.LoginToken(account, password));

        // 行填范围外机构「人事部」(Id=7),当前用户范围只有技术部(3)
        var resp = await c.PostJson("/api/v1/sys/user/import/commit", new
        {
            strategy = 0,
            rows = new[]
            {
                new
                {
                    index = 1,
                    cells = new Dictionary<string, string?>
                    {
                        ["Account"] = "out-of-scope-user",
                        ["Name"] = "越权机构用户",
                        ["OrgName"] = "人事部",
                        ["Gender"] = "男",
                    },
                    errors = Array.Empty<object>(),
                },
            },
        });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var env = await resp.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        var data = env.GetProperty("data");
        Assert.Equal(0, data.GetProperty("inserted").GetInt32());
        Assert.True(data.GetProperty("failed").GetInt32() >= 1);

        var failures = data.GetProperty("failures").EnumerateArray().ToList();
        Assert.NotEmpty(failures);
        Assert.Contains(failures, row =>
            row.GetProperty("errors").EnumerateArray()
                .Any(e => e.GetProperty("code").GetInt32() == (int)ErrorCode.ImportOrgOutOfScope));

        using var check = f.Services.CreateScope();
        var exists = await check.ServiceProvider.GetRequiredService<IRepository<SysUser>>()
            .AsQueryable().ClearFilter<ISoftDelete>()
            .AnyAsync(u => u.Account == "out-of-scope-user");
        Assert.False(exists, "越权机构行不得落库");
    }

    /// <summary>
    /// 清单 12(招牌):三个不同数据范围真实账号,各打同一个**导出端点** → 行数不同且互不可见对方行。
    /// <para>
    /// 走 SampleDoc(DataEntity)的 <c>GET /api/v1/sample/doc/export</c>:真登录 → 真鉴权 → 真解析数据范围
    /// → 服务端自己查库写 xlsx。断言的行集是**从返回的 xlsx 里读回来的**,不是从列表端点抄的 ——
    /// 否则测的是列表过滤,导出链路整条没被覆盖(早期版本正是如此,改坏导出仍绿)。
    /// sys_user 继承 BaseEntity 不受 IOrgScoped 约束,故这条只能由业务表来钉。
    /// </para>
    /// 变异:①SqlSugar 全局 IOrgScoped 过滤器恒 true / 写死 Unrestricted;
    /// ②<c>SampleDocController.Export</c> 不走 <c>ListAsync</c> 而另起一条查询或 <c>ClearFilter&lt;IOrgScoped&gt;()</c>
    /// —— 任一都让三账号行数相同 → 本条红。
    /// </summary>
    [Fact]
    public async Task Export_ThreeAccounts_DifferentDataScopes_SeeDifferentRows()
    {
        // 种子机构:4=前端组, 5=后端组, 6=产品部
        const long orgFe = 4, orgBe = 5, orgPm = 6;
        const string password = "Scope@123456";
        using var f = new AdminAppFactory { Overrides = UseRealExcelCodecs };

        string accFe, accBe, accPm;
        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;

            // 授权:列表 + 导出两个权限码(导出端点自身也是 [RolePermission])
            var menus = sp.GetRequiredService<IRepository<SysMenu>>();
            var listMenu = new SysMenu
            {
                ParentId = 1, Type = MenuType.Button, Title = "示例文档-列表",
                Permission = "GET:/api/v1/sample/doc", Enabled = true, Visible = true,
            };
            await menus.InsertAsync(listMenu);
            var exportMenu = new SysMenu
            {
                ParentId = 1, Type = MenuType.Button, Title = "示例文档-导出",
                Permission = "GET:/api/v1/sample/doc/export", Enabled = true, Visible = true,
            };
            await menus.InsertAsync(exportMenu);

            var roles = sp.GetRequiredService<IRepository<SysRole>>();
            var rbac = sp.GetRequiredService<IRbacService>();
            var users = sp.GetRequiredService<IUserService>();

            async Task<(string account, long roleId)> SeedScopedUser(string prefix, long orgId, DataScopeType scopeType)
            {
                var role = new SysRole
                {
                    Name = $"{prefix}角色", Code = prefix + "-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true,
                };
                await roles.InsertAsync(role);
                await rbac.SetRoleMenusAsync(role.Id, [listMenu.Id, exportMenu.Id]);
                await rbac.SetRoleDataScopeAsync(role.Id, scopeType);
                var account = prefix + "-" + Guid.CreateVersion7().ToString("N")[..8];
                await users.AddAsync(new AddUserInput
                {
                    Account = account, Password = password, Name = prefix,
                    Enabled = true, OrgId = orgId, RoleIds = [role.Id],
                });
                return (account, role.Id);
            }

            // 三账号:本机构前端组 / 本机构后端组 / 自定义(前端+后端)
            (accFe, _) = await SeedScopedUser("exp-fe", orgFe, DataScopeType.Org);
            (accBe, _) = await SeedScopedUser("exp-be", orgBe, DataScopeType.Org);
            (accPm, _) = await SeedScopedUser("exp-pm", orgPm, DataScopeType.Custom);
            // Custom 角色需指定机构集:前端组+产品部(与 fe/be 部分重叠、互不等)
            var pmRoleId = await roles.AsQueryable()
                .Where(r => r.Code.StartsWith("exp-pm-"))
                .Select(r => r.Id).FirstAsync();
            // 上面 Seed 已设 Custom 但 customOrgIds 空 → 实际可见空;重设为 前端组+产品部
            await rbac.SetRoleDataScopeAsync(pmRoleId, DataScopeType.Custom, [orgFe, orgPm]);

            // 业务数据:前端组 3 行、后端组 2 行、产品部 1 行(显式 CreateOrgId,后台不受限上下文)
            var docs = sp.GetRequiredService<IRepository<SampleDoc>>();
            await docs.InsertRangeAsync(
            [
                new SampleDoc { Title = "FE-1", CreateOrgId = orgFe },
                new SampleDoc { Title = "FE-2", CreateOrgId = orgFe },
                new SampleDoc { Title = "FE-3", CreateOrgId = orgFe },
                new SampleDoc { Title = "BE-1", CreateOrgId = orgBe },
                new SampleDoc { Title = "BE-2", CreateOrgId = orgBe },
                new SampleDoc { Title = "PM-1", CreateOrgId = orgPm },
            ]);
        }

        // 失效数据范围缓存(改了 role scope 后必须清,否则 pm 可能仍是旧空集)
        using (var scope = f.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();
            // 清所有用户 scope 缓存:用 CacheAdmin 或按 key 模式——这里按三账号 id 查后清
            var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            foreach (var acc in new[] { accFe, accBe, accPm })
            {
                var uid = await userRepo.AsQueryable().Where(u => u.Account == acc).Select(u => u.Id).FirstAsync();
                await cache.RemoveAsync(CacheKeys.UserDataScope(uid));
            }
        }

        async Task<(int count, HashSet<string> titles)> ExportAs(string account)
        {
            var c = f.CreateClient();
            WithToken(c, await c.LoginToken(account, password));

            // 真打导出端点:服务端在该账号的数据范围下自己查库、自己写 xlsx
            var resp = await c.GetAsync("/api/v1/sample/doc/export");
            Assert.True(resp.IsSuccessStatusCode, $"{account} 导出应 200,实际 {(int)resp.StatusCode}");
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                resp.Content.Headers.ContentType?.MediaType);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K',
                "导出必须是 xlsx 流,不是 JSON 信封(§5.2)");

            // 从返回的 xlsx 里读回行 —— 断言对象是导出文件的真实内容
            using var scope = f.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IExcelReader>();
            using var ms = new MemoryStream(bytes);
            var titles = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var row in reader.ReadRowsAsync(ms, new Dictionary<string, string> { ["标题"] = "Title" }))
            {
                if (row.TryGetValue("Title", out var t) && !string.IsNullOrWhiteSpace(t))
                    titles.Add(t);
            }
            return (titles.Count, titles);
        }

        var fe = await ExportAs(accFe);
        var be = await ExportAs(accBe);
        var pm = await ExportAs(accPm);

        // 三账号行数互不相同
        Assert.Equal(3, fe.count); // 仅前端组
        Assert.Equal(2, be.count); // 仅后端组
        Assert.Equal(4, pm.count); // 前端组 3 + 产品部 1
        Assert.NotEqual(fe.count, be.count);
        Assert.NotEqual(fe.count, pm.count);
        Assert.NotEqual(be.count, pm.count);

        // 互不可见对方独有行
        Assert.DoesNotContain("BE-1", fe.titles);
        Assert.DoesNotContain("PM-1", fe.titles);
        Assert.DoesNotContain("FE-1", be.titles);
        Assert.DoesNotContain("PM-1", be.titles);
        Assert.Contains("FE-1", pm.titles);
        Assert.Contains("PM-1", pm.titles);
        Assert.DoesNotContain("BE-1", pm.titles); // 自定义范围不含后端组
    }
}
