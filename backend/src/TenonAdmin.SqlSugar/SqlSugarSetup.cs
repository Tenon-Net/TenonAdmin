using System.Reflection;
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
/// </summary>
public static class SqlSugarSetup
{
    public static IServiceCollection AddTenonAdminSqlSugar(
        this IServiceCollection services,
        AdminDatabaseOptions db,
        IEnumerable<Assembly>? entityAssemblies = null)
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

        // ── SqlSugar 客户端:官方推荐的线程安全单例形态 SqlSugarScope ───────────
        services.TryAddSingleton<ISqlSugarClient>(sp =>
        {
            // SQLite 相对路径规整为相对 ContentRoot 的绝对路径(P2-3):否则从非项目目录/服务托管启动时,
            // 库与 EnsureSqliteDirectory 建的目录会落在意外位置。就地回写 options 单例,供 DatabaseInitializer 一致读取。
            db.ConnectionString = ResolveSqlitePath(db.DbType, db.ConnectionString, sp.GetService<IHostEnvironment>()?.ContentRootPath);
            var config = new ConnectionConfig
            {
                ConfigId = "TenonAdmin",
                DbType = Enum.Parse<DbType>(db.DbType, ignoreCase: true),
                ConnectionString = db.ConnectionString,
                IsAutoCloseConnection = true,
                // SqlServer CodeFirst 默认把 string 建成 varchar,存中文丢成 "??"(其他方言用 Unicode 类型,无此坑)。
                // 打开后 string 列建为 nvarchar,跨方言统一走 Unicode。ponytail: SqlSugar 内置开关,一行胜过逐实体标 [SugarColumn]。
                MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
            };

            var idGen = sp.GetRequiredService<IIdGenerator>();
            var time = sp.GetService<TimeProvider>() ?? TimeProvider.System; // 统一时间源,可测试(设计 §12)
            var scope = sp.GetRequiredService<IDataScopeContext>();          // 数据范围载体(单例,过滤器闭包捕获)
            var currentUser = sp.GetRequiredService<ICurrentUser>();         // 当前用户(单例,审计字段填充用)
            // SQL 诊断日志。类别名固定,便于消费者单独调级别(Logging:LogLevel:TenonAdmin.Sql)。
            // GetService 而非 GetRequiredService:本方法是公开装配入口,允许在裸容器上单独调用(测试与只要数据层的消费者
            // 都这么用),不能凭空多出一个必需依赖 —— 没有日志就静默不打,不该因此起不来。
            var sqlLog = (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("TenonAdmin.Sql");

            // 第二参数对每个上下文生效(Scope 内部按线程建 Client,钩子逐个挂上)
            return new SqlSugarScope(config, client =>
            {
                // 全局软删过滤器:实现 ISoftDelete 的实体,查询自动排除已删行(设计 §12)
                client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);

                // 全局数据范围过滤器(§6 招牌能力):对 IOrgScoped 实体(DataEntity 及其子类)按当前请求生效范围过滤。
                // 按接口匹配(SqlSugar 不对基类生效,只认接口/精确类型,与软删过滤器一致)。
                // 表达式里 scope.Current 的三个属性都与实体参数无关,SqlSugar 先本地求值成常量(机构集合 → SQL IN),
                // 再与实体相关的部分拼成 WHERE;Unrestricted 时整体恒真(不过滤)。
                // 两个布尔标记写成 `== true` 而非裸布尔:SqlServer 的谓词上下文不接受裸标量(裸 1/0 → "非布尔类型的表达式"),
                // 必须是比较式(渲染成 `@p = 1`)。软删过滤器 `e.IsDelete == false` 同理已是比较式,故本就跨方言可用。
                client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
                    scope.Current.IsUnrestricted == true
                    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
                    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));

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
                            else if (info is { PropertyName: nameof(BaseEntity.CreateTime), EntityValue: BaseEntity { CreateTime: var ct } } && ct == default)
                                info.SetValue(time.GetLocalNow().DateTime);
                            // CreateUserId 未指定 → 填当前登录用户(系统上下文为 null 则留空,不硬塞)
                            else if (info is { PropertyName: nameof(BaseEntity.CreateUserId), EntityValue: BaseEntity { CreateUserId: null } } && currentUser.UserId is { } insUid)
                                info.SetValue(insUid);
                            // CreateOrgId 未指定(仅 DataEntity 有此列)→ 填当前用户归属机构(数据范围锚点,§6);
                            // 无机构上下文(系统/无 org 用户)则留空。缺此填充则机构维度数据范围对业务表恒 0 行。
                            else if (info is { PropertyName: nameof(DataEntity.CreateOrgId), EntityValue: DataEntity { CreateOrgId: null } } && currentUser.OrgId is { } insOrgId)
                                info.SetValue(insOrgId);
                            break;

                        case DataFilterType.UpdateByObject:
                            // 每次整行更新都刷新 UpdateTime
                            if (info is { PropertyName: nameof(BaseEntity.UpdateTime), EntityValue: BaseEntity })
                                info.SetValue(time.GetLocalNow().DateTime);
                            // 每次整行更新记录操作人(有登录上下文时)
                            else if (info is { PropertyName: nameof(BaseEntity.UpdateUserId), EntityValue: BaseEntity } && currentUser.UserId is { } updUid)
                                info.SetValue(updUid);
                            break;
                    }
                };

                // ── SQL 诊断日志 ──
                // 输出只走 ILogger。**绝不能把这两条写进 SysOpLog**:那条 INSERT 自己又会触发一次
                // OnLogExecuted / 可能再失败触发 OnError —— 直接递归。日志的归日志(诊断),审计的归审计(sys_op_log)。

                // 失败的 SQL:没有这一条,线上查询一炸就只剩驱动层异常 —— 没有语句、没有参数,复现无从谈起。
                // 不给关的开关:失败却打不出 SQL,等于没有可运维性。
                client.Aop.OnError = ex =>
                    sqlLog.LogError(ex, "SQL 执行失败: {Sql} | 参数: {Parameters}",
                        ex.Sql, FormatSqlParameters(ex.Parametres));

                // 慢 SQL:阈值内的语句一条都不打,否则日志被淹掉(每个请求都有若干条 SQL)。
                if (db.SlowSqlMillis > 0)
                    client.Aop.OnLogExecuted = (sql, pars) =>
                    {
                        var elapsed = client.Ado.SqlExecutionTime;
                        if (elapsed.TotalMilliseconds < db.SlowSqlMillis) return;
                        sqlLog.LogWarning("慢 SQL({Elapsed}ms ≥ {Threshold}ms): {Sql} | 参数: {Parameters}",
                            (long)elapsed.TotalMilliseconds, db.SlowSqlMillis, sql, FormatSqlParameters(pars));
                    };
            });
        });

        // ── 泛型仓储:开放泛型一次注册,任意实体即注即用 ────────────────────────
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(SqlSugarRepository<>)));

        // ── 种子(多实现集合,用 TryAddEnumerable 防重)+ 首启初始化 ───────────
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SchemaVersionSeed>());
        services.AddHostedService<DatabaseInitializer>();

        return services;
    }

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
}
