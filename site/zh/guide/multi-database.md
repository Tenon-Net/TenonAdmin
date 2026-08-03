# 配置多数据库（多 ConfigId）

主库仍是 `TenonAdmin:Database` 那一条。要再挂日志库、遗留库、只读副本，写 `TenonAdmin:AdditionalDatabases`，用 `db.AsTenant().GetConnection("名字")` 访问。

这和[快速开始](/zh/guide/getting-started)里的「换方言」不是一回事：换方言是**一条连接**从 SQLite 改成 MySQL；这里是**同进程多条连接同时在线**。

## 什么时候该配

| 你要做的事 | 做法 |
| --- | --- |
| 审计 / 任务历史写放大，想和 sys_* 分库 | 副库 `ConfigId` + 代码里 `GetConnection` 写入 |
| 某模块必须连遗留库、报表库 | 同上；钩子默认关，避免误套软删和机构过滤 |
| 先挂一个从库名，读写策略自己写 | 只配连接串，路由逻辑放业务代码 |

内核**不会**按租户自动切库，也**不会**让 `IRepository<T>` 按实体自动打到副库。跨库事务、内置读写分离策略都不在范围内。

## 配置

### appsettings

```json
{
  "TenonAdmin": {
    "Database": {
      "DbType": "Sqlite",
      "ConnectionString": "Data Source=./data/admin.db",
      "EnableCodeFirst": true,
      "EnableSeed": true
    },
    "AdditionalDatabases": [
      {
        "ConfigId": "Audit",
        "DbType": "Sqlite",
        "ConnectionString": "Data Source=./data/audit.db",
        "ApplyAuditAop": true
      },
      {
        "ConfigId": "Legacy",
        "DbType": "MySql",
        "ConnectionString": "Server=...;Database=legacy;User ID=...;Password=...;",
        "ApplySoftDeleteFilter": false,
        "ApplyDataScopeFilter": false,
        "ApplyAuditAop": false
      }
    ]
  }
}
```

主库形状不变。副库是数组，可以挂多条。Worker 宿主走 `AddTenonAdminWorker` 时，同一套 `TenonAdmin` 节同样生效。

### 代码里追加

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, opt =>
{
    opt.AdditionalDatabases.Add(new AdminDatabaseConnectionOptions
    {
        ConfigId = "Audit",
        DbType = "Sqlite",
        ConnectionString = "Data Source=./data/audit.db",
        ApplyAuditAop = true,
    });
});
```

### 字段

| 字段 | 必填 | 默认 | 说明 |
| --- | --- | --- | --- |
| `ConfigId` | 是 | — | 连接名。全局唯一（大小写不敏感）；**不能**叫 `TenonAdmin`（主库保留，`tenonadmin` 也会拒） |
| `DbType` | 是 | `Sqlite` | `Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` |
| `ConnectionString` | 是 | — | 连接串。SQLite 相对路径按 ContentRoot 解析，并自动建父目录 |
| `ApplySoftDeleteFilter` | 否 | `false` | 是否挂软删过滤。遗留库常无 `IsDelete`，误开会查炸 |
| `ApplyDataScopeFilter` | 否 | `false` | 是否挂机构数据范围。外部库通常关 |
| `ApplyAuditAop` | 否 | `false` | 是否自动填雪花 Id 和审计字段。**关时插入必须自己给主键** |
| `SlowSqlMillis` | 否 | `0` | 慢 SQL 阈值（毫秒）；`≤0` 关闭。失败 SQL 的 Error 日志始终开 |

副库**没有** `EnableCodeFirst` / `EnableSeed`。主库 CodeFirst 和种子仍然只打主库。

### 配错会启动失败

下列情况在 `AddTenonAdmin` 时直接抛 `InvalidOperationException`：

- 数组元素为 null  
- `ConfigId` 空白  
- 占用保留名 `TenonAdmin`（忽略大小写）  
- `ConfigId` 重复（忽略大小写，如 `Audit` 与 `audit`）  
- `ConnectionString` 空白  
- 无法识别的 `DbType`  

## 访问副库

注入 `ISqlSugarClient`。默认连接永远是主库。

```csharp
public class AuditWriter(ISqlSugarClient db, IIdGenerator ids)
{
    public async Task WriteAsync(string message)
    {
        var audit = db.AsTenant().GetConnection("Audit");
        // 也可: db.AsTenant().GetConnectionScope("Audit")

        await audit.Insertable(new AuditLog
        {
            Id = ids.NextId(), // ApplyAuditAop 为 false 时必须自己填
            Message = message,
            CreateTime = DateTime.Now,
        }).ExecuteCommandAsync();
    }
}
```

| API | 打到哪 |
| --- | --- |
| `IRepository<T>`、默认 `db.Queryable` | 主库 |
| `db.AsTenant().GetConnection("Audit")` | 副库 `Audit` |

在 `AddTenonAdmin` **之前**整包 `TryAddSingleton<ISqlSugarClient>` 替换后，内置装配被跳过，`AdditionalDatabases` **不会**再生效，多库由你自己管。

## 钩子

| 钩子 | 主库 | 副库默认 |
| --- | --- | --- |
| 软删 | 开 | 关（`ApplySoftDeleteFilter: true` 打开） |
| 数据范围 | 开 | 关 |
| 审计 AOP | 开 | 关 |
| 失败 SQL 日志 | 开 | 开 |
| 慢 SQL | `Database.SlowSqlMillis`（默认 1000） | 项内 `SlowSqlMillis`（默认 0） |

## 纪律

1. **副库实体不要**放进 `options.ApplicationAssemblies`。主库 CodeFirst 会扫到，在主库误建表。  
2. 副库建表、迁移、种子自己做：`GetConnection(...).CodeFirst.InitTables(...)` 或 DBA 脚本。  
3. `GetConnection("…")` 的字符串要和配置里的 `ConfigId` 一致（校验唯一性忽略大小写，取连接按你写入的原样）。  
4. 配了副库时，启动日志会有一条 Information，提示访问方式和实体登记纪律。

## 最小示例：日志拆库

**配置**见上文 `Audit` 那一段。

**实体**（不要登记进 `ApplicationAssemblies`）：

```csharp
[SugarTable("biz_audit_log")]
public class AuditLog
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 512)]
    public string Message { get; set; } = "";

    public DateTime CreateTime { get; set; }
}
```

**首次建表 + 写入**（启动 HostedService 或模块初始化里做一次即可）：

```csharp
var audit = db.AsTenant().GetConnection("Audit");
audit.CodeFirst.InitTables<AuditLog>();

await audit.Insertable(new AuditLog
{
    // ApplyAuditAop=true 时 Id==0 会填雪花
    Message = "hello",
    CreateTime = DateTime.Now,
}).ExecuteCommandAsync();
```

## 和数据层其它机制的关系

软删、审计 AOP、机构数据范围的主库行为见[数据层与审计](/zh/backend/data-layer)。多组织权限模型见[多组织数据权限](/zh/backend/data-scope)。配置节总表见[项目结构与启动](/zh/backend/structure)。
