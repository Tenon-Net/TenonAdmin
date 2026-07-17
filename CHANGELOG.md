# Changelog

This project follows [Semantic Versioning](https://semver.org/).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Release cadence: **development happens on `dev`, releases happen on `main`** — merge `dev` into `main` first, then tag `v*` **on `main`**. Tags are branch-independent, so tagging elsewhere would trigger the release workflow just the same; there's a gate for that: if the tagged commit isn't on `main`, the release is rejected.

Once a tag triggers `backend-release`, it first runs build + tests + template smoke test (`dotnet new tenon-app` must restore and compile successfully), and only pushes to nuget.org if everything is green.

The step-by-step release runbook (version bump, verify, merge to `main`, tag) lives in [`docs/releasing.md`](docs/releasing.md).

> When releasing, **both halves' version numbers must be updated together**: the backend version is injected from the tag via `-p:Version`, while `web/package.json`'s `version` is a build-time constant **shown in the login page footer**. Forget to update it, and the version the user sees in the UI won't match the package they installed.

## 0.1.2 - 2026-07-16

Consumer-seam release: a business module now lives entirely in new files — zero edits to the upstream-churning shared files, so `git merge upstream` stays conflict-free — plus a password-expiry policy, a fuller personal center, a real-data workbench, and a bilingual documentation site.

### Added

- **Consumers can now add a whole business module without editing any shared frontend file** — the mechanism that keeps `git merge upstream` conflict-free. i18n text goes in a new `locales/ext/<locale>/<module>.ts` (globbed in and **deep-merged**, so adding a key can't clobber a sibling in the same namespace); API modules go in a new `api/<domain>.ts` importing the now-exported `unwrap` / `pageParams` / `toPage`; types and pages get their own files too. The frontend guide, the module skills, and the fork-sync doc were rewritten to teach this new-file placement — they previously told consumers to append to `api/index.ts`, `types/api.ts`, and `locales/zh-CN.ts`, the four highest-churn files, which guaranteed a conflict on every upstream merge. An `npx degit Tenon-Net/TenonAdmin/web` snapshot on-ramp is also documented as a third consumption model, for consumers who don't want to track upstream at all.
- **Password expiry policy** with a runtime-configurable max age (`sys.security.password.expireDays`, seeded, default `0` = never expires). Login is never blocked: an expired password sets the existing `MustChangePassword` flag, and legacy users with no change-time anchor get it backfilled on first login, so enabling the policy never flags the whole user base at once. Create / reset / self-change all stamp the anchor, and self-change resets the window. `ISecurityPolicyProvider` gained a default interface method so existing custom implementations keep compiling, and the config UI exposes the field under the security tab.
- **Personal center fleshed out**: the profile page now surfaces all of avatar / nickname / gender / phone / email (previously only name + password), and users can list and revoke their **own** online sessions (`GET` / `DELETE /api/v1/personal/sessions`) — revoking a session that isn't yours returns `SessionNotFound` (42024) rather than leaking its existence.
- **Business workbench now runs on real data**: it replaced its hardcoded placeholder stats with the signed-in user, the current app's menu leaves as a quick-access grid, and that user's notices — all from APIs that already exist, so no backend change.
- **CodeBlock JSON viewer component**, used to render operation-log request params (previously a raw `<pre>`). Built on Naive's `NCode` + highlight.js with only `json` registered — highlight.js is already a Naive dependency, so it adds no new download — and themed from the Naive palette. Documented in `web/COMPONENTS.md`.
- **Duplicate fixed seed ids are now rejected at startup and in CI.** A colliding id used to fail silently — the second row skipped by the idempotent existence check, or overwritten on upgrade by a `SyncOnUpgrade` seed. `DatabaseInitializer` now tracks claimed ids per entity across all seeds (consumer seeds included) and fails loudly on a collision, and a `SeedIdRangeTests` uniqueness contract turns CI red before any host boots.
- **A bilingual (English / 简体中文) VitePress documentation site**, covering the getting-started journey, core concepts, frontend/backend standards, and component references.
- **A frontend unit-test suite (Vitest)** now runs in `web/` (`npm test`), starting with the i18n merge seam and the Pinia stores.

### Fixed

- **The built-in system module can no longer be disabled, and a module with menus attached can no longer be deleted.** Disabling the system module through the API directly used to leave the portal unreachable with no UI recovery path — the frontend disabled-row guard was never a server-side defense. Both are now enforced server-side (`ModuleProtected` 42013 / new `ModuleHasMenus` 42023).
- **A user with no assigned modules can now log out** from the empty module-picker screen, instead of being stranded at `/module` with no way out.

### Removed

- **Removed unused template components** — `RoleSelect`, `DictRadio`, `DictCheckbox`, `JsonEditor`, and the `BarChart` chart preset — plus a dead theme self-check. Every removal was reference-checked across `web/src`, and `COMPONENTS.md` and the sibling READMEs were updated so no dead links remain. (`LineChart` / `PieChart` stay — the workbench uses them.)

---

## 0.1.1 - 2026-07-16

Patch release: front-end button-level permission enforcement and a small role permission gap.

### Fixed

- **Action buttons are now hidden when the user's role lacks the permission** (create / edit / delete / copy / reset-password / force-logout / restore, plus status toggles). Previously many were shown regardless — notably **every row-action button** (built inside table `render()` functions, which the `v-auth` directive cannot reach) and **all buttons on the organization page** (which had no gating at all); clicking one then failed on the server with a 403. Gating is now centralized in a single `hasPerm(code)` check shared by the `v-auth` directive and render-function buttons, so the UI only offers what the role can actually do.
- **Role "grant users" permission is now assignable.** `PUT /api/v1/sys/role/users` and `GET /api/v1/sys/role/{id}/users` were permission-gated but had no menu node, so the permission could never be granted to anyone but the super admin. The two menu nodes are now seeded (fresh databases pick them up automatically; existing databases need the two rows added manually, or a reseed).
- **User management page now adapts to screen width** — it previously overflowed horizontally on narrow monitors and left a large blank area on wide ones, because the table's flex sizing never reached ProTable's root element.

---

## 0.1.0 - 2026-07-15

**First version published to nuget.org** (the earlier `0.0.1-preview` only ever existed in the repo and was never pushed as a package).

The kernel capability baseline is listed below under `0.0.1-preview`; this section records what was added after preview, and the changes that made it **publishable and upgradable**.

> Version `0.x`: **the API may still change before 1.0**. Breaking changes will be called out explicitly here.

### Added

- **Targeted notification delivery**: notifications can now be sent to everyone / specific roles / specific users (previously broadcast-only). The top-bar bell dropdown was upgraded from plain text to a rich panel (All / Unread tabs, type tags, body preview, timestamps); the publish dialog was widened and gained a recipient picker; added an "in-app message" type.
- **Menu "permission code" route picker now filters by owning app**, plus several interaction improvements in menu management.
- Interaction improvements in role management and dictionary pages.
- Release gate: `backend-release` runs build, tests, and a template smoke test before pushing the package. Publishing is irreversible (nuget.org can only unlist), so nothing red gets published.
- Smoke test for `dotnet new tenon-app` added to CI (the `template-smoke` leg of `backend-ci`), covering a consumer's very first command.
- NuGet packages now ship with SourceLink, a symbol package (snupkg), and a package icon — consumers can step directly into kernel source, which is the prerequisite for the "inherit and override a single step" selling point.
- **SQL diagnostic logging**: failed SQL statements are logged at `Error` with the statement and parameters; statements taking ≥ `Database:SlowSqlMillis` (default 1000ms, disabled when ≤0) are logged at `Warning`. Previously SqlSugar had no logging hooks wired up at all, so a failed query in production surfaced only a driver-level exception — no SQL, no parameters, no timing. Logging goes through `ILogger` only and does not write to `sys_op_log` (that INSERT would self-trigger, causing direct recursion).

### Fixed

- The template no longer references a package version that was never published: `-p:Version` is now stamped into the `dotnet new` template's default value at pack time. Previously the template hardcoded `0.0.1-preview`, which tags never rewrote, so generated projects referenced a nonexistent version from day one (restore barely survived on NuGet's floating-version resolution, with a NU1603 warning; consumers with `TreatWarningsAsErrors`, lock files, or exact-version policies failed outright).
- **Schema drift on upgrade no longer fails silently mid-query**: the table-creation gate is off in production, so nothing backfills missing columns for existing databases. Previously the startup guard only checked whether a table existed, so: kernel adds a column → old database starts up fine → the first query against that table blows up on the driver's "column does not exist" error. Startup now fails immediately and names the exact table and column (`sys_user(Avatar)`), giving two ways to add the missing column. Only missing columns are checked — type/length changes are not.
- Fixed the package author organization name to `Tenon-Net` (the template package previously had `DotNet-MoYu`).

### Removed

- **Breaking**: removed `TenonAdminOptions.ScanApplicationAssemblies`. It was never implemented and never read anywhere in the code, so setting it had no effect. Business assemblies have always only worked via `ApplicationAssemblies.Add()`. Removed before the first package release; removing it after publishing would have been a breaking change.

---

## 0.0.1-preview

Preview release, never pushed to nuget.org. Kernel capabilities (all covered by CI):

- **Auth**: username/password + captcha, JWT + refresh token rotation, login lockout, online sessions with forced logout, mandatory password change on first login
- **RBAC**: roles, three-level menus (directory/page/button), button-level permissions, role-menu authorization. Permission codes are routes — no permission strings in the codebase
- **Multi-org data permissions**: five data scopes, automatically enforced at the query layer via ORM global filters
- **Multi-app portal**: module management, independent menu trees, app switching, user default app
- **Dictionaries and config**: dictionary types/items, key-value config (with caching and event-driven invalidation), config center ("change config without changing code": basic/security/upload/rate-limiting)
- **Logging**: operation logs (auto-recorded with parameter masking), login logs
- **Files**: local upload/download, chunked resumable upload with instant-transfer dedup, signed direct links, disk garbage collection (`FileGcService`)
- **Replaceability**: interfaces + `virtual` + `TryAdd`, four layers of override (config → swap service → inherit and override → override endpoint), locked in by the "six-piece set" tests
- **Zero-config startup**: SQLite by default, CodeFirst table creation, idempotent seeding, random super-admin password printed on first startup
- **Delivery**: containerized (Caddy + compose); multi-replica correctness (Redis-backed cache, rate-limit counters shared across replicas, per-replica snowflake worker IDs, real client IP behind a reverse proxy)
