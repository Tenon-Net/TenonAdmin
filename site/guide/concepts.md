# Core Concepts

Three lines of `Program.cs` buy a complete enterprise back office: auth, RBAC, multi-org data permissions, dict and config, logging, uploads, notices and announcements, a read-only demo mode, a password-expiry policy. It arrives as NuGet packages — a **kernel**, not an application, with your business code staying in your own repository. That shape forces one constraint on everything inside it: every piece has to be replaceable.

## Why not just another admin template

Copying an admin template gets you started fast, but as business code grows the project ends up deeply coupled to the template — and after that, upgrading base capabilities, pulling in upstream changes, or swapping out just one piece all become painful.

TenonAdmin factors these common capabilities out of business code: you can use the default implementations as-is, integrate it fairly naturally into an existing project, or replace any single piece without forking.

## The replaceability model

It comes down to three constraints, locked in by the "six-piece" `ReplaceabilityTests`:

1. **Interface registration + `TryAdd`** — built-in services are all registered with `TryAdd*`, so a consumer registering the same interface before `AddTenonAdmin()` wins and overrides the default implementation.
2. **Template-method decomposition** — long service methods are split into small `virtual` steps, so a consumer overrides **one step** via subclassing instead of copying the whole method.
3. **Business assembly mounting** — a consumer's entities join CodeFirst table creation via `options.ApplicationAssemblies`, and their controllers get `AddApplicationPart`-ed automatically, extending the system without touching the kernel.

## Package layering

Dependencies point downward only — this ordering is itself a load-bearing constraint:

```text
TenonAdmin.Core        Pure contracts: interfaces, Options, Result<T>, ErrorCode. No SqlSugar, no ASP.NET.
   ↑
TenonAdmin.SqlSugar    Data layer: ISqlSugarClient singleton, IRepository<>, entity base classes, CodeFirst, seeding.
   ↑
TenonAdmin.Services    Domain layer: entities (Sys*), service implementations, RBAC / data scope, event bus.
   ↑
TenonAdmin.AspNetCore  Host integration: AddTenonAdmin / MapTenonAdmin, JWT, permission/session filters, built-in controllers.

TenonAdmin             Meta-package: references AspNetCore only; a consumer installs this one to pull in the whole stack.
```

## Request pipeline

An authenticated request flows through, in order:

1. **Authentication** — Microsoft JWT Bearer; the framework's 401 is reshaped into the standard envelope (code 40006).
2. **`[RolePermission]`** — the permission code IS the normalized route (`{METHOD}:/{route}`); **there are no permission strings in code** — authorization is granted by checking routes in the role-menu UI. Super admin (`sadm`) bypasses directly, while session validity is also checked (so a forced logout takes effect immediately).
3. **Data scope** — during authorization, the current user's effective org data scope is resolved and injected into `IDataScopeContext`.
4. **Result envelope** — controllers can `return dto` directly, and a filter wraps it into `Result<T>`; business errors are thrown as `AdminException` / returned as `ErrorCode` and turned into an envelope. **Errors are numeric `ErrorCode`s, never localized text** — i18n is handled on the frontend by translating the code.

## Data layer conventions

- A single `SqlSugarScope` singleton; global query filters automatically apply **soft delete** (`ISoftDelete`) and **data scope** (`IOrgScoped` / `DataEntity` filtered by the org set resolved for the current request).
- AOP auto-fills audit fields on insert/update: snowflake `Id`, `CreateTime`, `CreateUserId`, `CreateOrgId` (the data-scope anchor), `UpdateTime`, `UpdateUserId`. Business code only needs to set business fields.
- The snowflake `WorkerId` comes from `TenonAdmin:Id:WorkerId` (default 0); **it must differ per instance when scaling horizontally**, or IDs generated in the same millisecond will collide.

---

> For a more complete picture of the architecture and design rationale, see the repo's [Architecture & Design Document](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/rebuild-design.md).
