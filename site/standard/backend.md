# Backend Standards (.NET 10 kernel)

Check your work against this list before and after touching backend code — every item is a hard rule already implemented in the kernel. To see why a rule is what it is, follow its link into the corresponding deep-dive; for fuller positive/negative examples, see [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md) in the repo.

::: tip First principle
The kernel ships as NuGet packages, so a consumer can replace any part without touching the source. Any newly added replaceable service is registered with `TryAdd*`, backed by an interface, and split into `virtual` steps — this is a hard constraint, not a suggestion. See the [replaceability model](/backend/replaceability) for the mechanism.
:::

## Where things go across layers

- Dependencies point downward only; skipping layers is forbidden: `Core` (contracts) ← `SqlSugar` (data) ← `Services` (domain + entities) ← `AspNetCore` (host) ← `TenonAdmin` (meta-package). Decide which layer new code belongs to first. See [architecture layering](/backend/architecture) for the full picture.
- Runtime dependencies are **only** SqlSugarCore + Microsoft.\* — core packages pull in no other third-party framework.
- Entities live in the `Services` layer, not the `SqlSugar` layer.
- Each layer's wiring is centralized in one `*Setup.cs` (`SqlSugarSetup` → `ServicesSetup` → `TenonAdminSetup`, the composition root) — no scattered registrations.

## Replaceability (locked by `ReplaceabilityTests` — treat it as a contract)

- All built-in replaceable services use `TryAdd*`; bare `Add*` is **forbidden**. A consumer registering the same interface before `AddTenonAdmin()` wins.
- Long methods are split into small `virtual` steps (e.g. `SessionService.EnforceConcurrencyAsync`) so a consumer overrides one step instead of copying the whole method.
- Every service has an `I*Service` first, with a `virtual` implementation class.
- When touching entity scanning or controller registration, preserve the `options.ApplicationAssemblies` mounting path (business entities' table creation, controllers' `AddApplicationPart`), or consumer modules fail silently. See the [replaceability model](/backend/replaceability) for how.

## Entities

- Defined under `Services/Entities/`; kernel system tables are named `Sys*`.
- Pick a base class: ordinary tables inherit `BaseEntity` (primary key + the four audit fields + soft delete); tables that need org-level isolation inherit `DataEntity` (which also carries the `CreateOrgId` anchor — see "Data access" below for what that means).
- The primary key is always `Id` (snowflake, filled by AOP, never assigned by hand); soft delete is always `IsDelete`, and querying deleted rows needs an explicit `.ClearFilter<ISoftDelete>()`.
- Extra information not reserved in the table schema goes into `ExtJson` — don't add a new column.
- Follow `Entities/SysDictType.cs` for attributes: `[SugarTable]` / unique index `[SugarIndex(IsUnique=true)]` / `[SugarColumn(Length, ColumnDescription, IsNullable)]`.
- Write immutability conventions (e.g. "Code is immutable after creation") into comments and enforce them in the Service's Update (don't touch that field). See [data layer & auditing](/backend/data-layer) for the fields and the audit mechanism.

## Services

- One directory per service: `I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs` (DTOs are `record`s, named `{X}Input`/`{X}PageInput`/`{X}Output`).
- Inject dependencies via the primary constructor; methods are `virtual`; async methods carry the `Async` suffix and take a `CancellationToken` on hot paths.
- Pagination is always `PagedList<T>` + `.ToPagedListAsync(current, size)`.
- Validate with `AdminException.ThrowIf(condition, ErrorCode.X)`, not hand-written if-throw.

## Controllers

- `[RolePermission]` takes no argument: the permission code IS `{METHOD}:/{route template}` (e.g. `GET:/api/v1/sys/dict/type/page`). **Never write magic strings like `"sys:user:add"` in code** — permissions are granted by checking routes in the role-menu UI; super admin (`sadm`) is let through.
- Use `[ActiveSession]` for logged-in-only endpoints that need no specific permission; mark anonymous endpoints with an explicit `[AllowAnonymous]` (the global `RequireAuthorization()` is the fallback, so a forgotten attribute never silently exposes an endpoint).
- Attach `[OperationLog(...)]` to write operations that need auditing; add `[Module("X")]` to make a whole module switchable off — `Api:DisabledModules` strips the controller's routes (404, data untouched). The module *record* itself carries two separate delete guards: one with menus attached is always refused (`ErrorCode.ModuleHasMenus`, applies to self-built modules too), and the built-in `system` module is permanently protected by a fixed Id (`ErrorCode.ModuleProtected`).
- Controllers may `return dto` directly (`ResultEnvelopeFilter` wraps the envelope as a fallback); built-in controllers return `Result<T>.Ok(...)` explicitly for a clear contract. See `Controllers/DictController.cs` for reference and the [request pipeline](/backend/request-pipeline) for the flow.

## Error handling

- Business errors are thrown as `AdminException(ErrorCode)` or returned as an `ErrorCode`, uniformly converted into an envelope by `AdminExceptionFilter`.
- `ErrorCode` is a numeric enum that **never carries localized text** (`Core/ErrorCode.cs`); i18n happens entirely on the frontend, keyed by `msgKey`. Adding an error code means adding both an `[MsgKey("error.<module>.<meaning>")]` attribute (e.g. `error.dict.typeNotFound`) and the matching entry in both frontend language packs — miss it, and it falls back to showing the user the raw `error.code.{number}` string, and `ErrorCodeLocaleConsistencyTests` turns a backend test red.

## Data access

- Inject `IRepository<T>`; go through `.AsQueryable()` for complex queries, and drop to `.Db` for escape hatches (`Db.Deleteable<>()` / `Db.Ado.UseTranAsync`).
- Soft delete and data scope are **global filters** — business code doesn't repeat the filter conditions.
- Uniqueness checks must include soft-deleted rows: `.ClearFilter<ISoftDelete>().AnyAsync(...)`, or you'll collide with the DB's unique index and throw a raw 500.
- Wrap multi-write operations in a `Db.Ado.UseTranAsync` transaction, rolling everything back on failure; **cache invalidation goes after the transaction commits**.
- Audit fields (`Id` snowflake, `CreateTime`/`User`/`Org`, `UpdateTime`/`User`) are filled by AOP; set only business fields.

::: danger CreateOrgId is the data-scope anchor
If a `DataEntity` row's `CreateOrgId` isn't set, org-scoped queries always return 0 rows for it — AOP fills it automatically; never bypass that to assign it by hand. See [multi-org data permissions](/backend/data-scope) for how it works.
:::

## Caching

- The model is cache-aside (read-through) + explicit invalidation, not query-every-time; after a create/update/delete, both `RemoveAsync` the cache and broadcast an event (e.g. `DictService.InvalidateAsync` → `DictChangedEvent`) for audit / push subscribers. The default `ChannelEventBus` is in-process, though — events don't cross replicas. Cross-node invalidation needs either a shared cache or your own `IEventBus` wired to an MQ.
- Logical keys are centralized in `Core/CacheKeys.cs` — no scattered magic strings; the prefix `Cache:KeyPrefix` (default `tenon:`) is appended uniformly by the provider.
- Default is the in-process `MemoryCacheProvider`; for multi-instance sharing install the optional `TenonAdmin.Caching.Redis` package, and `AddTenonAdminRedisCache` must be registered **before** `AddTenonAdmin` to win over `TryAdd` (zero business-code changes). Order isn't the only gate: the overload taking `IConfiguration` only actually takes over when `Cache:Provider=Redis`, otherwise it silently falls back to the in-process cache — the `AddTenonAdminRedisCache(connectionString)` overload, by contrast, enables unconditionally the moment it's called. See [data layer & auditing](/backend/data-layer) for the template.

## DI wiring

- Wiring goes in `*Setup.cs`; built-in services use explicit `TryAdd` (not scanning — predictable and replaceable), and seeds use `TryAddEnumerable`, deduplicated by implementation type.
- Stateless services are `Singleton` (hashing, captcha generator, file storage, cache provider, event bus); per-request services are `Scoped` (most business services, matching the repository).

## Seed data

- Implement `ISeedData<TEntity>`; `HasData()` returns the default rows (an empty collection is valid = "don't seed if the DB already has data").
- **Fixed IDs keep it idempotent**: fill in only what's missing, never overwrite existing rows — UI changes aren't clobbered by a restart. The one exception is `SyncOnUpgrade` (default `false`): the kernel's own `DefaultMenuSeed` and `DefaultModuleSeed` turn it on, so a seed-version bump overwrites same-Id rows, meaning UI edits to **built-in** menus and modules get lost on a kernel upgrade (self-built seeds are unaffected). Never turn this on for a seed whose rows users edit through the UI.
- **IDs must fall within a reserved range** (`Core/TenonSeedIds.cs`): kernel `[1, 999]`, consumers `[1000, 4095]`, `4096+` belongs to the snowflake runtime; out-of-range, `Id=0`, or a duplicate of an existing seed ID is rejected at startup.

## Naming / organization

- Namespaces follow directories; one type per file; suffixes `Sys*` for entities, `I*` for interfaces, `*Service`/`*Provider`/`*Filter`/`*Attribute`.
- Nullable reference types enabled; new code's time access goes through the injected `TimeProvider` (testable), never a bare `DateTime.Now`. The password-expiry path (`AuthService`/`UserService`/`PersonalService`), the SMS daily-cap day bucket, and the schema-version stamp all route through it now too — `SessionService` was always the pattern to follow, the rest have caught up. One bare call is left on purpose: `SchemaVersionSeed`'s first-install timestamp is a static seed row with no DI clock to inject.

## Package management

- Add or bump dependencies in `backend/Directory.Packages.props`'s `<PackageVersion>`, **not** version numbers in individual `.csproj` files; shared build/NuGet metadata lives in `backend/Directory.Build.props`.

## Comments

- Public types/members use `/// <summary>` to state responsibility and boundaries; reference design-doc section numbers (`§N`/`TN`) for key trade-offs.
- Inline comments explain only WHY (concurrency, transaction ordering, edge cases, cross-dialect pitfalls), not WHAT; in Chinese, matching the existing code.
- Deliberately simplified or capped implementations are flagged with `// ponytail:` noting the limit and the upgrade path.
