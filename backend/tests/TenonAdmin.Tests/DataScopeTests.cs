using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 多机构数据范围(§8 覆盖点 2 + 命名用例)——直压 SqlSugar 全局过滤器:切换生效范围,同一列表得不同数据集。
/// 数据层测最严谨(等价"两机构用户查同一接口得不同数据");由 scratchpad t3-datascope-check 转正。
/// </summary>
public class DataScopeTests
{
    [Fact]
    public async Task DataScope_ShouldFilterByCurrentUserOrg()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-scope-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddSingleton(new AdminCacheOptions());
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = $"Data Source={dbFile}" },
            [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(ScopeDoc));

        // 装 4 行(显式 CreateOrgId/CreateUserId;插入时上下文不受限,不被过滤)
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepository<ScopeDoc>>().InsertRangeAsync(
            [
                new() { Title = "A", CreateOrgId = 10, CreateUserId = 100 },
                new() { Title = "B", CreateOrgId = 11, CreateUserId = 101 },
                new() { Title = "C", CreateOrgId = 20, CreateUserId = 102 },
                new() { Title = "D", CreateOrgId = null, CreateUserId = 101 },
            ]);
        }

        var ctx = sp.GetRequiredService<IDataScopeContext>();
        async Task<int> Count()
        {
            using var scope = sp.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IRepository<ScopeDoc>>().AsQueryable().CountAsync();
        }

        ctx.Current = DataScopeResult.Unrestricted;
        Assert.Equal(4, await Count());                                            // 不受限 → 全部

        ctx.Current = DataScopeResult.Restricted([10], includeSelf: false, userId: 0);
        Assert.Equal(1, await Count());                                            // 本机构{10} → A

        ctx.Current = DataScopeResult.Restricted([10, 20], includeSelf: false, userId: 0);
        Assert.Equal(2, await Count());                                            // {10,20} → A+C

        ctx.Current = DataScopeResult.Restricted([], includeSelf: true, userId: 101);
        Assert.Equal(2, await Count());                                            // 仅本人(101)→ B+D

        ctx.Current = DataScopeResult.Restricted([], includeSelf: false, userId: 0);
        Assert.Equal(0, await Count());                                            // 空范围 → 默认拒绝

        // 招牌:两不同机构用户查同一列表得不同数据集(各见 1 行,互不相同)
        ctx.Current = DataScopeResult.Restricted([11], false, 0);
        Assert.Equal(1, await Count());
        ctx.Current = DataScopeResult.Restricted([20], false, 0);
        Assert.Equal(1, await Count());

        try { File.Delete(dbFile); } catch { /* 尽力而为 */ }
    }

    /// <summary>临时业务实体:继承 DataEntity 即自动进入全局数据范围过滤(CreateOrgId 锚点)。</summary>
    [SugarTable("scope_doc")]
    public class ScopeDoc : DataEntity
    {
        [SugarColumn(Length = 64)] public string Title { get; set; } = "";
    }
}
