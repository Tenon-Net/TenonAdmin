# Data Layer and Auditing

The line of business code that queries orders writes neither `IsDelete == false` nor any org condition, yet both conditions reach the final SQL. `SqlSugarSetup` put them there: the whole process holds a single `SqlSugarScope`, and its query filters and audit AOP are attached once when it's constructed, so from then on nothing slips past them.

## One `SqlSugarScope` singleton

`ISqlSugarClient` is registered as a singleton in the form of `SqlSugarScope` — the thread-safe form officially recommended by SqlSugar (it builds a Client per thread internally). At construction time, three sets of hooks are attached in one pass: global filters, audit AOP, and SQL diagnostic logging.

```csharp
// backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs
services.TryAddSingleton<ISqlSugarClient>(sp =>
{
    var config = new ConnectionConfig
    {
        ConfigId = "TenonAdmin",
        DbType = Enum.Parse<DbType>(db.DbType, ignoreCase: true),
        ConnectionString = db.ConnectionString,
        IsAutoCloseConnection = true,
        // SqlServer CodeFirst defaults string columns to varchar, which mangles Chinese text into "??"; turning this on builds nvarchar instead
        MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
    };
    return new SqlSugarScope(config, client => { /* attach filters + AOP + logging */ });
});
```

## Global query filters

Both filters are registered against entities matched **by interface**. SqlSugar's `AddTableFilter<T>` only recognizes an interface or an exact type, not a base class — which is why the markers go through the interfaces `ISoftDelete` / `IOrgScoped` rather than the base classes `BaseEntity` / `DataEntity`.

### Soft delete

For entities implementing `ISoftDelete`, queries automatically exclude deleted rows:

```csharp
client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);
```

Deleted data is naturally invisible to every query. To query deleted data when genuinely needed, explicitly lift the filter with `.ClearFilter<ISoftDelete>()`.

### Data scope

For `IOrgScoped` entities (i.e. `DataEntity` and its subclasses), results are filtered by the effective org set resolved for the current request:

```csharp
client.QueryFilter.AddTableFilter<IOrgScoped>(e =>
    scope.Current.IsUnrestricted == true
    || (e.CreateOrgId != null && scope.Current.OrgIds.Contains(e.CreateOrgId.Value))
    || (scope.Current.IncludeSelf == true && e.CreateUserId == scope.Current.UserId));
```

The three `scope.Current` properties in the expression are independent of the entity parameter, so SqlSugar evaluates them locally into constants first (the org set is rendered as a SQL `IN`), then splices them into the WHERE clause. When `IsUnrestricted`, the whole predicate is always true — no filtering happens.

::: warning Data scope only applies to queries
The global data-scope filter only applies to SELECT — not to primary-key-based `Updateable` / `Deleteable`. To cover this, `SqlSugarRepository<TEntity>` has a built-in write-path scope guard for `UpdateAsync` / `DeleteAsync` on `IOrgScoped` entities: before writing, it queries through the scope-filtered path to confirm the target row is within the current data scope; attempting to modify/delete a row from another org returns 0 rows — secure by default. Writes that bypass the repository via the `Db.Updateable` / `Deleteable` escape hatch aren't covered by this guard and must validate ownership themselves.
:::

## AOP auto-fills audit fields

`SqlSugarScope`'s `Aop.DataExecuting` hook backstops the infrastructure fields on insert/update. Business code never touches these fields.

| Field | Timing | Fill rule |
| --- | --- | --- |
| `Id` | Insert | Filled with a snowflake Id when `Id == 0`; an explicitly given value (e.g. seed data) is preserved as-is |
| `CreateTime` | Insert | Filled with the current time if unset |
| `CreateUserId` | Insert | Filled from the current logged-in user; left empty for a system context |
| `CreateOrgId` | Insert | Filled from the current user's owning org (only present on `DataEntity`) |
| `UpdateTime` | Update | Refreshed on every full-row update |
| `UpdateUserId` | Update | Records the operator when a login context is present |

```csharp
client.Aop.DataExecuting = (_, info) =>
{
    switch (info.OperationType)
    {
        case DataFilterType.InsertByObject:
            if (info is { PropertyName: nameof(BaseEntity.Id), EntityValue: BaseEntity { Id: 0 } })
                info.SetValue(idGen.NextId());
            // …CreateTime / CreateUserId / CreateOrgId backstopped the same way
            break;
        case DataFilterType.UpdateByObject:
            // …UpdateTime refreshed every time, UpdateUserId records the operator
            break;
    }
};
```

::: warning `CreateOrgId` is the data-scope anchor
`CreateOrgId` = the org the creator belonged to at creation time, auto-filled by the AOP hook from `ICurrentUser.OrgId` (the token's `org` claim) on insert. The data-scope filter uses exactly this field to decide row visibility. **If this field isn't filled, org-dimension data-scope queries will return 0 rows for a business table, always** — the data is in the database, but unreachable by query. `null` means the row isn't constrained by org scope (built-in system data, or a creator with no owning org).
:::

## Entity base classes

Business entities choose one of two base classes:

- `BaseEntity` — primary key `Id` + the audit quartet (`CreateTime` / `CreateUserId` / `UpdateTime` / `UpdateUserId`) + soft delete `IsDelete`. Implements `ISoftDelete`. Used by tables that don't need org isolation (global dictionaries, the org tree itself).
- `DataEntity` — inherits `BaseEntity`, adds the data-scope anchor `CreateOrgId`, implements `IOrgScoped`. Used by business tables that need "current org / current org and below / self only / custom" isolation.

## Generic repository `IRepository<>`

The standard entry point for data access — an open generic registered once, ready to use for any entity. Just inject it through the constructor:

```csharp
public class DeviceService(IRepository<Device> repo) : IDeviceService
{
    public Task<Device?> Get(long id) => repo.GetByIdAsync(id);  // automatically carries soft-delete + data-scope filters
}
```

Every query automatically carries the global filters. For complex operations the repository can't cover (joins, transactions, batches), use SqlSugar's native capabilities directly via `repo.Db` — the repository is a convenience layer, not an abstraction that locks the ORM in a cage. Common methods: `AsQueryable` / `GetByIdAsync` / `GetFirstAsync` / `AnyAsync` / `InsertAsync` / `InsertRangeAsync` / `UpdateAsync` / `DeleteAsync` (soft delete) / `HardDeleteAsync` (physical delete) / `RestoreAsync` (restore).

## Snowflake `WorkerId`

Primary key `Id` values are produced by `IIdGenerator`, whose default implementation is a self-written, single-file snowflake algorithm `SnowflakeIdGenerator` (zero third-party dependencies). 64-bit layout:

```text
┌─1 bit──┬─────────41 bit─────────┬──6 bit──┬──6 bit──┐
│ sign 0 │ ms timestamp from epoch │ worker  │ seq/ms  │
└────────┴─────────────────────────┴─────────┴─────────┘
```

The fixed 12 low-order bits (6 for machine + 6 for sequence) weren't chosen arbitrarily: 41+12=53, so every ID stays below 2^53, within JS's `Number.MAX_SAFE_INTEGER` — the frontend can parse a `long` primary key as a plain number without losing precision. Capacity: 64 machines, 64 IDs per machine per millisecond.

Those same 12 bits also carve out the seeds' territory: `id = milliseconds-from-epoch × 4096 + low bits`, so a snowflake can never issue a number below 4096, which leaves `[1, 4095]` for seed data's fixed Ids (the kernel uses `[1, 999]`; consumers allocate their numbers from `1000` up). On startup, `DatabaseInitializer` verifies that every seed Id falls within this range and isn't reused within a single entity: an out-of-range Id (which a snowflake would sooner or later catch up to and collide with on the primary key) or a duplicate (whose idempotent existence check would silently skip the later row as "already present") both throw at startup, and CI carries cases that catch this class of error before the host even boots.

The worker number comes from config `TenonAdmin:Id:WorkerId` (default 0, range 0–63):

```json
{
  "TenonAdmin": {
    "Id": { "WorkerId": 3 }
  }
}
```

::: danger Every instance must differ under horizontal scaling
For a single-machine deployment, leaving it unset (falling back to 0) is fine. **When scaling horizontally across multiple instances, each instance must be configured with a different `WorkerId`** — otherwise two instances issuing IDs in the same millisecond will collide on the primary key. This is a data-corruption-class problem, and it happens silently by default.

The kernel provides one line of defense: if Redis caching is chosen (a clear sign of multi-instance intent) but `WorkerId` isn't set explicitly, startup throws immediately — turning a silent primary-key collision into a readable startup error. For a genuinely single-instance deployment, set it to `0` explicitly to signal intent; on k8s, a StatefulSet's pod ordinal can be injected.
:::

On clock safety: `SnowflakeIdGenerator` takes an injected `TimeProvider` (testable). On detecting a clock rollback, it spin-waits briefly (≤5ms, NTP-adjustment scale) to catch back up; on a large rollback, it throws outright and refuses to issue an ID — it never issues an ID that might be a duplicate.
