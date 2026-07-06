using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 首启数据库初始化(IHostedService,应用启动时执行一次):
/// 确保 SQLite 目录 → CodeFirst 建表(扫描已登记程序集的全部 [SugarTable] 实体)→ 执行全部种子。
/// <para>建表与种子都幂等:表已存在则按实体差异补列(SqlSugar CodeFirst 语义,不删列不改窄);
/// 种子按主键判存只插缺失行(见 <see cref="ISeedData{TEntity}"/>)。
/// 生产环境的建表开关策略见设计 §12(EnableCodeFirstInProduction,接入宿主环境判断时启用)。</para>
/// </summary>
internal sealed class DatabaseInitializer(
    ISqlSugarClient db,
    AdminDatabaseOptions options,
    TenonEntitySources sources,
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureSqliteDirectory();

        if (options.EnableCodeFirst)
        {
            // 扫描所有登记程序集中带 [SugarTable] 的非抽象类 —— 实体清单唯一来源
            var entityTypes = sources.Assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetCustomAttribute<SugarTable>() is not null)
                .ToArray();

            db.CodeFirst.InitTables(entityTypes);
            logger.LogInformation("TenonAdmin: CodeFirst 建表完成({Count} 个实体:{Tables})",
                entityTypes.Length, string.Join(", ", entityTypes.Select(t => t.Name)));
        }

        if (options.EnableSeed)
        {
            // 种子实现可能有 Scoped 依赖(仓储/Options),开独立作用域解析
            await using var scope = scopeFactory.CreateAsyncScope();
            var total = 0;
            foreach (var seed in scope.ServiceProvider.GetServices<ISeedData>())
                total += await ExecuteSeedAsync(seed);
            logger.LogInformation("TenonAdmin: 种子执行完成(本次新插入 {Total} 行)", total);
        }

        logger.LogInformation("TenonAdmin 数据库就绪:{DbType} / {Conn}", options.DbType, options.ConnectionString);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>执行单个种子:经泛型接口取实体类型,反射进入强类型管道(仅启动期一次,开销可忽略)</summary>
    private async Task<int> ExecuteSeedAsync(ISeedData seed)
    {
        var entityType = seed.GetType().GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>))
            .GetGenericArguments()[0];

        var method = typeof(DatabaseInitializer)
            .GetMethod(nameof(ExecuteSeedCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityType);

        return await (Task<int>)method.Invoke(this, [seed])!;
    }

    /// <summary>强类型种子管道:校验固定 Id → Storageable 按主键分流 → 只插不存在的行(幂等核心)</summary>
    private async Task<int> ExecuteSeedCoreAsync<TEntity>(ISeedData<TEntity> seed) where TEntity : BaseEntity, new()
    {
        var rows = seed.HasData().ToList();
        if (rows.Count == 0) return 0;

        // 保护性检查:Id=0 会被 AOP 填成新雪花号,导致每次启动都"判不存在"而重复插入
        if (rows.Any(r => r.Id == 0))
            throw new InvalidOperationException(
                $"种子 {seed.GetType().Name} 存在 Id=0 的行:种子数据必须显式指定固定 Id(幂等判存依赖主键)。");

        var storage = await db.Storageable(rows).ToStorageAsync();
        return storage.InsertList.Count == 0 ? 0 : await storage.AsInsertable.ExecuteCommandAsync();
    }

    /// <summary>SQLite 只建文件不建目录,这里从连接串解析出目录并补建</summary>
    private void EnsureSqliteDirectory()
    {
        if (!options.DbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) return;
        const string marker = "Data Source=";
        var idx = options.ConnectionString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var path = options.ConnectionString[(idx + marker.Length)..].Split(';')[0].Trim();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
