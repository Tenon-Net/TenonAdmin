using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 多 ConfigId 装配开口(issue #28 / G7 门禁 + 独立 review 补测):
/// 双库隔离、钩子默认/opt-in、校验 fail-fast、SQLite Ensure、OnError、IRepository 仍主库。
/// </summary>
public class MultiConfigIdTests
{
    private const string SideConfigId = "Side";

    /// <summary>主/副库共用同一表名——隔离断言必须靠「同表不同库」,不能靠表名差异假绿。</summary>
    [SugarTable("mc_shared_row")]
    private sealed class SharedRow : BaseEntity
    {
        [SugarColumn(Length = 64)]
        public string Name { get; set; } = "";
    }

    /// <summary>副库专用实体(软删/审计用例,表名可与主库不同)。</summary>
    [SugarTable("mc_side_row")]
    private sealed class SideRow : BaseEntity
    {
        [SugarColumn(Length = 64)]
        public string Name { get; set; } = "";
    }

    /// <summary>副库机构范围实体(IOrgScoped)。</summary>
    [SugarTable("mc_scope_side")]
    private sealed class ScopeSideRow : DataEntity
    {
        [SugarColumn(Length = 64)]
        public string Title { get; set; } = "";
    }

    /// <summary>主库上的同构实体,证明 IRepository 只打主库。</summary>
    [SugarTable("mc_repo_row")]
    private sealed class RepoRow : BaseEntity
    {
        [SugarColumn(Length = 64)]
        public string Name { get; set; } = "";
    }

    private static (ServiceProvider Sp, string MainFile, string SideFile, string Id) BuildDual(
        AdminDatabaseConnectionOptions sideOptions,
        Action<IServiceCollection>? configure = null)
    {
        var id = $"mc-{Guid.NewGuid():N}";
        var mainFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}-main.db");
        var sideFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}-side.db");
        sideOptions.ConfigId = string.IsNullOrWhiteSpace(sideOptions.ConfigId) ? SideConfigId : sideOptions.ConfigId;
        // Options 默认 DbType="Sqlite"。CI 矩阵腿若仍用该默认 + TestDb 的 MySql/Postgres 连接串,
        // SqlSugar 会拿 SQLite 驱动去解析 Server=… → "keyword 'server' is not supported"。
        // 双库隔离用例必须与当前 TestDb 方言一致,强制覆盖默认值。
        sideOptions.DbType = TestDb.DbType;
        if (string.IsNullOrWhiteSpace(sideOptions.ConnectionString))
            sideOptions.ConnectionString = TestDb.ConnectionString(id + "-side", sideFile);

        var services = new ServiceCollection();
        configure?.Invoke(services);
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions
            {
                DbType = TestDb.DbType,
                ConnectionString = TestDb.ConnectionString(id + "-main", mainFile),
            },
            additionalDatabases: [sideOptions]);
        return (services.BuildServiceProvider(), mainFile, sideFile, id);
    }

    private static void CleanupDual(string id, string mainFile, string sideFile)
    {
        TestDb.Cleanup(id + "-main", mainFile);
        TestDb.Cleanup(id + "-side", sideFile);
    }

    // ── 双库隔离 + 软删差分 ──────────────────────────────────────────────

    [Fact]
    public async Task DualConfigId_IsolatesSameTableName_AndDifferentialSoftDelete()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions());
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var tenant = db.AsTenant();
            Assert.Equal(SqlSugarSetup.MainConfigId, db.CurrentConnectionConfig.ConfigId?.ToString());
            Assert.True(tenant.IsAnyConnection(SideConfigId));

            var side = tenant.GetConnection(SideConfigId);

            // 同表名建在两个库上
            db.CodeFirst.InitTables<SharedRow>();
            side.CodeFirst.InitTables<SharedRow>();

            await db.Insertable(new SharedRow { Name = "main-live" }).ExecuteCommandAsync();
            await db.Insertable(new SharedRow { Name = "main-dead", IsDelete = true }).ExecuteCommandAsync();
            await side.Insertable(new SharedRow { Id = 1, Name = "side-live" }).ExecuteCommandAsync();
            await side.Insertable(new SharedRow { Id = 2, Name = "side-dead", IsDelete = true }).ExecuteCommandAsync();

            // 真隔离:主库 SharedRow 只有 main-*;副库只有 side-*
            var mainNames = await db.Queryable<SharedRow>().Select(x => x.Name).ToListAsync();
            Assert.Equal(["main-live"], mainNames);
            Assert.DoesNotContain("side-live", mainNames);

            var sideNames = await side.Queryable<SharedRow>().Select(x => x.Name).ToListAsync();
            Assert.Contains("side-live", sideNames);
            Assert.Contains("side-dead", sideNames); // 副库默认无软删
            Assert.DoesNotContain("main-live", sideNames);

            // 主库软删 on:ClearFilter 可见 dead
            var mainWithDeleted = await db.Queryable<SharedRow>().ClearFilter<ISoftDelete>()
                .Select(x => x.Name).ToListAsync();
            Assert.Contains("main-dead", mainWithDeleted);
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public async Task Secondary_OptInSoftDelete_FiltersDeletedRows()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplySoftDeleteFilter = true,
        });
        await using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            side.CodeFirst.InitTables<SideRow>();
            await side.Insertable(new SideRow { Id = 1, Name = "live" }).ExecuteCommandAsync();
            await side.Insertable(new SideRow { Id = 2, Name = "dead", IsDelete = true }).ExecuteCommandAsync();
            Assert.Equal(["live"], await side.Queryable<SideRow>().Select(x => x.Name).ToListAsync());
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public async Task SecondarySoftDelete_SurvivesSuppressFlowContext()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplySoftDeleteFilter = true,
        });
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var side0 = db.AsTenant().GetConnection(SideConfigId);
            side0.CodeFirst.InitTables<SideRow>();
            await side0.Insertable(new SideRow { Id = 1, Name = "live" }).ExecuteCommandAsync();
            await side0.Insertable(new SideRow { Id = 2, Name = "dead", IsDelete = true }).ExecuteCommandAsync();

            List<string>? names = null;
            await Task.Run(() =>
            {
                using (ExecutionContext.SuppressFlow())
                {
                    var side1 = db.AsTenant().GetConnection(SideConfigId);
                    names = side1.Queryable<SideRow>().Select(x => x.Name).ToList();
                }
            });
            Assert.Equal(["live"], names);
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public async Task Secondary_OptInAuditAop_FillsSnowflakeId()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplyAuditAop = true,
        });
        await using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            side.CodeFirst.InitTables<SideRow>();
            await side.Insertable(new SideRow { Name = "auto-id" }).ExecuteCommandAsync();
            var stored = await side.Queryable<SideRow>().FirstAsync(x => x.Name == "auto-id");
            Assert.True(stored.Id >= TenonSeedIds.SnowflakeFloor,
                $"副库 ApplyAuditAop=true 时应填雪花 Id,实际 {stored.Id}");
        }

        CleanupDual(id, mainFile, sideFile);
    }

    // ── 数据范围(独立 review P1) ─────────────────────────────────────────

    [Fact]
    public async Task Secondary_Default_NoDataScopeFilter()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions());
        await using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            side.CodeFirst.InitTables<ScopeSideRow>();
            await side.Insertable(new ScopeSideRow { Id = 1, Title = "org10", CreateOrgId = 10 }).ExecuteCommandAsync();
            await side.Insertable(new ScopeSideRow { Id = 2, Title = "org20", CreateOrgId = 20 }).ExecuteCommandAsync();

            sp.GetRequiredService<IDataScopeContext>().Current =
                DataScopeResult.Restricted([10], includeSelf: false, userId: 0);

            // 默认 ApplyDataScopeFilter=false → 两行都可见
            var titles = await side.Queryable<ScopeSideRow>().Select(x => x.Title).ToListAsync();
            Assert.Contains("org10", titles);
            Assert.Contains("org20", titles);
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public async Task Secondary_OptInDataScopeFilter_FiltersByOrg()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplyDataScopeFilter = true,
        });
        await using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            side.CodeFirst.InitTables<ScopeSideRow>();
            await side.Insertable(new ScopeSideRow { Id = 1, Title = "org10", CreateOrgId = 10 }).ExecuteCommandAsync();
            await side.Insertable(new ScopeSideRow { Id = 2, Title = "org20", CreateOrgId = 20 }).ExecuteCommandAsync();

            sp.GetRequiredService<IDataScopeContext>().Current =
                DataScopeResult.Restricted([10], includeSelf: false, userId: 0);

            var titles = await side.Queryable<ScopeSideRow>().Select(x => x.Title).ToListAsync();
            Assert.Equal(["org10"], titles);
        }

        CleanupDual(id, mainFile, sideFile);
    }

    // ── OnError / Ensure / IRepository ───────────────────────────────────

    [Fact]
    public void Secondary_OnError_LogsFailedSql()
    {
        var log = new CaptureLoggerProvider();
        var (sp, mainFile, sideFile, id) = BuildDual(
            new AdminDatabaseConnectionOptions(),
            s => s.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddProvider(log);
                b.SetMinimumLevel(LogLevel.Trace);
            }));
        using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            // 触发主连接 action → 为副库挂 OnError(G7, 每 client 实例一次)
            _ = sp.GetRequiredService<ISqlSugarClient>().CurrentConnectionConfig;

            Assert.ThrowsAny<Exception>(() =>
                side.Ado.ExecuteCommand("SELECT 1 FROM tenon_mc_no_such_table_side"));

            Assert.Contains(log.Entries, e =>
                e.Level == LogLevel.Error
                && e.Text.Contains("tenon_mc_no_such_table_side", StringComparison.Ordinal));
            // 日志带 ConfigId 前缀,便于区分主/副库
            Assert.Contains(log.Entries, e =>
                e.Level == LogLevel.Error && e.Text.Contains(SideConfigId, StringComparison.Ordinal));
        }

        CleanupDual(id, mainFile, sideFile);
    }

    /// <summary>
    /// Codex P1 回归:内核不得 QueryFilter.Clear / 清空 AOP 覆盖消费方钩子。
    /// 挂上消费方 OnError 后,同上下文再触主库(可能再跑 config-action)仍应走消费方委托。
    /// </summary>
    [Fact]
    public void Secondary_PreservesConsumerOnError_AfterRepeatedGetConnection()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions());
        using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            // 先触发主 action,挂上内核 OnError
            var side = db.AsTenant().GetConnection(SideConfigId);
            var customHits = 0;
            side.Aop.OnError = _ => customHits++;

            // 再触主库 + 再 Get 副库:旧实现会 Clear/重挂并把 OnError 改回内核 logger
            db.Ado.GetInt("SELECT 1");
            var side2 = db.AsTenant().GetConnection(SideConfigId);
            // 同实例时 side2 已是 custom;若被内核重挂则 customHits 不会增加
            Assert.ThrowsAny<Exception>(() =>
                side2.Ado.ExecuteCommand("SELECT 1 FROM tenon_mc_consumer_onerror_probe"));
            Assert.True(customHits >= 1, "消费方 OnError 应仍触发,不得被内核 Clear/重挂抹掉");
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public async Task Secondary_Default_NoAuditAop_DoesNotFillSnowflakeId()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions());
        await using (sp)
        {
            var side = sp.GetRequiredService<ISqlSugarClient>().AsTenant().GetConnection(SideConfigId);
            side.CodeFirst.InitTables<SideRow>();
            // 默认 ApplyAuditAop=false:Id 保持 0,不会被雪花 AOP 改写
            await side.Insertable(new SideRow { Id = 0, Name = "no-aop" }).ExecuteCommandAsync();
            var stored = await side.Queryable<SideRow>().FirstAsync(x => x.Name == "no-aop");
            Assert.True(stored.Id < TenonSeedIds.SnowflakeFloor,
                $"副库默认不应填雪花 Id,实际 {stored.Id}");
        }

        CleanupDual(id, mainFile, sideFile);
    }

    [Fact]
    public void Secondary_Sqlite_EnsuresParentDirectory()
    {
        // 仅 SQLite 有「文件父目录」语义;其它方言腿直接放行
        if (!string.Equals(TestDb.DbType, "Sqlite", StringComparison.OrdinalIgnoreCase))
            return;

        var id = $"mc-dir-{Guid.NewGuid():N}";
        var nest = Path.Combine(Path.GetTempPath(), $"tenon-mc-nest-{id}", "deeper");
        var mainFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}-main.db");
        var sideFile = Path.Combine(nest, "side.db");
        Assert.False(Directory.Exists(nest));

        var services = new ServiceCollection();
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "Data Source=" + mainFile },
            additionalDatabases:
            [
                new AdminDatabaseConnectionOptions
                {
                    ConfigId = SideConfigId,
                    DbType = "Sqlite",
                    ConnectionString = "Data Source=" + sideFile,
                },
            ]);

        using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<ISqlSugarClient>(); // 触发工厂 → Ensure

        Assert.True(Directory.Exists(nest), "副库 SQLite 连接串父目录应在装配时 Ensure");

        try { File.Delete(mainFile); } catch { /* ignore */ }
        try { File.Delete(sideFile); } catch { /* ignore */ }
        try { Directory.Delete(Path.GetDirectoryName(nest)!, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Secondary_RelativeSqlitePath_ResolvesAgainstContentRoot()
    {
        if (!string.Equals(TestDb.DbType, "Sqlite", StringComparison.OrdinalIgnoreCase))
            return;

        var contentRoot = Path.Combine(Path.GetTempPath(), $"tenon-mc-cr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        var mainRel = "data/main.db";
        var sideRel = "data/side.db";
        var sideOptions = new AdminDatabaseConnectionOptions
        {
            ConfigId = SideConfigId,
            DbType = "Sqlite",
            ConnectionString = "Data Source=" + sideRel,
        };

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new ContentRootEnv(contentRoot));
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "Data Source=" + mainRel },
            additionalDatabases: [sideOptions]);

        using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<ISqlSugarClient>();

        var expectedSide = Path.GetFullPath(Path.Combine(contentRoot, sideRel));
        Assert.Contains(expectedSide, sideOptions.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(expectedSide)!),
            "相对路径解析后父目录应 Ensure 在 ContentRoot 下");

        try { Directory.Delete(contentRoot, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Codex P1 并发:多线程 + SuppressFlow 同时取副库并查询。
    /// 锁内只挂一次钩子;软删 opt-in 在并发下仍只见 live。
    /// </summary>
    [Fact]
    public void Secondary_ConcurrentGetConnection_SoftDeleteStillCorrect()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplySoftDeleteFilter = true,
        });
        using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var side0 = db.AsTenant().GetConnection(SideConfigId);
            side0.CodeFirst.InitTables<SideRow>();
            side0.Insertable(new SideRow { Id = 1, Name = "live" }).ExecuteCommand();
            side0.Insertable(new SideRow { Id = 2, Name = "dead", IsDelete = true }).ExecuteCommand();

            Exception? fault = null;
            Parallel.For(0, 32, i =>
            {
                try
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        var side = db.AsTenant().GetConnection(SideConfigId);
                        var names = side.Queryable<SideRow>().Select(x => x.Name).ToList();
                        Assert.Equal(["live"], names);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref fault, ex, null);
                }
            });

            Assert.Null(fault);
        }

        CleanupDual(id, mainFile, sideFile);
    }

    private sealed class ContentRootEnv(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "MultiConfigIdTests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task IRepository_AlwaysUsesMainConnection()
    {
        var (sp, mainFile, sideFile, id) = BuildDual(new AdminDatabaseConnectionOptions
        {
            ApplyAuditAop = true,
        });
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var side = db.AsTenant().GetConnection(SideConfigId);
            db.CodeFirst.InitTables<RepoRow>();
            side.CodeFirst.InitTables<RepoRow>();

            // 副库直接写入
            await side.Insertable(new RepoRow { Name = "only-on-side" }).ExecuteCommandAsync();
            // 主库仓储应看不到
            await using (var scope = sp.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IRepository<RepoRow>>();
                Assert.Equal(0, await repo.AsQueryable().CountAsync());
            }

            // 主库仓储写入后副库仍只有 side 那一行
            await using (var scope = sp.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IRepository<RepoRow>>();
                await repo.InsertAsync(new RepoRow { Name = "only-on-main" });
            }

            var sideNames = await side.Queryable<RepoRow>().Select(x => x.Name).ToListAsync();
            Assert.Equal(["only-on-side"], sideNames);
            Assert.Equal(1, await db.Queryable<RepoRow>().CountAsync());
        }

        CleanupDual(id, mainFile, sideFile);
    }

    // ── 校验 ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateAdditionalDatabases_RejectsInvalidEntries()
    {
        static void AddWith(params AdminDatabaseConnectionOptions?[] extras)
        {
            var services = new ServiceCollection();
            services.AddTenonAdminSqlSugar(
                new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "Data Source=:memory:" },
                additionalDatabases: extras!);
        }

        var reserved = Assert.Throws<InvalidOperationException>(() =>
            AddWith(new AdminDatabaseConnectionOptions
            {
                ConfigId = SqlSugarSetup.MainConfigId,
                DbType = "Sqlite",
                ConnectionString = "Data Source=:memory:",
            }));
        Assert.Contains(SqlSugarSetup.MainConfigId, reserved.Message);

        // 保留名大小写不敏感(Codex P2: tenonadmin 不得绕过)
        var reservedCi = Assert.Throws<InvalidOperationException>(() =>
            AddWith(new AdminDatabaseConnectionOptions
            {
                ConfigId = "tenonadmin",
                DbType = "Sqlite",
                ConnectionString = "Data Source=:memory:",
            }));
        Assert.Contains(SqlSugarSetup.MainConfigId, reservedCi.Message, StringComparison.OrdinalIgnoreCase);

        var dup = Assert.Throws<InvalidOperationException>(() =>
            AddWith(
                new AdminDatabaseConnectionOptions
                {
                    ConfigId = "Audit",
                    DbType = "Sqlite",
                    ConnectionString = "Data Source=a.db",
                },
                new AdminDatabaseConnectionOptions
                {
                    ConfigId = "Audit",
                    DbType = "Sqlite",
                    ConnectionString = "Data Source=b.db",
                }));
        Assert.Contains("Audit", dup.Message);

        var dupCi = Assert.Throws<InvalidOperationException>(() =>
            AddWith(
                new AdminDatabaseConnectionOptions
                {
                    ConfigId = "Audit",
                    DbType = "Sqlite",
                    ConnectionString = "Data Source=a.db",
                },
                new AdminDatabaseConnectionOptions
                {
                    ConfigId = "audit",
                    DbType = "Sqlite",
                    ConnectionString = "Data Source=b.db",
                }));
        Assert.Contains("重复", dupCi.Message);

        var emptyId = Assert.Throws<InvalidOperationException>(() =>
            AddWith(new AdminDatabaseConnectionOptions
            {
                ConfigId = "  ",
                DbType = "Sqlite",
                ConnectionString = "Data Source=a.db",
            }));
        Assert.Contains("ConfigId", emptyId.Message);

        var emptyConn = Assert.Throws<InvalidOperationException>(() =>
            AddWith(new AdminDatabaseConnectionOptions
            {
                ConfigId = "Audit",
                DbType = "Sqlite",
                ConnectionString = "  ",
            }));
        Assert.Contains("ConnectionString", emptyConn.Message);

        var badType = Assert.Throws<InvalidOperationException>(() =>
            AddWith(new AdminDatabaseConnectionOptions
            {
                ConfigId = "Audit",
                DbType = "NotARealDb",
                ConnectionString = "Data Source=a.db",
            }));
        Assert.Contains("DbType", badType.Message);

        var nullItem = Assert.Throws<InvalidOperationException>(() =>
            AddWith((AdminDatabaseConnectionOptions?)null));
        Assert.Contains("null", nullItem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDatabase_StillRegistersOnlyMainConfig()
    {
        var id = $"mc-single-{Guid.NewGuid():N}";
        var mainFile = Path.Combine(Path.GetTempPath(), $"tenon-{id}.db");

        var services = new ServiceCollection();
        services.AddTenonAdminSqlSugar(
            new AdminDatabaseOptions
            {
                DbType = TestDb.DbType,
                ConnectionString = TestDb.ConnectionString(id, mainFile),
            });

        using var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(SqlSugarSetup.MainConfigId, db.CurrentConnectionConfig.ConfigId?.ToString());
        Assert.False(db.AsTenant().IsAnyConnection(SideConfigId));

        // 单库路径主库软删仍在(比「仅看 ConfigId」更贴零回归)
        db.CodeFirst.InitTables<SharedRow>();
        db.Insertable(new SharedRow { Name = "live" }).ExecuteCommand();
        db.Insertable(new SharedRow { Name = "dead", IsDelete = true }).ExecuteCommand();
        Assert.Equal(["live"], db.Queryable<SharedRow>().Select(x => x.Name).ToList());

        TestDb.Cleanup(id, mainFile);
    }
}
