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
    IHostEnvironment env,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureSqliteDirectory();

        // 生产建表安全闸门(§12/§4.1):生产环境即便 EnableCodeFirst=true,也需显式 EnableCodeFirstInProduction 才建表——
        // 生产库通常 DBA 手工维护,应用不应擅自 ALTER。非生产环境不受此约束。
        var codeFirstAllowed = options.EnableCodeFirst
            && (!env.IsProduction() || options.EnableCodeFirstInProduction);
        if (options.EnableCodeFirst && !codeFirstAllowed)
            logger.LogWarning("TenonAdmin: 生产环境未显式开启 EnableCodeFirstInProduction,已跳过 CodeFirst 自动建表(§12 建表安全)。");

        if (codeFirstAllowed)
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
            var seeds = scope.ServiceProvider.GetServices<ISeedData>().ToArray();
            // 建表被跳过时先探表:种子第一步就要读写表(SuperAdminSeed 在 HasData() 里就 SELECT sys_user),
            // 空库上撞过去只会得到驱动层的 "no such table",无从反推真正原因。
            if (!codeFirstAllowed) EnsureSeedTablesExist(seeds);
            var total = 0;
            foreach (var seed in seeds)
                total += await ExecuteSeedAsync(seed);
            logger.LogInformation("TenonAdmin: 种子执行完成(本次新插入 {Total} 行)", total);
        }

        logger.LogInformation("TenonAdmin 数据库就绪:{DbType} / {Conn}", options.DbType, options.ConnectionString);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 建表被跳过(生产闸门关 / EnableCodeFirst=false)时的启动前置检查:种子要写的表必须已存在。
    /// 缺表就抛出可行动的错误,而不是让种子去撞驱动层的"表不存在"。
    /// <para>只探种子涉及的表,不探全部实体:全量探测会让今天能正常启动的配置(消费者自管部分表等)
    /// 变成起不来 —— 那是新的失败面,而种子表恰好覆盖了真正会崩的那条路径。</para>
    /// </summary>
    private void EnsureSeedTablesExist(IReadOnlyCollection<ISeedData> seeds)
    {
        // 表名取自 ISeedData<T> 的泛型实参,不调 HasData()——SuperAdminSeed 在 HasData() 里就查库了。
        var missing = seeds
            .Select(SeedEntityType)
            .Distinct()
            .Select(db.EntityMaintenance.GetTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // isCache:false —— 元数据缓存可能掩盖"表其实不存在";不 try/catch,连不上库应原样冒出而非误报为缺表
            .Where(table => !db.DbMaintenance.IsAnyTable(table, false))
            .ToArray();
        if (missing.Length == 0) return;   // 表齐 → 种子照常执行(DBA 手工建表的库,超管与菜单树仍须写入)

        throw new InvalidOperationException(
            $"TenonAdmin 启动失败:CodeFirst 自动建表已跳过(EnableCodeFirst={options.EnableCodeFirst}," +
            $"当前环境={env.EnvironmentName},EnableCodeFirstInProduction={options.EnableCodeFirstInProduction})," +
            $"但种子要写的表在库中不存在:{string.Join(", ", missing)}。二选一:" +
            "(1) 配置 TenonAdmin:Database:EnableCodeFirstInProduction=true,首启由应用建表;" +
            "(2) 先由 DBA 建好表结构再启动。详见 docs/deployment.md。" +
            "(若确实不需要内置种子——超管账号与菜单树——可配置 TenonAdmin:Database:EnableSeed=false。)");
    }

    /// <summary>种子的目标实体类型 = 其 <see cref="ISeedData{TEntity}"/> 的泛型实参</summary>
    private static Type SeedEntityType(ISeedData seed) => seed.GetType().GetInterfaces()
        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>))
        .GetGenericArguments()[0];

    /// <summary>执行单个种子:经泛型接口取实体类型,反射进入强类型管道(仅启动期一次,开销可忽略)</summary>
    private async Task<int> ExecuteSeedAsync(ISeedData seed)
    {
        var method = typeof(DatabaseInitializer)
            .GetMethod(nameof(ExecuteSeedCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(SeedEntityType(seed));

        return await (Task<int>)method.Invoke(this, [seed])!;
    }

    /// <summary>强类型种子管道:校验固定 Id → Storageable 按主键分流 → 只插不存在的行(幂等核心)</summary>
    private async Task<int> ExecuteSeedCoreAsync<TEntity>(ISeedData<TEntity> seed) where TEntity : BaseEntity, new()
    {
        var rows = seed.HasData().ToList();
        if (rows.Count == 0) return 0;

        EnsureSeedIdsInReservedRange(seed, rows);

        var storage = await db.Storageable(rows).ToStorageAsync();
        return storage.InsertList.Count == 0 ? 0 : await storage.AsInsertable.ExecuteCommandAsync();
    }

    /// <summary>
    /// 种子固定 Id 必须落在保留区间 <c>[1, <see cref="TenonSeedIds.ConsumerMax"/>]</c>(见 <see cref="TenonSeedIds"/>)。
    /// <para>下界:Id=0 会被 AOP 当成"未指定"填上新雪花号,于是每次启动都判不存在 → 重复插入,幂等直接失效。</para>
    /// <para>上界:4096 起是雪花运行时发号区,时间迟早推到那个号上 —— 种子占了它,将来某次插入就主键冲突。
    /// 这一层是<b>唯一真正有牙的约束</b>:区间写在文档里没人读,写成启动异常才跑不掉。</para>
    /// <para>这里只校验"所有种子都不得越界"这条通用规则;"内核自己不得超过 <see cref="TenonSeedIds.KernelMax"/>"
    /// 是内核的自律,由 <c>SeedIdRangeTests</c> 守 —— 运行时不该去猜哪个种子是消费者的。</para>
    /// </summary>
    private static void EnsureSeedIdsInReservedRange<TEntity>(ISeedData<TEntity> seed, List<TEntity> rows)
        where TEntity : BaseEntity, new()
    {
        var bad = rows.Where(r => r.Id is < 1 or > TenonSeedIds.ConsumerMax).Select(r => r.Id).Distinct().ToArray();
        if (bad.Length == 0) return;

        throw new InvalidOperationException(
            $"种子 {seed.GetType().Name} 的固定 Id 越界:{string.Join(", ", bad)}。" +
            $"种子数据必须显式指定固定 Id 且落在保留区间 [1, {TenonSeedIds.ConsumerMax}] 内" +
            "(Id=0 会被审计 AOP 填成新雪花号,导致每次启动重复插入;" +
            $"≥ {TenonSeedIds.SnowflakeFloor} 是雪花运行时发号区,迟早与新增数据主键冲突)。" +
            $"内核内置种子用 [1, {TenonSeedIds.KernelMax}],消费者种子请从 {TenonSeedIds.ConsumerMin} 起取号。");
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
