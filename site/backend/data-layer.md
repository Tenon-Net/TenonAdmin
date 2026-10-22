# Data Layer and Auditing

Business code writes only the order condition, yet soft-delete and organization predicates still reach the final SQL. Inserts likewise receive their primary key and audit fields automatically. `SqlSugarSetup` attaches these rules once to the process-wide `SqlSugarScope`. Services can focus on business fields, but code that bypasses the repository or clears a filter must take over the corresponding protection explicitly.

## One `SqlSugarScope` singleton

`ISqlSugarClient` is registered as a singleton in the form of `SqlSugarScope` — the thread-safe form officially recommended by SqlSugar (it builds a Client per thread internally). At construction time it attaches global filters, audit AOP, and SQL diagnostic logging. By default there is a single main connection whose ConfigId is fixed as `TenonAdmin`.

```csharp
// Main database (the zero-config path)
// ConfigId = "TenonAdmin"; hooks: soft-delete + data-scope + audit AOP + failed/slow SQL logging
return new SqlSugarScope(mainConfig, client => { /* attach filters + AOP + logging */ });
```

### Multiple databases (multiple ConfigIds)

The main database is still one connection. To attach a log or legacy store, use `TenonAdmin:AdditionalDatabases` and `db.AsTenant().GetConnection("Audit")`. `IRepository<T>` always hits main.

Field reference, pitfalls, and a minimal walkthrough: [Configure Multiple Databases](/guide/multi-database).

## Global query filters

Both filters are registered against entities matched **by interface**. SqlSugar's `AddTableFilter<T>` only recognizes an interface or an exact type, not a base class — which is why the markers go through the interfaces `ISoftDelete` / `IOrgScoped` rather than the base classes `BaseEntity` / `DataEntity`.

### Soft delete

For entities implementing `ISoftDelete`, queries automatically exclude deleted rows:

```csharp
client.QueryFilter.AddTableFilter<ISoftDelete>(e => e.IsDelete == false);
```

Deleted data is naturally invisible to every query. To query deleted data when genuinely needed, explicitly lift the filter with `.ClearFilter<ISoftDelete>()`.

If deleted rows appear in a list, first check that the entity implements `ISoftDelete`, then look for a call to `ClearFilter`. Filters match interfaces; merely having a property named `IsDelete` does not enable soft deletion.

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

Choose a business entity's base class by whether the data needs organization isolation and whether deleted rows must be recoverable. A wrong choice changes query filtering and `DeleteAsync` behavior, not just the available fields. Five base classes form a chain, each level adding one capability:

```text
PrimaryId          primary key Id only
  └─ AuditEntity   + the audit quartet (CreateTime / CreateUserId / UpdateTime / UpdateUserId)
       ├─ BaseEntity      + soft delete IsDelete         implements ISoftDelete
       │    └─ DataEntity      + org anchor CreateOrgId  implements IOrgScoped
       └─ OrgAuditEntity  + org anchor CreateOrgId        implements IOrgScoped
```

| Base class | Audit | Soft delete | Org isolation | Used for |
| --- | --- | --- | --- | --- |
| `PrimaryId` | No | No | No | Detail/child tables. **Can't use the built-in repository or be seeded** (`IRepository<T>` is constrained to `where T : BaseEntity`) — typically read/written alongside the parent table in the same transaction via `ISqlSugarClient` |
| `AuditEntity` | Yes | No | No | Tables that genuinely need real deletes but still want a paper trail: hard-deletable join tables, append-only log tables |
| `BaseEntity` | Yes | Yes | No | Globally shared tables: dictionaries, config, the org tree itself |
| `DataEntity` | Yes | Yes | Yes | Business tables needing "current org / current org and below / self only / custom" isolation |
| `OrgAuditEntity` | Yes | No | Yes | Tables that need org isolation but also genuinely need real deletes |

Ask two questions first: must the data be isolated by organization, and must a deletion be recoverable? Use `DataEntity` for both, `BaseEntity` for soft deletion alone, and `OrgAuditEntity` only when organization isolation is required but deletion must be physical. **For the two classes without soft delete, the repository's `DeleteAsync` is a physical delete**: the row leaves the database, with no recycle bin or `RestoreAsync`.

Both org-isolated base classes (`DataEntity` / `OrgAuditEntity`) get the write-path guard: the repository's `UpdateAsync`/`DeleteAsync` has a built-in scope check for `IOrgScoped` entities, and modifying/deleting a row from another org is rejected, returning 0 rows.

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

The fixed 12 low-order bits (6 for machine + 6 for sequence) were chosen deliberately: 41+12=53, so every ID produced by the default snowflake layout stays below 2^53, within JavaScript's `Number.MAX_SAFE_INTEGER`. Capacity is 64 machines and 64 IDs per machine per millisecond. That guarantee belongs to the default layout, not to every `long` returned by a consumer DTO, a custom generator, or another source. MVC and SignalR encode an out-of-range `long` as a decimal string, OpenAPI describes `long` as `integer|string`, and frontends must accept `number|string`.

Those same 12 bits also carve out the seeds' territory: `id = milliseconds-from-epoch × 4096 + low bits`, so a snowflake can never issue a number below 4096, and `[1, 999]` is safely reserved for the kernel's built-in seeds on that basis. Consumers allocate their numbers from `1000` up, but the ceiling isn't a hardcoded number — on startup, `DatabaseInitializer` computes "the smallest snowflake Id obtainable from this moment on" (`SnowflakeIdGenerator.CurrentFloor()`), and any seed Id strictly below that value can never collide with an Id this instance actually generates from now on, since the clock only moves forward. On startup, `DatabaseInitializer` verifies that every seed Id falls within this dynamic ceiling and isn't reused within a single entity: an out-of-range Id (which a snowflake would sooner or later catch up to and collide with on the primary key) or a duplicate (whose idempotent existence check would silently skip the later row as "already present") both throw at startup, and CI carries cases that catch this class of error before the host even boots.

The worker number comes from `TenonAdmin:Id:WorkerId` (range 0–63). A complete `AddTenonAdmin()` host first confirms a database lease in the shared `sys_worker_lease` table. With an explicit number it confirms that fixed slot; without one it scans 0 through 63, claims the first available database slot, and also takes the same-machine file lock for that number. This covers both cross-machine/container conflicts and overlapping processes on one machine. A host that installs only the data layer, without the Services lease implementation, falls back to the file lock alone.

The DI-singleton `WorkerIdAssignment` owns the file lock and database-renewal lifetime. `WorkerIdLeaseGuard` resolves the default generator before ordinary hosted services start, then performs a fresh owner confirmation after database initialization. Normal shutdown invalidates the generator immediately, stops renewal, and attempts to delete the owned lease row; if deletion fails, the database expiry remains the recovery path. `SnowflakeIdGenerator` checks the assignment both before and after calculating an ID. It refuses to issue one when the database lease has not been confirmed, has been lost or expired, or the assignment has been disposed. A transient database failure can still be retried, but ID generation stops once the last valid confirmation deadline passes.

The default file-lock directory is `%ProgramData%\TenonAdmin\workerid` on Windows and `/var/lock/tenonadmin/workerid` on Linux, with a fallback to the temp directory when the preferred location is not writable. Set `TenonAdmin:Id:WorkerIdLockDir` to choose another location. Releasing the lock closes its handle without deleting the file. On k8s, a StatefulSet pod ordinal can still be injected as `WorkerId` so the instance keeps a fixed slot across restarts.

```json
{
  "TenonAdmin": {
    "Id": { "WorkerId": 3 }
  }
}
```

On clock safety: `SnowflakeIdGenerator` takes an injected `TimeProvider` (testable). On detecting a clock rollback, it spin-waits briefly (≤5ms, NTP-adjustment scale) to catch back up; on a large rollback, it throws outright and refuses to issue an ID — it never issues an ID that might be a duplicate.
