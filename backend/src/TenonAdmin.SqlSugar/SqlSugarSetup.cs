using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        // WorkerId 暂固定 0(单机);多实例部署的 WorkerId 配置项随部署文档一起补
        services.TryAddSingleton<IIdGenerator>(_ => new SnowflakeIdGenerator());

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
            };

            var idGen = sp.GetRequiredService<IIdGenerator>();
            var time = sp.GetService<TimeProvider>() ?? TimeProvider.System; // 统一时间源,可测试(设计 §12)
            var scope = sp.GetRequiredService<IDataScopeContext>();          // 数据范围载体(单例,过滤器闭包捕获)
            var currentUser = sp.GetRequiredService<ICurrentUser>();         // 当前用户(单例,审计字段填充用)

            // 第二参数对每个上下文生效(Scope 内部按线程建 Client,钩子逐个挂上)
            return new SqlSugarScope(config, client =>
            {
                // 全局软删过滤器:实现 ISoftDelete 的实体,查询自动排除已删行(设计 §12)
                client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);

                // 全局数据范围过滤器(§6 招牌能力):对 IOrgScoped 实体(DataEntity 及其子类)按当前请求生效范围过滤。
                // 按接口匹配(SqlSugar 不对基类生效,只认接口/精确类型,与软删过滤器一致)。
                // 表达式里 scope.Current 的三个属性都与实体参数无关,SqlSugar 先本地求值成常量(机构集合 → SQL IN),
                // 再与实体相关的部分拼成 WHERE;Unrestricted 时整体恒真(不过滤)。
                client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
                    scope.Current.IsUnrestricted
                    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
                    || (scope.Current.IncludeSelf && e.CreateUserId == scope.Current.UserId));

                // 审计字段自动填充:业务代码只管业务字段,基建字段框架兜底(见 BaseEntity 注释)
                client.Aop.DataExecuting = (_, info) =>
                {
                    switch (info.OperationType)
                    {
                        case DataFilterType.InsertByObject:
                            // Id 未指定(=0)→ 填雪花号;种子等显式指定的 Id 原样保留
                            if (info is { PropertyName: nameof(BaseEntity.Id), EntityValue: BaseEntity { Id: 0 } })
                                info.SetValue(idGen.NextId());
                            // CreateTime 未指定 → 填当前时间
                            else if (info is { PropertyName: nameof(BaseEntity.CreateTime), EntityValue: BaseEntity { CreateTime: var ct } } && ct == default)
                                info.SetValue(time.GetLocalNow().DateTime);
                            // CreateUserId 未指定 → 填当前登录用户(系统上下文为 null 则留空,不硬塞)
                            else if (info is { PropertyName: nameof(BaseEntity.CreateUserId), EntityValue: BaseEntity { CreateUserId: null } } && currentUser.UserId is { } insUid)
                                info.SetValue(insUid);
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
