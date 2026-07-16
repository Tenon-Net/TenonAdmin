# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TenonAdmin (榫卯) is a **distributable admin-system kernel**, not an application. It ships as NuGet packages so a consumer gets a full enterprise back-office (auth, RBAC, multi-org data permissions, dict/config, logging, uploads) from three lines of `Program.cs`. The overriding design constraint is **replaceability**: every service is interface-backed, `virtual`, and registered via `TryAdd` so a consumer can swap any piece without forking. Runtime deps are **only SqlSugarCore + Microsoft.\*** — no other third-party frameworks in the core packages.

The repo has two independent halves:
- `backend/` — the .NET 10 kernel (the product) + sample host + tests.
- `web/` — a Vue 3 + Naive UI admin template that consumes the kernel's API.

Codebase comments and docs are in Chinese; design doc section refs like `§6` / `T3` point into `docs/rebuild-design.md` and `docs/dev-plan.md`. **Git commit messages, however, are written in English** (conventional-commit format: `type(scope): subject`).

## Commands

Backend (run from repo root; solution is `.slnx`, not `.sln`):
```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx                       # xUnit + WebApplicationFactory, defaults to SQLite
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~DataScopeTests"   # single test/class
dotnet run   --project backend/samples/MinimalHost         # zero-config run on http://localhost:5100
```
Tests against MySQL (matches the CI matrix leg) via env vars:
```bash
TENON_TEST_DBTYPE=MySql TENON_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/TenonAdmin.slnx
```

Frontend (run from `web/`):
```bash
npm run dev          # Vite on :5173, proxies /api and /openapi to backend :5100 (override: TENON_API_TARGET)
npm run build        # vue-tsc --noEmit && vite build
npm run lint         # oxlint (lint:fix to autofix)
npm run typecheck    # vue-tsc --noEmit
npm run gen:api      # regenerate src/api/schema.d.ts from a RUNNING backend's /openapi/v1.json
```

Full local env: `dev.bat` starts backend + frontend in separate windows (installs web deps on first run); `stop.bat` stops them.

Package versions are **centrally managed** — add/bump deps in `backend/Directory.Packages.props` (`<PackageVersion>`), not in individual `.csproj` files. Shared build/NuGet metadata lives in `backend/Directory.Build.props`.

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

## Frontend architecture (`web/`)

Vue 3 `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse. Path alias `@` → `src`.

- **API is contract-generated**: `src/api/schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, backend must be running). `src/api/client.ts` wraps `openapi-fetch` typed against it. Don't hand-edit `schema.d.ts`; regenerate it.
- **Dynamic routing**: `router/routes.ts` holds static routes (login, error, shell); the real menu tree is fetched from the backend after login and injected as dynamic routes (multi-app portal — user picks/switches an app). `useModule().enterInitial()` in the router guard rebuilds them on hard refresh/deep-link, since dynamic routes live only in memory. `v-auth` directive (`directives/auth.ts`) gates buttons by permission.
- **Stores**: `auth` (token/session, routesReady), `user` (profile/login state), `app` (theme/prefs). First visit follows system dark/light (VueUse `usePreferredDark`); after a manual toggle, persistence takes over.
- Login page ships three swappable skins (`views/login/skins/`); theming via `styles/tokens.css` + `theme/`. Design system spec is `web/DESIGN.md`.
- **Shared components live in `web/COMPONENTS.md`** — read it before writing a page (FormContainer, useConfirm, StatusSwitch, dict suite, ProTable, icons); no component-demo menu by design. Update it when adding a shared component.

## CI

`.github/workflows/backend-ci.yml` runs build + test on a `[sqlite, mysql]` matrix (`fail-fast: false`) for pushes/PRs touching `backend/**`. `backend-release.yml` handles NuGet packaging. Keep the SQLite and MySQL test legs green — `TestDb.cs` derives isolated DBs per test from the DB-type env vars.

## Agent skills

### Module scaffolding

Building a new module (entity / backend CRUD / frontend page / service replacement)? Start from `skills/README.md` — `skills/new-module.md` orchestrates the full flow (entity → backend → tests → `gen:api` → frontend → i18n → menu/permission wiring). Also exposed as slash commands (`/new-module`, `/create-entity`, `/create-crud-backend`, `/create-crud-frontend`, `/replace-service`, `/create-page-variant`) via thin wrappers in `.claude/skills/`; the markdown files in `skills/` are the single source of truth.

### Issue tracker

Issues/PRDs live as GitHub issues in `Tenon-Net/TenonAdmin` (via the `gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five canonical roles, label string = role name (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` + `docs/adr/` at repo root, created lazily. See `docs/agents/domain.md`.
