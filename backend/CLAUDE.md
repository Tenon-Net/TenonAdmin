## Backend architecture

**Package layering** (dependencies point downward only; this ordering is the load-bearing constraint):

```
TenonAdmin.Core        contracts only: interfaces (I*Provider, I*Service surface), Options, Result<T>, ErrorCode, AdminException. No SqlSugar, no ASP.NET.
   ↑
TenonAdmin.SqlSugar    data layer: ISqlSugarClient singleton (SqlSugarScope), IRepository<>, entity base classes, CodeFirst DatabaseInitializer, seed runner.
   ↑
TenonAdmin.Services    domain: entities (Sys*), *Service implementations, RBAC/data-scope providers, event bus. Entities live HERE, not in SqlSugar.
   ↑
TenonAdmin.AspNetCore  host integration: AddTenonAdmin/MapTenonAdmin, JWT, [RolePermission]/[ActiveSession] filters, built-in Controllers, envelope/exception/oplog filters.

TenonAdmin             meta-package: references AspNetCore only; consumers install this one to pull the whole stack.
```

Each layer's DI wiring is a `*Setup.cs` extension (`SqlSugarSetup`, `ServicesSetup`, `TenonAdminSetup`). `AddTenonAdmin` (in `TenonAdminSetup.cs`, the composition root) binds config, then calls down the chain.

**Replaceability model** (the whole point — respect it when adding features):
- Built-in services are registered with **`TryAdd*`** so a consumer registering the same interface *before* `AddTenonAdmin()` wins. Never use plain `Add*` for a replaceable service.
- Long service methods are split into small `virtual` steps (template-method) so consumers override one step by subclassing, not by copying the method.
- Consumer business assemblies are wired in via `options.ApplicationAssemblies`: their entities join CodeFirst table creation and their controllers get `AddApplicationPart`-ed. When touching entity scanning or controller registration in `TenonAdminSetup`, keep this path intact — dropping it silently breaks consumer modules (their tables aren't created, their controllers 404).
- The replaceability guarantees are locked by the "六件套" tests (`ReplaceabilityTests`) — treat those as a contract, not ordinary tests.

**Request pipeline** (an authenticated call flows through these, in order):
1. **Auth** — Microsoft JWT Bearer. Claims are unmapped (`sub`, `sid`, `sadm`, `unique_name`). Framework 401 challenges are reshaped into the standard envelope (code 40006).
2. **`[RolePermission]`** (`RolePermissionAttribute`) — permission code IS the normalized route: `{METHOD}:/{route template}` (e.g. `GET:/api/v1/ping`). There are **no permission strings in code** — authorization is granted by checking routes in the role-menu UI. Super admin (`sadm` claim) bypasses. Also validates the session (`sid`) is still active, so force-logout takes effect immediately. Use `[ActiveSession]` for any-logged-in-user endpoints that need no specific permission.
3. **Data scope** — during authorization, the user's effective org data-scope is resolved (cached) into `IDataScopeContext` (an `HttpContext.Items` carrier on the HTTP path — deliberately *not* `AsyncLocal`, which doesn't flow back through auth filters).
4. **Result envelope** — controllers may `return dto` directly; `ResultEnvelopeFilter` wraps bare returns into `Result<T>`. Business errors are thrown as `AdminException` / returned as `ErrorCode` and turned into envelopes by `AdminExceptionFilter`. **Errors are numeric `ErrorCode`s, never localized text** — i18n happens on the frontend by code (`§13.2`).

**Data layer conventions** (enforced globally in `SqlSugarSetup`, so business code stays clean):
- One `SqlSugarScope` singleton (thread-safe). Global query filters: soft-delete (`ISoftDelete` → `IsDelete == false`) and **data scope** (`IOrgScoped`/`DataEntity` filtered by the current request's resolved org set). The data-scope filter is the signature feature (`§6`).
- AOP auto-fills audit fields on insert/update: snowflake `Id` (when 0), `CreateTime`, `CreateUserId`, `CreateOrgId` (the data-scope anchor — if this isn't filled, org-scoped queries return 0 rows), `UpdateTime`, `UpdateUserId`. Business code sets business fields only.
- Snowflake `WorkerId` comes from `TenonAdmin:Id:WorkerId` (default 0) — **must differ per instance** when horizontally scaled or same-millisecond IDs collide.

**Zero-config bootstrap**: default SQLite (relative paths resolved against ContentRoot), CodeFirst auto-DDL via `DatabaseInitializer` (a hosted service), seed data (`ISeedData` implementations run once, idempotently), and a **random super-admin password printed to the console on first startup**. Switch DB by changing `TenonAdmin:Database` (DbType + connection string); SQLite/MySQL/SqlServer/PostgreSQL supported.

Config lives under the `TenonAdmin` section of `appsettings.json`, bound to `TenonAdminOptions` (see `Core/Options/*`). `appsettings.Development.json` is gitignored (holds credentials) — copy from the `.example`.

Health/OpenAPI: `/health` (liveness), `/health/ready` (DB+cache), and `/openapi/v1.json` (dev-only, the frontend's contract source).
