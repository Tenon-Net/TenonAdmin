# Configure Multiple Databases (Multi-ConfigId)

The main database is still `TenonAdmin:Database`. To attach a log store, legacy database, or read replica, add `TenonAdmin:AdditionalDatabases` and call `db.AsTenant().GetConnection("name")`.

This is not the same as [switching dialect](/guide/getting-started) in Quick Start. Switching dialect keeps **one** connection and changes SQLite to MySQL. Here you run **several connections in the same process**.

## When to use it

| Goal | Approach |
| --- | --- |
| Audit / job history grows fast; split from sys_* | Secondary `ConfigId` + `GetConnection` writes |
| A module must hit a legacy or reporting DB | Same; hooks default off so soft-delete/org filters are not applied by mistake |
| Register a replica ConfigId; own the R/W policy | Configure the connection only; put routing in your code |

The kernel does **not** auto-switch databases per tenant, and `IRepository<T>` does **not** route entities to a secondary. Cross-database transactions and a built-in read/write split policy are out of scope.

## Configuration

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

The main-database shape is unchanged. Secondaries are an array. Worker hosts using `AddTenonAdminWorker` bind the same `TenonAdmin` section.

### Add from code

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

### Fields

| Field | Required | Default | Meaning |
| --- | --- | --- | --- |
| `ConfigId` | yes | — | Connection name. Globally unique (case-insensitive); must **not** be `TenonAdmin` (reserved; `tenonadmin` is rejected too) |
| `DbType` | yes | `Sqlite` | `Sqlite` / `MySql` / `SqlServer` / `PostgreSQL` |
| `ConnectionString` | yes | — | Connection string. SQLite relative paths resolve against ContentRoot; parent directories are created |
| `ApplySoftDeleteFilter` | no | `false` | Soft-delete filter. Legacy DBs often lack `IsDelete` |
| `ApplyDataScopeFilter` | no | `false` | Org data-scope filter. Usually off for external DBs |
| `ApplyAuditAop` | no | `false` | Auto-fill snowflake Id / audit fields. **When off, supply primary keys yourself** |
| `SlowSqlMillis` | no | `0` | Slow-SQL threshold (ms); `≤0` disables. Failed-SQL Error logging is always on |

Secondaries have **no** `EnableCodeFirst` / `EnableSeed`. Main-DB CodeFirst and seeds still target the main connection only.

### Fail-fast validation

These throw `InvalidOperationException` during `AddTenonAdmin`:

- null array element  
- blank `ConfigId`  
- reserved name `TenonAdmin` (case-insensitive)  
- duplicate `ConfigId` (case-insensitive, e.g. `Audit` and `audit`)  
- blank `ConnectionString`  
- unknown `DbType`  

## Accessing a secondary

Inject `ISqlSugarClient`. The default connection is always main.

```csharp
public class AuditWriter(ISqlSugarClient db, IIdGenerator ids)
{
    public async Task WriteAsync(string message)
    {
        var audit = db.AsTenant().GetConnection("Audit");
        // or: db.AsTenant().GetConnectionScope("Audit")

        await audit.Insertable(new AuditLog
        {
            Id = ids.NextId(), // required when ApplyAuditAop is false
            Message = message,
            CreateTime = DateTime.Now,
        }).ExecuteCommandAsync();
    }
}
```

| API | Hits |
| --- | --- |
| `IRepository<T>`, default `db.Queryable` | Main |
| `db.AsTenant().GetConnection("Audit")` | Secondary `Audit` |

If you `TryAddSingleton<ISqlSugarClient>` **before** `AddTenonAdmin`, the built-in registration is skipped and `AdditionalDatabases` has **no** effect — you own multi-DB yourself.

## Hooks

| Hook | Main | Secondary default |
| --- | --- | --- |
| Soft-delete | on | off (`ApplySoftDeleteFilter: true` to enable) |
| Data scope | on | off |
| Audit AOP | on | off |
| Failed SQL log | on | on |
| Slow SQL | `Database.SlowSqlMillis` (default 1000) | per-entry `SlowSqlMillis` (default 0) |

## Rules

1. **Do not** put secondary entities in `options.ApplicationAssemblies`. Main CodeFirst will create those tables on the **main** database.  
2. Own secondary schema: `GetConnection(...).CodeFirst.InitTables(...)` or DBA scripts.  
3. The string in `GetConnection("…")` must match the configured `ConfigId` (uniqueness checks are case-insensitive; lookup uses the string you stored).  
4. When secondaries are configured, startup logs an Information line about access and entity registration.

## Minimal example: split audit log

Use the `Audit` config block above.

Entity (**not** in `ApplicationAssemblies`):

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

Create tables once, then write:

```csharp
var audit = db.AsTenant().GetConnection("Audit");
audit.CodeFirst.InitTables<AuditLog>();

await audit.Insertable(new AuditLog
{
    // with ApplyAuditAop=true, Id==0 is filled with a snowflake
    Message = "hello",
    CreateTime = DateTime.Now,
}).ExecuteCommandAsync();
```

## Related pages

Main-DB soft-delete, audit AOP, and filters: [Data Layer & Auditing](/backend/data-layer). Org permissions: [Multi-Org Data Permissions](/backend/data-scope). Options overview: [Project Structure & Startup](/backend/structure).
