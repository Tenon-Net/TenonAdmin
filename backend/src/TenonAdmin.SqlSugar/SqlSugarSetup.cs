using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;

namespace TenonAdmin.SqlSugar;

/// <summary>SqlSugar 单例(SqlSugarScope,线程安全)注册。设计:单实例 + CodeFirst + 仓储。</summary>
public static class SqlSugarSetup
{
    public static IServiceCollection AddTenonAdminSqlSugar(this IServiceCollection services, AdminDatabaseOptions db)
    {
        var dbType = Enum.Parse<DbType>(db.DbType, ignoreCase: true);
        var config = new ConnectionConfig
        {
            ConfigId = "TenonAdmin",
            DbType = dbType,
            ConnectionString = db.ConnectionString,
            IsAutoCloseConnection = true,
        };
        // SqlSugarScope 是官方推荐的线程安全单例形态
        services.AddSingleton<ISqlSugarClient>(_ => new SqlSugarScope(config));
        return services;
    }
}
