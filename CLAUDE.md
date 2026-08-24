# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TenonAdmin (榫卯) is a **distributable admin-system kernel**, not an application. It ships as NuGet packages so a consumer gets a full enterprise back-office (auth, RBAC, multi-org data permissions, dict/config, logging, uploads) from three lines of `Program.cs`. The overriding design constraint is **replaceability**: every service is interface-backed, `virtual`, and registered via `TryAdd` so a consumer can swap any piece without forking. Runtime deps are **only SqlSugarCore + Microsoft.\*** — no other third-party frameworks in the core packages.

The repo is a backend kernel plus **two independent, self-contained frontend templates** that never share code — a consumer picks whichever stack they want and owns it:
- `backend/` — the .NET 10 kernel (the product) + sample host + tests.
- `web/` — a Vue 3 + Naive UI admin template that consumes the kernel's API.
- `web-react/` — a React 19 + Ant Design admin template consuming the **same** API. **Zero-shared with `web/` by design** (each is `degit`-able on its own); the duplication is deliberate — a shared layer was tried and overturned (`docs/react-template-ledger.md`). **Never factor a shared layer out of the two, and never write any "must bundle both" coupling.**

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
npm run gen:api      # regenerate src/api/schema.d.ts; override the backend with TENON_API_TARGET
```

Contract drift check (run once per clone to activate the tracked pre-push hook):
```bash
git config core.hooksPath .githooks
node scripts/check-contract-drift.mjs
```
The check starts its own Development MinimalHost, regenerates both frontend schemas, and compares them with `HEAD`.

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

**Zero-config bootstrap**: default SQLite (relative paths resolved against ContentRoot), CodeFirst auto-DDL via `DatabaseInitializer` (a hosted service), seed data (`ISeedData` implementations run once, idempotently), and a **random super-admin password printed to the console on first startup**. Switch dialect by changing `TenonAdmin:Database` (DbType + connection string); SQLite/MySQL/SqlServer/PostgreSQL supported. Same-process **extra connections** (multi ConfigId): `TenonAdmin:AdditionalDatabases` — see site guide `site/zh/guide/multi-database.md` (en: `site/guide/multi-database.md`); access via `db.AsTenant().GetConnection(configId)`; `IRepository<>` always hits main.

Config lives under the `TenonAdmin` section of `appsettings.json`, bound to `TenonAdminOptions` (see `Core/Options/*`). `appsettings.Development.json` is gitignored (holds credentials) — copy from the `.example`.

Health/OpenAPI: `/health` (liveness), `/health/ready` (DB+cache), and `/openapi/v1.json` (dev-only, the frontend's contract source).

## Frontend architecture (`web/`)

Vue 3 `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse. Path alias `@` → `src`.

- **API is contract-generated**: `src/api/schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, backend must be running). `src/api/client.ts` wraps `openapi-fetch` typed against it. Don't hand-edit `schema.d.ts`; regenerate it.
- **Dynamic routing**: `router/routes.ts` holds static routes (login, error, shell); the real menu tree is fetched from the backend after login and injected as dynamic routes (multi-app portal — user picks/switches an app). `useModule().enterInitial()` in the router guard rebuilds them on hard refresh/deep-link, since dynamic routes live only in memory. `v-auth` directive (`directives/auth.ts`) gates buttons by permission.
- **Stores**: `auth` (token/session, routesReady), `user` (profile/login state), `app` (theme/prefs). First visit follows system dark/light (VueUse `usePreferredDark`); after a manual toggle, persistence takes over.
- Login page ships three swappable skins (`views/login/skins/`); theming via `styles/tokens.css` + `theme/`. Design system spec is `web/DESIGN.md`.
- **Shared components live in `web/COMPONENTS.md`** — read it before writing a page (FormContainer, useConfirm, StatusSwitch, dict suite, ProTable, icons); no component-demo menu by design. Update it when adding a shared component.

## Frontend architecture (`web-react/`)

React 19 + Ant Design (antd 6) + `@ant-design/pro-components` + zustand (persisted) + react-router-dom 7 + react-i18next. Path alias `@` → `src`. A second official template that ports `web/` feature-for-feature against the same backend contract — **a parallel template, not a shared library**. Commands run from `web-react/` (`npm run dev` on **5174**, plus `build`/`lint`/`typecheck`/`gen:api`, each its own script); `dev.bat`/`dev.sh` start it next to `web`.

- **Self-contained, zero-shared — the load-bearing constraint**: `web-react/` never imports from `web/`, `web-shared`, or `@shared`. This is a deliberate product decision (fork-and-own: a consumer degits one template), documented with its rationale in `docs/react-template-ledger.md`. **Don't factor common code out of the two templates, don't add a "must bundle both" note anywhere, and expect text/design-tokens to be maintained twice on purpose.**
- **Contract-generated API** (same shape as `web/`): `src/api/schema.d.ts` from the backend OpenAPI (`npm run gen:api`, its own script); `src/api/client.ts` wraps `openapi-fetch`. Don't hand-edit `schema.d.ts`.
- **Dynamic routing**: `useRoutes` over routes derived from the backend menu tree; `<RequireAuth>` guards (login / must-change-password / routes-ready); `<Can code="VERB:/path">` gates buttons by permission (antd's answer to `v-auth`).
- **Stores** (zustand): `user`/`auth`/`app`/`dict`/`tabs`. Selectors must return **primitives or stable references** — a selector returning a new object/closure re-runs every render and loops forever.
- **antd v6, not v5**: renamed props (`variant` not `bordered`, `styles.body` not `bodyStyle`, `styles.container` for Modal padding, …) are silent under `tsc`. Query the offline CLI before writing a component — `antd info/demo/semantic <C> --version 6.x` — and `antd lint <file>` after.
- Login ships the same three swappable skins (`views/login/skins/`); the `<DataTable>` wrapper isolates `pro-components` so CRUD pages depend only on it. The driving log for the whole port is `docs/react-template-ledger.md`.
- **Shared components live in `web-react/COMPONENTS.md`** (self-contained, contracts inline — no per-component README tree) — read it before writing a page; update it when adding a shared component.

## CI

`.github/workflows/backend-ci.yml` runs on pushes/PRs touching `backend/**`, `templates/**`, or the workflow itself, with two jobs:
- **`build-test`** — build + test on a `[sqlite, mysql, sqlserver, postgres]` matrix (`fail-fast: false`, so one red leg doesn't mask the others). MySQL/SqlServer/PostgreSQL/Redis service containers start on *every* leg; `TestDb.cs` derives an isolated DB per test from the DB-type env vars. `TENON_TEST_REDIS` is set on all legs, so the Redis contract tests actually run instead of silently skipping. **On push/PR the SqlServer leg runs only a dialect-sensitive subset** (`TEST_FILTER` in the Test step) — the other three legs run the full suite; see the SqlServer subsection below.
- **`template-smoke`** — `dotnet new tenon-app` → restore → build, via `templates/smoke-test.ps1` (the same script used for local manual runs). Catches the consumer's very first command breaking while kernel tests are all green.

`docker-smoke.yml` also fires on `backend/**` (and `web/**`), so a backend PR shows two more checks: **`single`** (image comes up, creates tables, seeds, issues a token) and **`multi`** (two replicas behind Caddy — cross-replica force-logout, lockout threshold, cluster-wide rate limit, distinct `WorkerId`, real client IP). Those guarantees only surface with two replicas online, so don't collapse `multi` into `single`.

`backend-release.yml` handles NuGet packaging. Expect **7 checks** on a backend-only PR; keep all of them green.

### The sqlserver leg: dialect-sensitive subset on push/PR, full nightly

The full SqlServer suite ran **40–60 min** (measured 2026-07-20: 2302 / 2466 / 2748 / 3000 / 3124 / 3277 / 3335 / 3514s, all green) vs 3–5 min for the other three legs. It was never hung — just slow — so don't add a short `timeout-minutes` (under ~90 would turn a green leg red), and don't cancel a nightly SqlServer run for "looking stuck".

**Why it's slow (diagnosed, not guessed).** `TestDb` gives every test its own database — near-free on SQLite (a file), MySQL (a directory), PostgreSQL (a `template1` copy), brutal on SQL Server. A micro-benchmark against a real SqlServer (network latency 1.5 ms, so not a network artifact) put **85% of the per-database cost in `CodeFirst.InitTables`**: ~20 s/db, of which ~17.6 s is the 23 `CREATE TABLE`/`CREATE INDEX` statements themselves (each auto-committed → its own transaction-log flush), only ~2.4 s is existence-check round-trips, and `CREATE DATABASE`/`DROP` are rounding error. ×~200 databases ≈ the whole leg.

**What was tried and rejected** (don't re-attempt without new evidence): tmpfs on `/var/opt/mssql` and a targeted `ClearPool` — both *measured as no-ops*. Building the schema once and cloning per test — `DBCC CLONEDATABASE` is fragile under repeated cloning of one source (transient "database may be offline"), and `BACKUP`/`RESTORE` per test was ruinously slow on the test instrument. The schema DDL is disk-bound and there's no cheap per-database shortcut.

**The resolution** is to stop rebuilding ~200 SqlServer schemas on every PR. On push/PR the SqlServer leg runs only the SqlServer-*specific* surface (nvarchar Chinese, boolean-predicate global filters, T-SQL DDL/bootstrap, Storageable seed SQL) via `TEST_FILTER` — DB-agnostic business logic is already exercised by the full sqlite/mysql/postgres legs on the same PR. The **full** SqlServer suite runs nightly (the `schedule` trigger), so nothing is permanently uncovered — a test outside the subset regresses into the nightly run, not into a blind spot. Measurement discipline that cost real time to learn: the baseline's own spread (2302–3514s, 1.5×) means **a single CI run cannot resolve anything smaller than a ~50% change** — repeat runs or measure locally.

## Agent skills

### Module scaffolding

Building a new module (entity / backend CRUD / frontend page / service replacement)? Start from `skills/README.md` — `skills/new-module.md` orchestrates the full flow (entity → backend → tests → `gen:api` → frontend → i18n → menu/permission wiring). Also exposed as slash commands (`/new-module`, `/create-entity`, `/create-crud-backend`, `/create-crud-frontend`, `/create-crud-frontend-react`, `/replace-service`, `/create-job`, `/create-page-variant`, `/tenon-release`) via thin wrappers in `.claude/skills/`; the markdown files in `skills/` are the single source of truth. The frontend CRUD skill is per-template: `create-crud-frontend.md` for `web/` (Vue), `create-crud-frontend-react.md` for `web-react/`.

### Writing docs

Writing or editing any page under `site/`? Read `skills/write-docs.md` first (`/write-docs`) — voice, punctuation, openings, em-dash budget, and the zh-is-source/en-is-translation contract. Its machine-checkable half is enforced by `site/scripts/lint-prose.mjs` (`cd site && npm run lint:prose -- <page>`).

### Releasing

Shipping a TenonAdmin version (changelog, dual frontend + site badge bumps, merge to `main`, `v*` tag, NuGet via `backend-release`)? Use `skills/tenon-release.md` (`/tenon-release`). Human runbook and cadence notes stay in `docs/releasing.md`.

### Issue tracker

Issues/PRDs live as GitHub issues in `Tenon-Net/TenonAdmin` (via the `gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five canonical roles, label string = role name (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` + `docs/adr/` at repo root, created lazily. See `docs/agents/domain.md`.
