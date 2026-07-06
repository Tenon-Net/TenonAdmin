using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // ── 实体程序集登记:本层自带 sys_schema_version;上层/用户程序集从参数并入 ──
        var sources = new TenonEntitySources();
        sources.Add(typeof(SqlSugarSetup).Assembly);
        foreach (var asm in entityAssemblies ?? []) sources.Add(asm);
        services.TryAddSingleton(sources);

        // ── SqlSugar 客户端:官方推荐的线程安全单例形态 SqlSugarScope ───────────
        var config = new ConnectionConfig
        {
            ConfigId = "TenonAdmin",
            DbType = Enum.Parse<DbType>(db.DbType, ignoreCase: true),
            ConnectionString = db.ConnectionString,
            IsAutoCloseConnection = true,
        };

        services.TryAddSingleton<ISqlSugarClient>(sp =>
        {
            var idGen = sp.GetRequiredService<IIdGenerator>();
            var time = sp.GetService<TimeProvider>() ?? TimeProvider.System; // 统一时间源,可测试(设计 §12)

            // 第二参数对每个上下文生效(Scope 内部按线程建 Client,钩子逐个挂上)
            return new SqlSugarScope(config, client =>
            {
                // 全局软删过滤器:实现 ISoftDelete 的实体,查询自动排除已删行(设计 §12)
                client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);

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
                            break;

                        case DataFilterType.UpdateByObject:
                            // 每次整行更新都刷新 UpdateTime
                            if (info is { PropertyName: nameof(BaseEntity.UpdateTime), EntityValue: BaseEntity })
                                info.SetValue(time.GetLocalNow().DateTime);
                            break;
                    }
                    // CreateUserId / UpdateUserId 的填充在认证闭环接入后挂到这里(取当前登录用户上下文)
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
}
