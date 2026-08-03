using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>
/// 数据层装配(设计 §2.2):SqlSugar 单例 + 泛型仓储 + 种子机制 + 首启初始化。
/// 注册一律 TryAdd——用户在 AddTenonAdmin() 之前注册同接口实现即可整体替换(设计 §5.2)。
/// <para>多库(issue #28):主库 ConfigId 固定 <see cref="MainConfigId"/>;副库经
/// <c>additionalDatabases</c> / <c>TenonAdmin:AdditionalDatabases</c> 挂入同一
/// <see cref="SqlSugarScope"/>。副库默认不挂软删/数据范围/审计 AOP(显式 opt-in);
/// CodeFirst/种子仍只扫主库。<c>IRepository&lt;T&gt;</c> 始终打主库。
/// 访问副库:<c>db.AsTenant().GetConnection(configId)</c>。</para>
/// </summary>
public static class SqlSugarSetup
{
    /// <summary>主库 SqlSugar ConfigId(写死;副库不得占用,校验大小写不敏感)。</summary>
    public const string MainConfigId = "TenonAdmin";

    /// <summary>
    /// 注册数据层:雪花 Id、<see cref="ISqlSugarClient"/>(<see cref="SqlSugarScope"/>)、
    /// 开放泛型 <see cref="IRepository{TEntity}"/>、种子集合与 <c>DatabaseInitializer</c>。
    /// <para>一律 <c>TryAdd*</c>:消费方在 <c>AddTenonAdmin</c> 之前注册同接口即可整体替换。
    /// 公开入口允许裸容器调用(无 <c>ILoggerFactory</c>/<c>IHostEnvironment</c> 也能起)。</para>
    /// <para>多库:主库 ConfigId 固定 <see cref="MainConfigId"/>;副库见
    /// <paramref name="additionalDatabases"/> / <c>TenonAdmin:AdditionalDatabases</c>。
    /// 副库钩子默认关(软删/数据范围/审计 AOP),OnError 始终开;访问
    /// <c>db.AsTenant().GetConnection(configId)</c>。<c>IRepository&lt;T&gt;</c> 始终主库。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="db">主库配置(<c>TenonAdmin:Database</c>)。</param>
    /// <param name="entityAssemblies">并入 CodeFirst 扫描的实体程序集(主库);可为 null。</param>
    /// <param name="additionalDatabases">副库列表;null/空 = 单库历史路径。校验见
    /// <see cref="ValidateAdditionalDatabases"/>。</param>
    /// <returns>同一 <paramref name="services"/>,便于链式调用。</returns>
    /// <exception cref="InvalidOperationException">
    /// 副库 ConfigId 为空、与主库保留名冲突(大小写不敏感)、重复、连接串为空、或 DbType 无法解析。
    /// </exception>
    public static IServiceCollection AddTenonAdminSqlSugar(
        this IServiceCollection services,
        AdminDatabaseOptions db,
        IEnumerable<Assembly>? entityAssemblies = null,
        IEnumerable<AdminDatabaseConnectionOptions>? additionalDatabases = null)
    {
        // ── ID 生成器:雪花默认实现(用户可换,见 IIdGenerator)──────────────
        // WorkerId 从 TenonAdmin:Id:WorkerId 注入(默认 0);多实例水平扩展须为每实例配不同值,否则同毫秒撞号(P2-20)
        services.TryAddSingleton<IIdGenerator>(sp =>
            new SnowflakeIdGenerator(sp.GetService<AdminIdOptions>()?.WorkerId ?? 0, sp.GetService<TimeProvider>()));

        // ── 数据范围环境载体(§6):授权管道写入、全局过滤器读取;AsyncLocal 单例 ──
        services.TryAddSingleton<IDataScopeContext, DataScopeContext>();

        // ── 当前用户兜底实现(系统上下文);HTTP 侧由 AspNetCore 层前置注册覆盖 ──
        services.TryAddSingleton<ICurrentUser, SystemCurrentUser>();

        // ── 实体程序集登记:本层自带 sys_schema_version;上层/用户程序集从参数并入 ──
        var sources = new TenonEntitySources();
        sources.Add(typeof(SqlSugarSetup).Assembly);
        foreach (var asm in entityAssemblies ?? []) sources.Add(asm);
        services.TryAddSingleton(sources);

        // 副库列表快照(校验一次;后续闭包只读)。空 = 单库历史路径。
        var additionals = (additionalDatabases ?? []).ToList();
        ValidateAdditionalDatabases(additionals);

        // ── SqlSugar 客户端:官方推荐的线程安全单例形态 SqlSugarScope ───────────
        services.TryAddSingleton<ISqlSugarClient>(sp =>
        {
            var contentRoot = sp.GetService<IHostEnvironment>()?.ContentRootPath;
            // SQLite 相对路径规整为相对 ContentRoot 的绝对路径(P2-3):否则从非项目目录/服务托管启动时,
            // 库与 EnsureSqliteDirectory 建的目录会落在意外位置。就地回写 options 单例,供 DatabaseInitializer 一致读取。
            db.ConnectionString = ResolveSqlitePath(db.DbType, db.ConnectionString, contentRoot);
            EnsureSqliteDirectory(db.DbType, db.ConnectionString);

            var configs = new List<ConnectionConfig>(1 + additionals.Count)
            {
                BuildConnectionConfig(MainConfigId, db.DbType, db.ConnectionString),
            };

            foreach (var extra in additionals)
            {
                extra.ConnectionString = ResolveSqlitePath(extra.DbType, extra.ConnectionString, contentRoot);
                EnsureSqliteDirectory(extra.DbType, extra.ConnectionString);
                configs.Add(BuildConnectionConfig(extra.ConfigId, extra.DbType, extra.ConnectionString));
            }

            // ConfigId → 钩子策略。主库全开;副库按项 opt-in(默认全关,见 AdminDatabaseConnectionOptions)。
            var hookByConfigId = new Dictionary<string, HookPolicy>(StringComparer.Ordinal)
            {
                [MainConfigId] = HookPolicy.ForMain(db.SlowSqlMillis),
            };
            foreach (var extra in additionals)
                hookByConfigId[extra.ConfigId] = HookPolicy.ForAdditional(extra);

            var idGen = sp.GetRequiredService<IIdGenerator>();
            var time = sp.GetService<TimeProvider>() ?? TimeProvider.System; // 统一时间源,可测试(设计 §12)
            var dataScope = sp.GetRequiredService<IDataScopeContext>();      // 数据范围载体(单例,过滤器闭包捕获)
            var currentUser = sp.GetRequiredService<ICurrentUser>();         // 当前用户(单例,审计字段填充用)
            // SQL 诊断日志。类别名固定,便于消费者单独调级别(Logging:LogLevel:TenonAdmin.Sql)。
            // GetService 而非 GetRequiredService:本方法是公开装配入口,允许在裸容器上单独调用(测试与只要数据层的消费者
            // 都这么用),不能凭空多出一个必需依赖 —— 没有日志就静默不打,不该因此起不来。
            var sqlLog = (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("TenonAdmin.Sql");

            if (additionals.Count > 0)
            {
                // 引导日志:副库实体勿进 ApplicationAssemblies,否则主库 CodeFirst 会误建表(issue #28 G4)。
                sqlLog.LogInformation(
                    "TenonAdmin: 已挂 {Count} 个副库 ConfigId=[{Ids}]。访问请用 db.AsTenant().GetConnection(configId);" +
                    "IRepository<> 始终主库;副库实体勿登记 ApplicationAssemblies(见文档「配置多数据库」)。",
                    additionals.Count, string.Join(", ", additionals.Select(a => a.ConfigId)));
            }

            // 把钩子挂到指定 client。configId 用于日志区分主/副库。
            void AttachHooks(ISqlSugarClient client, string configId, HookPolicy policy)
            {
                if (policy.ApplySoftDeleteFilter)
                {
                    // 全局软删过滤器:实现 ISoftDelete 的实体,查询自动排除已删行(设计 §12)
                    client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);
                }

                if (policy.ApplyDataScopeFilter)
                {
                    // 全局数据范围过滤器(§6 招牌能力):对 IOrgScoped 实体(DataEntity 及其子类)按当前请求生效范围过滤。
                    // 按接口匹配(SqlSugar 不对基类生效,只认接口/精确类型,与软删过滤器一致)。
                    // 表达式里 dataScope.Current 的三个属性都与实体参数无关,SqlSugar 先本地求值成常量(机构集合 → SQL IN),
                    // 再与实体相关的部分拼成 WHERE;Unrestricted 时整体恒真(不过滤)。
                    // 两个布尔标记写成 `== true` 而非裸布尔:SqlServer 的谓词上下文不接受裸标量(裸 1/0 → "非布尔类型的表达式"),
                    // 必须是比较式(渲染成 `@p = 1`)。软删过滤器 `e.IsDelete == false` 同理已是比较式,故本就跨方言可用。
                    client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
                        dataScope.Current.IsUnrestricted == true
                        || (e.CreateOrgId != null && dataScope.Current.OrgIds.Contains(e.CreateOrgId.Value))
                        || (dataScope.Current.IncludeSelf == true && e.CreateUserId == dataScope.Current.UserId));
                }

                if (policy.ApplyAuditAop)
                {
                    // 审计字段自动填充:业务代码只管业务字段,基建字段框架兜底(见 BaseEntity 注释)
                    client.Aop.DataExecuting = (_, info) =>
                    {
                        switch (info.OperationType)
                        {
                            case DataFilterType.InsertByObject:
                                // Id 未指定(=0)→ 填雪花号;种子等显式指定的 Id 原样保留。
                                // 按 PrimaryId 匹配(而非 BaseEntity):BaseEntity : PrimaryId,故老实体一并覆盖,
                                // 且明细/子表(仅继承 PrimaryId,无审计字段,#8)插入时同样自动获得雪花 Id。
                                if (info is { PropertyName: nameof(PrimaryId.Id), EntityValue: PrimaryId { Id: 0 } })
                                    info.SetValue(idGen.NextId());
                                // CreateTime 未指定 → 填当前时间
                                else if (info is { PropertyName: nameof(AuditEntity.CreateTime), EntityValue: AuditEntity { CreateTime: var ct } } && ct == default)
                                    info.SetValue(time.GetLocalNow().DateTime);
                                // CreateUserId 未指定 → 填当前登录用户(系统上下文为 null 则留空,不硬塞)
                                else if (info is { PropertyName: nameof(AuditEntity.CreateUserId), EntityValue: AuditEntity { CreateUserId: null } } && currentUser.UserId is { } insUid)
                                    info.SetValue(insUid);
                                // CreateOrgId 未指定(实现 IOrgScoped 的实体有此列:DataEntity / OrgAuditEntity)→ 填当前用户归属机构(数据范围锚点,§6);
                                // 无机构上下文(系统/无 org 用户)则留空。缺此填充则机构维度数据范围对业务表恒 0 行。
                                // 按接口而非 DataEntity 基类匹配,故不软删的机构实体(OrgAuditEntity)同样自动填充。
                                else if (info is { PropertyName: nameof(IOrgScoped.CreateOrgId), EntityValue: IOrgScoped { CreateOrgId: null } } && currentUser.OrgId is { } insOrgId)
                                    info.SetValue(insOrgId);
                                break;

                            case DataFilterType.UpdateByObject:
                                // 每次整行更新都刷新 UpdateTime
                                if (info is { PropertyName: nameof(AuditEntity.UpdateTime), EntityValue: AuditEntity })
                                    info.SetValue(time.GetLocalNow().DateTime);
                                // 每次整行更新记录操作人(有登录上下文时)
                                else if (info is { PropertyName: nameof(AuditEntity.UpdateUserId), EntityValue: AuditEntity } && currentUser.UserId is { } updUid)
                                    info.SetValue(updUid);
                                break;
                        }
                    };
                }

                // ── SQL 诊断日志 ──
                // 输出只走 ILogger。**绝不能把这两条写进 SysOpLog**:那条 INSERT 自己又会触发一次
                // OnLogExecuted / 可能再失败触发 OnError —— 直接递归。日志的归日志(诊断),审计的归审计(sys_op_log)。

                // 失败的 SQL:没有这一条,线上查询一炸就只剩驱动层异常 —— 没有语句、没有参数,复现无从谈起。
                // 不给关的开关:失败却打不出 SQL,等于没有可运维性。主库与副库一律挂上。
                client.Aop.OnError = ex =>
                    sqlLog.LogError(ex, "SQL 执行失败[{ConfigId}]: {Sql} | 参数: {Parameters}",
                        configId, ex.Sql, FormatSqlParameters(ex.Parametres));

                // 慢 SQL:阈值内的语句一条都不打,否则日志被淹掉(每个请求都有若干条 SQL)。
                if (policy.SlowSqlMillis > 0)
                {
                    var threshold = policy.SlowSqlMillis;
                    client.Aop.OnLogExecuted = (sql, pars) =>
                    {
                        var elapsed = client.Ado.SqlExecutionTime;
                        if (elapsed.TotalMilliseconds < threshold) return;
                        sqlLog.LogWarning("慢 SQL[{ConfigId}]({Elapsed}ms ≥ {Threshold}ms): {Sql} | 参数: {Parameters}",
                            configId, (long)elapsed.TotalMilliseconds, threshold, sql, FormatSqlParameters(pars));
                    };
                }
            }

            // SqlSugar 5.1.4.198 G7 spike 结论:
            // - config-action 在多 ConnectionConfig 下**只会**对默认主连接触发(AsyncLocal 每上下文一次);
            // - 直接 GetConnection(副库) 不会跑 action;新 AsyncLocal 上下文会拿到新的副库 client 实例。
            // 对策:action 给主连接挂钩后,再 GetConnectionScope 各副库并挂策略。
            // **每个副库 client 实例只挂一次**:禁止 QueryFilter.Clear / 清空 AOP(会抹消费方钩子)。
            // 查表+挂钩必须在同一把锁内完成(Codex P1):TryGetValue→Attach→Add 非原子时,
            // 两线程可能对同一 client 双挂软删过滤器并互相覆盖 OnError/DataExecuting。
            // 新上下文新实例再挂一次,跨 SuppressFlow 仍生效。单库 additionals 空 = 历史路径。
            var sideHooksAttached = new ConditionalWeakTable<object, object>();
            var attachedMarker = new object();
            var sideHooksGate = new object();
            SqlSugarScope? scopeRef = null;
            scopeRef = new SqlSugarScope(configs, client =>
            {
                var configId = client.CurrentConnectionConfig?.ConfigId?.ToString() ?? MainConfigId;
                if (!hookByConfigId.TryGetValue(configId, out var policy))
                    policy = HookPolicy.ForAdditionalBare();
                AttachHooks(client, configId, policy);

                if (scopeRef is null || additionals.Count == 0) return;
                foreach (var extra in additionals)
                {
                    var side = scopeRef.GetConnectionScope(extra.ConfigId);
                    lock (sideHooksGate)
                    {
                        if (sideHooksAttached.TryGetValue(side, out _))
                            continue; // 已挂:保留消费方后续叠加的 Filter/AOP
                        AttachHooks(side, extra.ConfigId, hookByConfigId[extra.ConfigId]);
                        sideHooksAttached.Add(side, attachedMarker);
                    }
                }
            });
            return scopeRef;
        });

        // ── 泛型仓储:开放泛型一次注册,任意实体即注即用 ────────────────────────
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(SqlSugarRepository<>)));

        // ── 种子(多实现集合,用 TryAddEnumerable 防重)+ 首启初始化 ───────────
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SchemaVersionSeed>());
        services.AddHostedService<DatabaseInitializer>();

        return services;
    }

    /// <summary>校验副库列表:ConfigId 非空、唯一、不占用主库保留名、DbType 可解析、连接串非空。</summary>
    internal static void ValidateAdditionalDatabases(IReadOnlyList<AdminDatabaseConnectionOptions> additionals)
    {
        if (additionals.Count == 0) return;
        // 保留名与唯一性按忽略大小写:避免 tenonadmin / AUDIT+audit 绕过(Codex P2)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < additionals.Count; i++)
        {
            var item = additionals[i] ?? throw new InvalidOperationException(
                $"TenonAdmin:AdditionalDatabases[{i}] 为 null。");
            var id = item.ConfigId?.Trim() ?? "";
            if (id.Length == 0)
                throw new InvalidOperationException(
                    $"TenonAdmin:AdditionalDatabases[{i}].ConfigId 不能为空。");
            if (string.Equals(id, MainConfigId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"TenonAdmin:AdditionalDatabases[{i}].ConfigId 不能为保留值 \"{MainConfigId}\"(主库专用,大小写不敏感)。");
            if (!seen.Add(id))
                throw new InvalidOperationException(
                    $"TenonAdmin:AdditionalDatabases 存在重复 ConfigId \"{id}\"(大小写不敏感)。");
            // 回写 Trim 后的 Id,避免配置两侧空白导致 GetConnection 对不上(大小写保持用户写法,与 SqlSugar 一致)
            item.ConfigId = id;
            if (string.IsNullOrWhiteSpace(item.ConnectionString))
                throw new InvalidOperationException(
                    $"TenonAdmin:AdditionalDatabases[{i}](ConfigId={id}) ConnectionString 不能为空。");
            if (!Enum.TryParse<DbType>(item.DbType, ignoreCase: true, out _))
                throw new InvalidOperationException(
                    $"TenonAdmin:AdditionalDatabases[{i}](ConfigId={id}) 未知 DbType \"{item.DbType}\"。" +
                    "支持: Sqlite | MySql | SqlServer | PostgreSQL。");
        }
    }

    private static ConnectionConfig BuildConnectionConfig(string configId, string dbType, string connectionString) =>
        new()
        {
            ConfigId = configId,
            DbType = Enum.Parse<DbType>(dbType, ignoreCase: true),
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            // SqlServer CodeFirst 默认把 string 建成 varchar,存中文丢成 "??"(其他方言用 Unicode 类型,无此坑)。
            // 打开后 string 列建为 nvarchar,跨方言统一走 Unicode。ponytail: SqlSugar 内置开关,一行胜过逐实体标 [SugarColumn]。
            // 注意:显式 ColumnDataType="text" 仍会建成非 Unicode 的 text,不受本开关影响——超长列须用
            // StaticConfig.CodeFirst_BigString(SqlServer 上解析为 nvarchar(max);见 SysJobLog.ErrorText)。
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        };

    /// <summary>
    /// SQL 参数渲染成 <c>@p0=1, @name=张三</c>。
    /// <para>没有参数值,同一条失败的 SQL 就复现不出来 —— 只有语句是不够的。</para>
    /// <para>ponytail: 明文打参数值。内核不会把口令原文交给 SQL(登录是取出用户再在代码里校验哈希),
    /// 所以这里不做脱敏;消费者若把敏感值直接写进业务表,自行调 <c>TenonAdmin.Sql</c> 这个类别的日志级别。</para>
    /// </summary>
    private static string FormatSqlParameters(object? parameters) => parameters switch
    {
        SugarParameter[] ps when ps.Length > 0 => string.Join(", ", ps.Select(p => $"{p.ParameterName}={p.Value}")),
        _ => "(无)",
    };

    /// <summary>
    /// SQLite 连接串里的相对 <c>Data Source</c> 规整为相对 <paramref name="contentRoot"/> 的绝对路径。
    /// 非 SQLite / 无 ContentRoot / 已是绝对路径 / <c>:memory:</c> 一律原样返回(幂等)。
    /// </summary>
    internal static string ResolveSqlitePath(string dbType, string conn, string? contentRoot)
    {
        if (contentRoot is null || !dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) return conn;
        const string marker = "Data Source=";
        var idx = conn.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return conn;
        var start = idx + marker.Length;
        var rest = conn[start..];
        var semi = rest.IndexOf(';');
        var path = (semi < 0 ? rest : rest[..semi]).Trim();
        if (path.Length == 0 || path.Equals(":memory:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(path)) return conn;
        var abs = Path.GetFullPath(Path.Combine(contentRoot, path));
        return conn[..start] + abs + (semi < 0 ? "" : rest[semi..]);
    }

    /// <summary>SQLite 只建文件不建目录:从连接串解析出目录并补建(主库与副库共用)。</summary>
    internal static void EnsureSqliteDirectory(string dbType, string connectionString)
    {
        if (!dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) return;
        const string marker = "Data Source=";
        var idx = connectionString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var path = connectionString[(idx + marker.Length)..].Split(';')[0].Trim();
        if (path.Length == 0 || path.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>单条连接的钩子策略(主库全开 / 副库按 Options)。</summary>
    private readonly record struct HookPolicy(
        bool ApplySoftDeleteFilter,
        bool ApplyDataScopeFilter,
        bool ApplyAuditAop,
        int SlowSqlMillis)
    {
        public static HookPolicy ForMain(int slowSqlMillis) =>
            new(ApplySoftDeleteFilter: true, ApplyDataScopeFilter: true, ApplyAuditAop: true, SlowSqlMillis: slowSqlMillis);

        public static HookPolicy ForAdditional(AdminDatabaseConnectionOptions o) =>
            new(o.ApplySoftDeleteFilter, o.ApplyDataScopeFilter, o.ApplyAuditAop, o.SlowSqlMillis);

        /// <summary>未知 ConfigId 的安全兜底:不挂业务钩子。</summary>
        public static HookPolicy ForAdditionalBare() =>
            new(ApplySoftDeleteFilter: false, ApplyDataScopeFilter: false, ApplyAuditAop: false, SlowSqlMillis: 0);
    }
}
