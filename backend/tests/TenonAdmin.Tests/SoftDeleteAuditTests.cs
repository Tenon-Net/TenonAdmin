using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 软删的审计留痕:删也是一次更新,必须记下"谁、什么时候删的"。
/// <para>仓储软删走的是 <c>Updateable&lt;T&gt;().SetColumns(...)</c>(按列更新),而审计 AOP 只挂在
/// <c>UpdateByObject</c>(整对象更新)上——两条路径不重合,所以 UpdateTime/UpdateUserId 必须由仓储自己显式置。
/// 少了这两列:审计上查不到"谁删的",工程上文件回收任务也没有可依据的删除时间戳(见 dev-plan T-D2)。</para>
/// </summary>
public class SoftDeleteAuditTests
{
    [Fact]
    public async Task SoftDelete_StampsUpdateTimeAndUser()
    {
        var id = $"softdel-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");

        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deleted = new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero);
        var clock = new FixedTime(created);

        var services = new ServiceCollection();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton<TimeProvider>(clock);                                       // 前置注册 → 压过内核默认(TryAdd)
        services.AddSingleton<ICurrentUser>(new StubUser(userId: 777));
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = TestDb.DbType, ConnectionString = TestDb.ConnectionString(id, dbFile) },
            [typeof(ServicesSetup).Assembly]);
        services.AddTenonAdminServices();
        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables(typeof(DataScopeTests.ScopeDoc));
        sp.GetRequiredService<IDataScopeContext>().Current = DataScopeResult.Unrestricted;   // 读写都不过滤,便于取回验证

        long docId;
        using (var scope = sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DataScopeTests.ScopeDoc>>();
            await repo.InsertAsync(new DataScopeTests.ScopeDoc { Title = "doomed" });
            docId = (await repo.AsQueryable().Where(d => d.Title == "doomed").FirstAsync()).Id;
        }

        clock.Now = deleted;   // 时钟推到删除那一刻

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepository<DataScopeTests.ScopeDoc>>().DeleteAsync(docId);
        }

        using (var scope = sp.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<IRepository<DataScopeTests.ScopeDoc>>()
                .AsQueryable().ClearFilter<ISoftDelete>().Where(d => d.Id == docId).FirstAsync();

            Assert.True(row.IsDelete);                                                     // 确已软删
            Assert.NotNull(row.UpdateTime);                                                // ← 现在是 null:AOP 不覆盖 SetColumns 路径
            Assert.True(Math.Abs((row.UpdateTime!.Value - deleted.UtcDateTime).TotalSeconds) < 1,
                $"UpdateTime 应是删除那一刻({deleted.UtcDateTime:O}),实际 {row.UpdateTime:O}");
            Assert.Equal(777, row.UpdateUserId);                                           // 谁删的
        }

        TestDb.Cleanup(id, dbFile);
    }

    /// <summary>固定时钟(本地时区取 UTC,断言不受运行机器时区影响)。</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>固定身份的当前用户桩(无机构,只为验证 UpdateUserId 落笔)。</summary>
    private sealed class StubUser(long userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => userId;
        public bool IsSuperAdmin => false;
        public long? OrgId => null;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
