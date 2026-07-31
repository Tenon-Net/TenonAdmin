using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// OrgAuditEntity(#10)——机构范围 + 审计 + <b>物理删除</b>、被内置仓储托管的实体。验证:
/// 审计/CreateOrgId 自动填充按接口匹配对不软删实体同样生效;无 IsDelete 列、不受软删过滤;数据范围过滤与越权写守卫照旧;
/// 仓储 DeleteAsync 对它是物理删;RestoreAsync 拒绝。
/// </summary>
public class OrgAuditEntityTests
{
    private static ServiceProvider BuildSp(string id, string dbFile, ICurrentUser? currentUser = null, params Type[] entities)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AdminCacheOptions());
        if (currentUser is not null)
            services.AddSingleton(currentUser);   // 前置注册 → 压过 SystemCurrentUser(TryAdd)
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(id, dbFile) },
            [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(entities.Length > 0 ? entities : [typeof(FooOrgAudit)]);
        return sp;
    }

    [Fact]
    public async Task Insert_fills_id_audit_and_org_and_row_is_visible_without_softdelete_filter()
    {
        var id = $"orgaudit-fill-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        await using var sp = BuildSp(id, dbFile, new StubCurrentUser(userId: 100, orgId: 55));

        // 该实体无 IsDelete 列(不实现 ISoftDelete)——直接由类型映射证明,防误加回软删列。
        var cols = sp.GetRequiredService<ISqlSugarClient>().EntityMaintenance.GetEntityInfo<FooOrgAudit>()
            .Columns.Select(c => c.PropertyName);
        Assert.DoesNotContain(nameof(ISoftDelete.IsDelete), cols);

        sp.GetRequiredService<IDataScopeContext>().Current = DataScopeResult.Unrestricted;   // 读写都不过滤,便于取回

        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>();
        await repo.InsertAsync(new FooOrgAudit { Title = "X" });   // 不显式设 Id/审计/CreateOrgId

        // 无需 ClearFilter<ISoftDelete>():软删全局过滤器只作用于 ISoftDelete 实体,对它天然不生效,裸查即见。
        var saved = await repo.AsQueryable().Where(d => d.Title == "X").FirstAsync();
        Assert.NotNull(saved);
        Assert.True(saved!.Id >= TenonSeedIds.SnowflakeFloor);   // 雪花 Id 由 AOP 按 PrimaryId 回填
        Assert.NotEqual(default, saved.CreateTime);              // CreateTime 按 AuditEntity 回填
        Assert.Equal(100, saved.CreateUserId);                  // CreateUserId 按 AuditEntity 回填
        Assert.Equal(55, saved.CreateOrgId);                    // CreateOrgId 按 IOrgScoped 回填(非软删机构实体照样填)

        TestDb.Cleanup(id, dbFile);
    }

    [Fact]
    public async Task DataScope_filter_still_applies_to_non_softdelete_org_entity()
    {
        var id = $"orgaudit-scope-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        await using var sp = BuildSp(id, dbFile);

        var ctx = sp.GetRequiredService<IDataScopeContext>();
        ctx.Current = DataScopeResult.Unrestricted;
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>().InsertRangeAsync(
            [
                new() { Title = "A", CreateOrgId = 10, CreateUserId = 100 },
                new() { Title = "B", CreateOrgId = 20, CreateUserId = 200 },
            ]);
        }

        async Task<int> Count()
        {
            using var scope = sp.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>().AsQueryable().CountAsync();
        }

        ctx.Current = DataScopeResult.Unrestricted;                              Assert.Equal(2, await Count());
        ctx.Current = DataScopeResult.Restricted([10], includeSelf: false, 0);   Assert.Equal(1, await Count());  // 机构{10} → A
        ctx.Current = DataScopeResult.Restricted([99], includeSelf: false, 0);   Assert.Equal(0, await Count());  // 无匹配机构 → 0

        TestDb.Cleanup(id, dbFile);
    }

    [Fact]
    public async Task Delete_is_physical_and_scope_guarded()
    {
        var id = $"orgaudit-del-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        await using var sp = BuildSp(id, dbFile);

        var ctx = sp.GetRequiredService<IDataScopeContext>();
        ctx.Current = DataScopeResult.Unrestricted;
        long org10Id, org20Id;
        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>();
            var a = new FooOrgAudit { Title = "A", CreateOrgId = 10, CreateUserId = 100 };
            var b = new FooOrgAudit { Title = "B", CreateOrgId = 20, CreateUserId = 200 };
            await repo.InsertAsync(a);
            await repo.InsertAsync(b);
            org10Id = a.Id;
            org20Id = b.Id;
        }

        // 越权写守卫仍在(IOrgScoped 未改):仅机构 10 时改/删机构 20 的行都被拒(OrgAuditEntity 文档承诺 Update+Delete 均受守卫)
        ctx.Current = DataScopeResult.Restricted([10], includeSelf: false, 0);
        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>();
            Assert.Equal(0, await repo.UpdateAsync(new FooOrgAudit { Id = org20Id, Title = "hacked" }));   // 越权改 → 守卫拒绝
            Assert.Equal(0, await repo.DeleteAsync(org20Id));   // 越权删 → 守卫拒绝
            Assert.Equal(1, await repo.DeleteAsync(org10Id));   // 本机构 → 删除
        }

        // 复核:机构 20 行完好(未被越权改/删);机构 10 行被【物理删】——即便清掉软删过滤器也查不到(对照 BaseEntity 软删后仍可 ClearFilter 查到)
        ctx.Current = DataScopeResult.Unrestricted;
        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>();
            var b = await repo.AsQueryable().Where(d => d.Id == org20Id).FirstAsync();
            Assert.NotNull(b);
            Assert.Equal("B", b!.Title);   // 越权改被拒,标题未变
            Assert.Null(await repo.AsQueryable().ClearFilter<ISoftDelete>().Where(d => d.Id == org10Id).FirstAsync());
        }

        TestDb.Cleanup(id, dbFile);
    }

    [Fact]
    public async Task Plain_AuditEntity_physical_delete_fills_audit_and_frees_unique_constraint()
    {
        // 非机构审计实体走物理删分支(InScopeAsync 对非 IOrgScoped 恒真短路),且物理删不做 _del_{id} 占位释放。
        var id = $"audit-uniq-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        await using var sp = BuildSp(id, dbFile, new StubCurrentUser(userId: 100, orgId: 55), typeof(FooAudit));

        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooAudit>>();

        var row = new FooAudit { Code = "u1" };
        await repo.InsertAsync(row);

        var saved = await repo.GetByIdAsync(row.Id);
        Assert.NotNull(saved);
        Assert.True(saved!.Id >= TenonSeedIds.SnowflakeFloor);   // 雪花 Id 回填
        Assert.NotEqual(default, saved.CreateTime);              // 审计回填(非机构实体照样填)
        Assert.Equal(100, saved.CreateUserId);

        // 物理删:行消失,唯一约束立即释放 → 同一唯一值可再插(软删则会因 _del_ 占位/撞唯一索引而不同)
        Assert.Equal(1, await repo.DeleteAsync(row.Id));
        Assert.Null(await repo.GetByIdAsync(row.Id));
        await repo.InsertAsync(new FooAudit { Code = "u1" });    // 同值重插:物理删已释放唯一约束 → 成功
        Assert.Equal(1, await repo.AsQueryable().CountAsync());

        TestDb.Cleanup(id, dbFile);
    }

    [Fact]
    public async Task Restore_throws_NotSupported_for_non_softdelete_entity()
    {
        var id = $"orgaudit-restore-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");
        await using var sp = BuildSp(id, dbFile);
        sp.GetRequiredService<IDataScopeContext>().Current = DataScopeResult.Unrestricted;

        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FooOrgAudit>>();
        await Assert.ThrowsAsync<NotSupportedException>(() => repo.RestoreAsync(1));

        TestDb.Cleanup(id, dbFile);
    }

    /// <summary>临时业务实体:继承 OrgAuditEntity → 机构数据范围 + 审计,但物理删、无软删列。</summary>
    [SugarTable("foo_org_audit")]
    public class FooOrgAudit : OrgAuditEntity
    {
        [SugarColumn(Length = 64)] public string Title { get; set; } = "";
    }

    /// <summary>临时业务实体:继承 AuditEntity → 审计 + 物理删、非机构、带唯一列(验证物理删释放唯一约束)。</summary>
    [SugarTable("foo_audit")]
    [SugarIndex("idx_foo_audit_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
    public class FooAudit : AuditEntity
    {
        [SugarColumn(Length = 64)] public string Code { get; set; } = "";
    }

    private sealed class StubCurrentUser(long userId, long orgId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => userId;
        public string? SessionId => null;
        public bool IsSuperAdmin => false;
        public long? OrgId => orgId;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
