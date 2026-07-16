# Backend Standards (.NET 10 kernel)

> This page is an actionable checklist distilled from the existing code. For the full version (with positive/negative examples for every rule), see [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md) in the repo.

::: tip First principle
The kernel ships as NuGet packages so a consumer can replace any part without touching the source. Any newly added replaceable service must be registered with `TryAdd*`, backed by an interface, and split into `virtual` steps — this is a hard constraint, not a suggestion.
:::

## Layering and dependency direction

Dependencies point downward only; skipping layers is forbidden:

```
Core        Contracts only: interfaces (I*Provider/I*Service), Options, Result<T>, ErrorCode, AdminException. No SqlSugar, no ASP.NET.
  ↑
SqlSugar    Data layer: ISqlSugarClient singleton, IRepository<>, entity base classes, CodeFirst init, seed runner.
  ↑
Services    Domain layer: entities (Sys*), *Service implementations, RBAC/data-scope providers, event bus. ★ Entities live here, not in the SqlSugar layer.
  ↑
AspNetCore  Host integration: AddTenonAdmin/MapTenonAdmin, JWT, filters, built-in controllers.
  ↑
TenonAdmin  Meta-package: references AspNetCore only; consumers install this one to pull the whole stack.
```

Think through which layer new code belongs to before writing it. Runtime dependencies are **only** SqlSugarCore + Microsoft.\* — core packages must not pull in any other third-party framework. Each layer's wiring is a `*Setup.cs` extension method: `SqlSugarSetup` → `ServicesSetup` → `TenonAdminSetup` (the composition root).

## The replaceability three-piece

The contract locked in by `ReplaceabilityTests` (the "six-piece set") is a hard constraint, not an ordinary test:

| Mechanism | Practice | Reference |
|---|---|---|
| `TryAdd` registration | All built-in services use `TryAdd*`; a consumer registering the same interface before `AddTenonAdmin()` wins. **Never** use bare `Add*` for a replaceable service. | `ServicesSetup.cs`, `SqlSugarSetup.cs` |
| `virtual` template methods | Long methods are split into small `virtual` steps so a consumer overrides one step instead of copying the whole method. | `SessionService.EnforceConcurrencyAsync` |
| Interface backing | Every service has an `I*Service` first; the implementation class is `virtual`. | `Services/*/I*.cs` |
| Consumer wiring | Business assemblies are merged in via `options.ApplicationAssemblies`: their entities join table creation, their controllers get `AddApplicationPart`-ed. When touching entity scanning or controller registration, **this path must be preserved**. | `TenonAdminSetup.cs` |

## Entity conventions

- **Location**: entities are defined under `Services/Entities/`, not in the SqlSugar layer. Kernel system tables are named `Sys*`.
- **Base classes**:
  - `BaseEntity` (`SqlSugar/Entities/BaseEntity.cs`): primary key + the four audit fields (`CreateTime`/`CreateUserId`/`UpdateTime`/`UpdateUserId`) + soft-delete `IsDelete`. These fields are auto-filled by AOP; business code has zero awareness of them.
  - `DataEntity`: business tables that need org-level data isolation inherit from this, carrying the `CreateOrgId` anchor (the basis for data-scope filtering).
- **Primary key**: always `Id` (snowflake ID, auto-filled by AOP; business code never assigns it manually).
- **Soft delete**: always the `IsDelete` field. The global query filter automatically adds `IsDelete == false`; querying deleted rows requires an explicit `.ClearFilter<ISoftDelete>()`.
- **Extension fields**: extra information not reserved in the table schema goes into `ExtJson` — don't add a new column for it.
- **SqlSugar attributes**: `[SugarTable("table name", TableDescription=…)]`, unique index `[SugarIndex(..., IsUnique=true)]`, column `[SugarColumn(Length=…, ColumnDescription=…, IsNullable=…)]` — see `Entities/SysDictType.cs` for reference.
- **Document immutability conventions in comments**: e.g. "Code is immutable after creation," and enforce it in the Service's Update method (don't modify that field there).

## Service conventions

- One directory per service: `I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs` (DTOs: `{X}Input`/`{X}PageInput`/`{X}Output`, as `record`s).
- Implementation classes inject dependencies via constructor (primary constructor syntax), methods are `virtual`, async methods carry the `Async` suffix.
- Pagination is always `PagedList<T>` + `.ToPagedListAsync(current, size)` (`SqlSugar/Paging/`).
- Validation uses `AdminException.ThrowIf(condition, ErrorCode.X)`.

## Error handling

::: warning Errors are numeric codes
`ErrorCode` is a numeric enum and **never carries localized text** (`Core/ErrorCode.cs`). i18n happens entirely on the frontend by code — the backend never sends down any message text. Adding a new error code is just adding an entry to the enum.
:::

- Business errors are thrown as `AdminException(ErrorCode)` or returned as `ErrorCode`, uniformly converted into an envelope by `AdminExceptionFilter`.
- Controllers can `return dto` directly and `ResultEnvelopeFilter` wraps it into `Result<T>` as a fallback; built-in controllers explicitly return `Result<T>.Ok(...)` to keep the OpenAPI contract clear.

## Controller conventions

See `Controllers/DictController.cs` for reference:

```csharp
[ApiController]
[Route("api/v1/sys/dict")]
[Module("Dict")]                       // The whole module can be disabled via Api:DisabledModules
public class DictController(IDictService svc) : ControllerBase
{
    [HttpGet("type/page")]
    [RolePermission]                   // Permission code = normalized route, no strings
    public async Task<Result<PagedList<SysDictType>>> PageTypes([FromQuery] DictTypePageInput input) =>
        Result<PagedList<SysDictType>>.Ok(await svc.PageTypesAsync(input));
}
```

- **`[RolePermission]` takes no argument**: the permission code IS `{METHOD}:/{route template}` (e.g. `GET:/api/v1/sys/dict/type/page`) — permissions are granted by checking routes in the role-menu UI. **Never write magic strings like `"sys:user:add"` in code.** Super admin (`sadm` claim) bypasses directly.
- **`[ActiveSession]`**: use it for endpoints reachable by any logged-in user that don't need a specific permission.
- **`[OperationLog(...)]`**: attach it to write operations that need auditing; recorded by `OperationLogFilter`.
- **`[Module("X")]`**: a modular on/off switch that can be removed via configuration.
- Anonymous endpoints must explicitly add `[AllowAnonymous]` (login/refresh/captcha). Default deny: `MapControllers().RequireAuthorization()` is the global fallback, so forgetting to attach `[RolePermission]` never silently exposes an endpoint.

## Data access

- Inject `IRepository<T>`; use `.AsQueryable()` for complex queries, and drop to `.Db` for escape hatches (e.g. `Db.Deleteable<>()`, `Db.Ado.UseTranAsync`).
- **Global filters** (business code doesn't need to repeat these): soft delete (`ISoftDelete` entities are auto-filtered), data scope (`IOrgScoped`/`DataEntity` filtered by the current request's effective org set — the signature feature).
- **Uniqueness checks must include soft-deleted rows**: `.ClearFilter<ISoftDelete>().AnyAsync(...)`, otherwise you'll collide with the DB's unique index and get a raw 500.
- **Wrap multi-write operations in a transaction**: `Db.Ado.UseTranAsync`, rolling back everything on failure; **cache invalidation happens after the transaction commits**.
- Audit fields (`Id` snowflake, `CreateTime`/`User`/`Org`, `UpdateTime`/`User`) are auto-filled by AOP; business code only sets business fields.

::: danger CreateOrgId must not be bypassed manually
`CreateOrgId` is the anchor for org-dimension data scope; if it's not set, that row will always return 0 rows in org-scoped queries — don't manually bypass AOP and set it yourself.
:::

- The snowflake `WorkerId` comes from `TenonAdmin:Id:WorkerId`; **each instance in a horizontally scaled deployment must use a different value** — see the [FAQ](/faq) for details.

## Caching conventions (performance core)

The system uses a **read-through / cache-aside + explicit invalidation** model, not a query-every-time approach. New hot-read paths should follow this template:

```csharp
public virtual async Task<T> GetHotAsync(string k)
{
    var key = CacheKeys.Xxx(k);               // ① logical key centrally defined
    var cached = await cache.GetAsync<T>(key); // ② return on hit
    if (cached is not null) return cached;
    var v = await LoadFromDb(k);               // ③ query DB on miss
    await cache.SetAsync(key, v, ttl);         // ④ backfill (TTL is only a fallback; explicit invalidation is primary)
    return v;
}
// After any create/update/delete: await cache.RemoveAsync(CacheKeys.Xxx(k));  ⑤ explicit invalidation
```

- **Keys are centralized in `Core/CacheKeys.cs` — no scattered magic strings.** The prefix `Cache:KeyPrefix` (default `tenon:`) is appended uniformly by the provider.
- On change, **both invalidate the cache and broadcast an event** (e.g. `DictService.InvalidateAsync` → `DictChangedEvent`), for cross-node invalidation/audit/push subscribers.
- Default is `MemoryCacheProvider` (in-process); for multi-instance sharing, install the optional `TenonAdmin.Caching.Redis` package — zero business-code changes required, just register it **before** `AddTenonAdmin` so it wins over `TryAdd`:

```csharp
builder.Services.AddTenonAdminRedisCache(builder.Configuration); // Must come before AddTenonAdmin
builder.Services.AddTenonAdmin(builder.Configuration);
```

## DI wiring

- Wiring is written in `*Setup.cs` extension methods. Built-in services use **explicit** `TryAdd` (not scanning, so it's predictable and replaceable); seeds use `TryAddEnumerable` (deduplicated by implementation type).
- Stateless services are `Singleton` (hashing, captcha generator, file storage, cache provider, event bus); per-request services are `Scoped` (most business services, matching the repository).

## Seed data

- Implement `ISeedData<TEntity>` (generic version); `HasData()` returns the default rows. Returning an empty collection is valid ("don't seed if the DB already has data").
- **Fixed IDs keep seeding idempotent**: seeds only fill in missing rows and never overwrite existing ones — changes made through the UI survive a restart.
- **IDs must fall within a reserved range** (`Core/TenonSeedIds.cs`): kernel `[1, 999]`, consumers `[1000, 4095]`, `4096+` belongs to the snowflake runtime. Out-of-range or `Id=0` is rejected at startup.

## Naming / organization

- Namespaces follow directory structure; one type per file; suffixes `Sys*` for entities, `I*` for interfaces, `*Service`/`*Provider`/`*Filter`/`*Attribute`.
- Nullable reference types enabled; `async` methods carry the `Async` suffix and accept a `CancellationToken` (on hot paths).
- All time access goes through the injected `TimeProvider` (testable), never a bare `DateTime.Now` call.

## Package management

Versions are **centrally managed**: add or bump dependencies in `backend/Directory.Packages.props`'s `<PackageVersion>` — **not** in individual `.csproj` files. Shared build/NuGet metadata lives in `backend/Directory.Build.props`.

## Comment conventions

- Public types/members use `/// <summary>` to state their responsibility and boundaries clearly; key trade-offs reference design-doc section numbers (`§N`/`TN`).
- Inline comments explain WHY (concurrency, transaction ordering, edge cases, cross-dialect pitfalls), not WHAT.
- Comments are written in Chinese, matching the existing codebase.
- Deliberately simplified implementations, or ones with known limits, are flagged with a `// ponytail:` comment noting the limit and the upgrade path.

---

> See [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md) for the fuller write-up with positive/negative examples.
