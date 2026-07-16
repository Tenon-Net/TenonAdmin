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

        // 扫描所有登记程序集中带 [SugarTable] 的非抽象类 —— 实体清单唯一来源
        var entityTypes = sources.Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetCustomAttribute<SugarTable>() is not null)
            .ToArray();

        if (codeFirstAllowed)
        {
            db.CodeFirst.InitTables(entityTypes);
            logger.LogInformation("TenonAdmin: CodeFirst 建表完成({Count} 个实体:{Tables})",
                entityTypes.Length, string.Join(", ", entityTypes.Select(t => t.Name)));
        }
        else
        {
            // 建表被跳过 ⇒ 没人替库补列。升级路径上这是最要命的一条,见方法注释。
            EnsureExistingTablesHaveEntityColumns(entityTypes);
        }

        if (options.EnableSeed)
        {
            // 种子实现可能有 Scoped 依赖(仓储/Options),开独立作用域解析
            await using var scope = scopeFactory.CreateAsyncScope();
            var seeds = scope.ServiceProvider.GetServices<ISeedData>().ToArray();
            // 建表被跳过时先探表:种子第一步就要读写表(SuperAdminSeed 在 HasData() 里就 SELECT sys_user),
            // 空库上撞过去只会得到驱动层的 "no such table",无从反推真正原因。
            if (!codeFirstAllowed) EnsureSeedTablesExist(seeds);

            // 升级闸门(见 ISeedData.SyncOnUpgrade):必须在跑种子**之前**读——SchemaVersionSeed 本身
            // 会把版本行插成 Current,读晚了空库和老库就分不出来了。
            // 空库:stored 为 null → upgrading=true,但空库上 UpdateList 本就是空,无副作用。
            var stored = (await db.Queryable<SysSchemaVersion>().FirstAsync(x => x.Id == 1))?.Version;
            var upgrading = stored != SysSchemaVersion.Current;

            var (inserted, synced) = (0, 0);
            foreach (var seed in seeds)
            {
                var (i, u) = await ExecuteSeedAsync(seed, upgrading);
                inserted += i;
                synced += u;
            }

            if (upgrading)
            {
                // 版本行落到 Current。SchemaVersionSeed 只在空库上插得进去(它自己不开 SyncOnUpgrade),
                // 老库那行还停在旧版本,得在这儿显式写回,否则下次启动又当成一次升级。
                await db.Updateable<SysSchemaVersion>()
                    .SetColumns(x => new SysSchemaVersion { Version = SysSchemaVersion.Current, AppliedTime = DateTime.Now })
                    .Where(x => x.Id == 1)
                    .ExecuteCommandAsync();
                logger.LogInformation("TenonAdmin: 种子版本 {From} → {To}(结构性种子已同步 {Synced} 行)",
                    stored ?? "(空库)", SysSchemaVersion.Current, synced);
            }

            logger.LogInformation("TenonAdmin: 种子执行完成(新插入 {Inserted} 行,升级同步 {Synced} 行)", inserted, synced);
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

    /// <summary>
    /// 建表被跳过时的<b>升级</b>前置检查:已存在的表必须具备实体映射的全部列。
    /// <para>老守卫 <see cref="EnsureSeedTablesExist"/> 只看<b>表在不在</b>。而生产按 deployment.md 是关着建表闸门的,
    /// 于是消费者从 v1.0 升到 v1.1(内核新增一列)时:表在 → 守卫放行 → <b>启动成功</b> → 第一次查到那张表才炸在
    /// 驱动层的 "no such column" 上。没有表名、没有列名、没人知道该 ALTER 什么。发了正式版之后,
    /// <b>内核每加一列都会这样咬到所有关闸门的用户</b>。这里把它换成一条点名到列的启动错误。</para>
    /// <para>只查<b>缺列</b>,不查类型/长度/可空性的变化:那些既难跨四种方言判准,又容易把今天能正常跑的库
    /// (DBA 有意把 varchar 放宽、加了自己的列)判成起不来 —— 那是新的失败面。而缺列是<b>确定会崩</b>的那一类:
    /// 实体映射了它,查询就一定会 SELECT 它。</para>
    /// <para>ponytail: 表不存在的情况留给种子探表那条路(见 <see cref="EnsureSeedTablesExist"/>),这里跳过 —— 不重复报同一件事。</para>
    /// </summary>
    private void EnsureExistingTablesHaveEntityColumns(Type[] entityTypes)
    {
        var drift = new List<string>();

        foreach (var type in entityTypes)
        {
            var table = db.EntityMaintenance.GetTableName(type);
            if (!db.DbMaintenance.IsAnyTable(table, false)) continue;      // 缺表:另一条路径管

            var actual = db.DbMaintenance.GetColumnInfosByTableName(table, false)
                .Select(c => c.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = db.EntityMaintenance.GetEntityInfo(type).Columns
                .Where(c => !c.IsIgnore && !actual.Contains(c.DbColumnName))
                .Select(c => c.DbColumnName)
                .ToArray();

            if (missing.Length > 0) drift.Add($"{table}({string.Join(", ", missing)})");
        }

        if (drift.Count == 0) return;

        throw new InvalidOperationException(
            $"TenonAdmin 启动失败:库表结构落后于当前实体,以下表缺少列:{string.Join("; ", drift)}。" +
            $"(CodeFirst 自动建表已跳过:EnableCodeFirst={options.EnableCodeFirst},当前环境={env.EnvironmentName}," +
            $"EnableCodeFirstInProduction={options.EnableCodeFirstInProduction} —— 于是没人替库补列。)" +
            "这通常意味着内核升级新增了列而库还是老结构。二选一:" +
            "(1) 本次启动配置 TenonAdmin:Database:EnableCodeFirstInProduction=true,由应用补列" +
            "(CodeFirst 只加列,不删列、不改窄,对存量数据是安全的);" +
            "(2) 由 DBA 对上述表执行 ALTER TABLE ... ADD COLUMN 补齐后再启动。" +
            "详见 docs/deployment.md 的升级一节。" +
            "现在拦下,是因为放行的话进程会正常启动,直到第一次查到这些表才炸在驱动层的\"列不存在\"上。");
    }

    /// <summary>种子的目标实体类型 = 其 <see cref="ISeedData{TEntity}"/> 的泛型实参</summary>
    private static Type SeedEntityType(ISeedData seed) => seed.GetType().GetInterfaces()
        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>))
        .GetGenericArguments()[0];

    /// <summary>执行单个种子:经泛型接口取实体类型,反射进入强类型管道(仅启动期一次,开销可忽略)</summary>
    private async Task<(int Inserted, int Synced)> ExecuteSeedAsync(ISeedData seed, bool upgrading)
    {
        var method = typeof(DatabaseInitializer)
            .GetMethod(nameof(ExecuteSeedCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(SeedEntityType(seed));

        return await (Task<(int, int)>)method.Invoke(this, [seed, upgrading])!;
    }

    /// <summary>
    /// 强类型种子管道:校验固定 Id → Storageable 按主键分流 → 插入缺失行(幂等核心);
    /// 升级时对结构性种子(<see cref="ISeedData{TEntity}.SyncOnUpgrade"/>)额外覆盖已有行。
    /// </summary>
    private async Task<(int Inserted, int Synced)> ExecuteSeedCoreAsync<TEntity>(ISeedData<TEntity> seed, bool upgrading)
        where TEntity : BaseEntity, new()
    {
        var rows = seed.HasData().ToList();
        if (rows.Count == 0) return (0, 0);

        EnsureSeedIdsInReservedRange(seed, rows);
        EnsureSeedIdsUnique(seed, rows);

        // 连接表:代理主键会漂(运行时授权发雪花号),按业务唯一键判存——该键已存在就跳过,
        // 无论它挂的是种子 Id 还是雪花 Id。不这么做,种子会拿固定 Id 把同一业务键再插一遍 → 撞唯一索引 → 启动崩。
        // WhereColumns 让 Storageable 按这些列(而非主键)分流;这类种子无业务字段可覆盖,故不参与 SyncOnUpgrade。
        // (连接行按 RbacService 语义是物理删除,库里不存在软删行,故无需 ClearFilter。)
        if (seed.DedupColumns is { Length: > 0 } dedup)
        {
            var joinStorage = await db.Storageable(rows).WhereColumns(dedup).ToStorageAsync();
            var n = joinStorage.InsertList.Count == 0 ? 0 : await joinStorage.AsInsertable.ExecuteCommandAsync();
            return (n, 0);
        }

        // 按主键分流。不用 Storageable 而是自己查:存在性判断必须看**物理**行,
        // 而 ClearFilter 挂不到 Storageable 上 —— 全局软删过滤器会让用户软删掉的内置菜单查不到,
        // 判成"不存在"→ 走 INSERT → 撞主键(软删一条内置菜单,应用就再也起不来)。
        var ids = rows.Select(r => r.Id).ToList();
        var existing = await db.Queryable<TEntity>().ClearFilter()
            .Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var existingIds = existing.ToHashSet();

        var toInsert = rows.Where(r => !existingIds.Contains(r.Id)).ToList();
        var inserted = toInsert.Count == 0 ? 0 : await db.Insertable(toInsert).ExecuteCommandAsync();

        var toUpdate = rows.Where(r => existingIds.Contains(r.Id)).ToList();
        if (!upgrading || !seed.SyncOnUpgrade || toUpdate.Count == 0) return (inserted, 0);

        // IgnoreColumns 不是防御性代码:AsUpdateable 默认刷全部映射列,而种子实例的 CreateTime 是
        // default(DateTime)(0001-01-01)、CreateUserId 是 null —— 不排除就会把老库正确的审计字段刷成垃圾。
        // IsDelete 同理:用户软删掉的内置菜单不该被升级复活。
        // CreateOrgId(DataEntity 的数据范围锚点)只在消费者把开关开在 DataEntity 种子上时才存在;
        // 刷成 null 会让那些行从所有机构范围查询里消失,一并排除。
        string[] ignored = typeof(TEntity).IsAssignableTo(typeof(DataEntity))
            ? [nameof(BaseEntity.CreateTime), nameof(BaseEntity.CreateUserId), nameof(BaseEntity.IsDelete), nameof(DataEntity.CreateOrgId)]
            : [nameof(BaseEntity.CreateTime), nameof(BaseEntity.CreateUserId), nameof(BaseEntity.IsDelete)];

        var synced = await db.Updateable(toUpdate).IgnoreColumns(ignored).ExecuteCommandAsync();

        return (inserted, synced);
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

    /// <summary>本次启动已见过的种子固定 Id(按实体类型分桶,值=声领它的种子类名)——供跨种子撞号检测。</summary>
    private readonly Dictionary<Type, Dictionary<long, string>> _seenSeedIds = [];

    /// <summary>
    /// 种子固定 Id 不得重复:同种子内重复(复制行忘改 Id)与跨种子撞号(新种子挑了已占用的号)一律启动失败。
    /// <para>撞号的破坏是<b>静默</b>的:幂等判存把后来的行当"已存在"跳过(菜单树无声缺一块),
    /// 开了 <see cref="ISeedData{TEntity}.SyncOnUpgrade"/> 的种子升级时还会把别人的行覆盖掉——所以必须大声失败。</para>
    /// </summary>
    private void EnsureSeedIdsUnique<TEntity>(ISeedData<TEntity> seed, List<TEntity> rows)
        where TEntity : BaseEntity, new()
    {
        if (!_seenSeedIds.TryGetValue(typeof(TEntity), out var seen))
            _seenSeedIds[typeof(TEntity)] = seen = [];

        foreach (var row in rows)
        {
            if (seen.TryGetValue(row.Id, out var owner))
                throw new InvalidOperationException(
                    $"种子 Id 撞号:{seed.GetType().Name} 与 {owner} 都声领了 {typeof(TEntity).Name} 的 Id={row.Id}。" +
                    "固定 Id 在同一实体上必须全局唯一,请换一个未占用的号" +
                    $"(内核用 [1, {TenonSeedIds.KernelMax}],消费者从 {TenonSeedIds.ConsumerMin} 起取号)。");
            seen[row.Id] = seed.GetType().Name;
        }
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
