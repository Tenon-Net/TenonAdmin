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
        var id = $"scope-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        var services = new ServiceCollection();
        services.AddSingleton(new AdminCacheOptions());
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(id, dbFile) },
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

        TestDb.Cleanup(id, dbFile);
    }

    [Fact]
    public async Task CreateOrgId_is_filled_from_current_user_org_on_insert()
    {
        // P1-12:经仓储插入 DataEntity 时,CreateOrgId 由审计 AOP 从 ICurrentUser.OrgId 回填(否则机构数据范围恒 0 行)
        var id = $"orgfill-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        var services = new ServiceCollection();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton<ICurrentUser>(new StubCurrentUser(userId: 100, orgId: 55));   // 前置注册 → 压过 SystemCurrentUser(TryAdd)
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(id, dbFile) },
            [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        await using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(ScopeDoc));

        sp.GetRequiredService<IDataScopeContext>().Current = DataScopeResult.Unrestricted;   // 读写都不过滤,便于取回验证

        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<ScopeDoc>>();
            await repo.InsertAsync(new ScopeDoc { Title = "X" });    // 不显式设 CreateOrgId
            var saved = await repo.AsQueryable().Where(d => d.Title == "X").FirstAsync();
            Assert.Equal(55, saved.CreateOrgId);     // 从 currentUser.OrgId 回填
            Assert.Equal(100, saved.CreateUserId);   // 顺带验证 CreateUserId 回填
        }

        TestDb.Cleanup(id, dbFile);
    }

    /// <summary>临时业务实体:继承 DataEntity 即自动进入全局数据范围过滤(CreateOrgId 锚点)。</summary>
    [SugarTable("scope_doc")]
    public class ScopeDoc : DataEntity
    {
        [SugarColumn(Length = 64)] public string Title { get; set; } = "";
    }

    /// <summary>固定身份的当前用户桩(带归属机构),用于验证审计 AOP 从 OrgId/UserId 回填。</summary>
    private sealed class StubCurrentUser(long userId, long orgId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => userId;
        public bool IsSuperAdmin => false;
        public long? OrgId => orgId;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
