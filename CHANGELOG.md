# Changelog

This project follows [Semantic Versioning](https://semver.org/).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Release cadence: **development happens on `dev`, releases happen on `main`** — merge `dev` into `main` first, then tag `v*` **on `main`**. Tags are branch-independent, so tagging elsewhere would trigger the release workflow just the same; there's a gate for that: if the tagged commit isn't on `main`, the release is rejected.

Once a tag triggers `backend-release`, it first runs build + tests + template smoke test (`dotnet new tenon-app` must restore and compile successfully), and only pushes to nuget.org if everything is green.

The step-by-step release runbook (version bump, verify, merge to `main`, tag) lives in [`docs/releasing.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/releasing.md).

> When releasing, **both halves' version numbers must be updated together**: the backend version is injected from the tag via `-p:Version`, while `web/package.json`'s `version` is a build-time constant **shown in the login page footer**. Forget to update it, and the version the user sees in the UI won't match the package they installed.

> **This file is the source of truth for what shipped**, not the GitHub Release page: `backend-release` creates that page with `gh release create --generate-notes`, which drafts "What's Changed" from merged PRs only. Feature commits pushed straight to `dev` (common in this repo) never show up there, while unrelated PRs (docs, CI) do — so the release page can look complete while missing the actual content. The runbook's post-release step now replaces that draft with the matching section of this file.

## 0.3.1 - 2026-07-24

A consumer-bootstrap patch release: the project template now restores cleanly on creation, and the Vue template ships with an audited dependency tree and an explicit install-script policy.

### Added

- Added a GitHub issue form for publishing and tracking user-facing project announcements such as releases, maintenance, deprecations, and security notices.

### Fixed

- **`dotnet new tenon-app` now restores the generated project without a template-engine warning** (#22): the template declares `TenonApp.csproj` as its primary output, so the existing restore post-action can locate and restore the generated project. The template smoke test now exercises the consumer command without `--skipRestore`, asserts the restore asset exists, and builds with `--no-restore` to prove the post-action did the work.
- **The Vue template's reported npm advisories are resolved** (#22): ECharts and vue-echarts are upgraded together, vulnerable `brace-expansion` and `js-yaml` resolutions are replaced, and the dependency tree now audits with zero known vulnerabilities.
- **Release-exact Vue source passes `git diff --check` when imported unchanged** (#22): trailing whitespace and end-of-file formatting defects in the published source and design handoff files were removed.

### Changed

- **The Vue template now records an explicit npm install-script policy** (#22): only the pinned `esbuild@0.25.12` install script is allowed; the previous `vue-demi` script is gone with the vue-echarts upgrade.
- Maintained guidance now uses the .NET 10 exact-version template syntax, `TenonAdmin.Templates@<version>`, instead of the deprecated `Package::version` form.
- Refreshed the project README copy across Chinese, English, and Japanese, added the `tenon-example` CRM reference-app execution ledger, and expanded the release runbook's version-badge checks.

## 0.3.0 - 2026-07-23

A second official frontend template — a full React 19 + Ant Design port of `web/`, self-contained and zero-shared by design — plus a batch of parity fixes discovered while building it, and a SqlServer/PostgreSQL startup performance fix.

### Added

- **`web-react/`: a new React 19 + Ant Design (antd 6) admin console template**, feature-for-feature parity with `web/` (Vue 3 + Naive UI) against the same backend contract, but deliberately **self-contained and zero-shared** — no code is factored out between the two templates (rationale in `docs/react-template-ledger.md`). Covers the three login skins, dynamic routing driven by the backend menu tree, a six-mode layout shell with settings drawer, tabs + keep-alive page caching, shared components (dict trio, form container, selectors, upload, Markdown editor, charts, icon picker), every business page (org/menu tree-tables with button management, role authorization, dict master-detail, the three audit-log pages, five personal-center pages, dashboards, and more), SMS login, SignalR realtime push (force-logout, notice badges), a command palette (Ctrl/Cmd+K), and Docker/Caddy/nginx deployment configs. `npm run dev` runs on port 5174.
- **Dynamic-route diagnostics page** (both templates): when a menu entry's route matches but its view component can't be resolved, the app now shows a diagnostic page instead of a silent 404 or a stale view — helps distinguish a typo'd component path from a glob that didn't match (`web/src/views/error/MissingRoute.vue`, `web-react/src/router/detailRoutes.tsx`).

### Fixed

- **SqlServer/PostgreSQL CodeFirst startup could take 10+ minutes on table-heavy schemas** (#16): each `CREATE TABLE`/`CREATE INDEX` auto-committed independently, so every statement paid its own transaction-log flush (measured 152 tables at 10+ minutes). `DatabaseInitializer` now wraps the whole `CodeFirst.InitTables` call in one transaction, collapsing that to a single flush at commit. MySQL's DDL already auto-commits regardless, so the wrapper is a no-op there.
- **`web/` production reverse-proxy configs were missing `/hub`**: `Caddyfile`/`nginx.conf` only proxied `/api` and `/health`, so the SignalR realtime channel (force-logout, unread-count refresh) failed its WebSocket handshake behind Docker/Caddy or nginx and silently degraded to 30-second polling. Fixed to match `web-react/`'s already-correct config.
- **`web/`'s Markdown editor had a stored-XSS gap and phoned home** (the same defect class found and fixed on `web-react/` during its build): `md-editor-v3` renders raw inline HTML with no sanitizer (a malicious notice author could steal a viewer's JWT) and lazy-loads highlight/katex/mermaid/prettier/echarts from unpkg on mount, breaking air-gapped self-containment. Fixed symmetrically: a bundled `XSSPlugin` sanitizes rendered Markdown and echarts gets a no-op instance; the other four extensions are disabled.
- **`web/src/api/schema.d.ts` was missing the site-info `Logo` field**: the backend DTO gained the field earlier but `gen:api` was never re-run afterward, and the hand-written `unwrap<T>` type assertion masked the drift from typecheck. Regenerated to match the backend contract.

### Changed

- **Notice bell redesigned as a single list** (both templates, symmetric): dropped the "all/unread" tabs — unread is a filter, not a category, so presenting it as a sibling of "all" was misplaced navigation. Unread rows now get a dot, bold text, and a tinted background; read rows dim. Filtering by read state moved to the personal notice page.

## 0.2.2 - 2026-07-22

A small fix-and-polish release: consumer seed Ids are no longer capped at a tiny fixed range, and the web UI gets a new enterprise color palette with a supporting texture pass.

### Fixed

- **Consumer seed Id ceiling is now dynamic, not a hardcoded 4095** (#21): the fixed range consumer seeds could use for their own menus/dictionaries was `[1000, 4095]` — only ~3096 slots, too tight for systems with many menus that want semantically meaningful numbering. `DatabaseInitializer` now computes the ceiling at startup (`SnowflakeIdGenerator.CurrentFloor()`, the smallest snowflake Id the instance could produce *right now*) instead of a static constant — any fixed seed Id below that value can never collide with an Id this instance generates from now on, since the clock only moves forward. This gives consumers effectively unlimited room for semantic Id schemes while keeping the startup check that rejects genuinely dangerous (future-colliding) values.

### Changed

- **New default color palette** (light: daisyUI *corporate*, dark: *business*): replaces the previous gray/indigo palette, converted from daisyUI's OKLCH values to sRGB hex with the same interpolation used elsewhere in the token pipeline. Radius/typography/shadows keep the existing rounder in-house scale. Warning and tertiary-text colors were deepened beyond daisyUI's raw values (2.5:1 / 2.9:1) to clear the documented AA-large contrast floor (3.5:1 / 3.6:1). A previously persisted custom accent that's no longer in the palette is migrated automatically on first load. Brand identity (logo, indigo `#646CFF`) is unchanged — brand color and UI accent are deliberately independent.
- **Texture pass**: cards now have a 1px border (reads clearly in dark mode, where shadows don't), table headers use a heavier secondary-color weight, and scrollbars are slimmer with translucent thumbs.
- `vite preview` now proxies `/api`, `/openapi`, and `/hub` the same way the dev server does, so a built-and-previewed frontend can talk to a local backend on memory-constrained machines.

## 0.2.1 - 2026-07-19

A small polish + quality release: the login page's brand logo is now operator-configurable, and the three quality-audit judgment items left after 0.2.0 are closed.

### Added

- **Backend-configurable login logo**: a new `sys.site.logo` config key (GroupCode `sys`, seeded empty) is exposed through the anonymous site-info whitelist, so a consumer can point the login page at their own brand logo without editing code. The login page renders an `<img>` when a URL is set and falls back to the built-in vector logo when empty — across all three skins. Edited in the existing "系统基础配置" structured form (plain URL field); saving takes effect immediately. The login skin stays a deploy-time frontend constant (`DEFAULT_SKIN`), per the "deploy-time-fixed → constant, not backend config" convention; the skin switcher moved from bottom-center to the top-right, beside the theme/language pills (still per-browser remembered). (#11)

### Fixed

- **Snowflake ID epoch alignment** (#13): the built-in snowflake generator's epoch moved from 2026-01-01 UTC to 2020-02-20 02:20:02 UTC, `Yitter.IdGenerator`'s default. Projects migrating legacy Yitter data used to get new IDs an order of magnitude smaller than the old ones, so sorting a mixed table by `Id` no longer matched insertion order; same epoch keeps both generators in lockstep. Moving the epoch earlier is safe in both directions that matter — every new ID stays strictly larger than any already-issued one, no collision or reordering.
- **Menu button-permission UI gate**: the "配置权限" entry and the add/edit/delete/batch actions inside the button manager now honor the same client-side `hasPerm` gates as the main menu table, so a read-only user no longer sees write affordances they can't use. Server-side `[RolePermission]` was (and remains) the actual enforcement — this only aligns the UI.
- **`/module` route title i18n**: the app-picker route title now uses the existing `module.choose` i18n key instead of a hardcoded Chinese string, so it renders correctly under English.

### Changed

- **Internal**: password-expiry and schema-version audit timestamps now read the injected `TimeProvider` (`GetLocalNow()`) instead of `DateTime.Now` directly, aligning with the audit-field time convention and making the expiry decision unit-testable with a fake clock. Runtime behavior is unchanged. The new `TimeProvider` parameters are trailing and optional, so consumers subclassing `AuthService`/`UserService`/`PersonalService` are unaffected.

## 0.2.0 - 2026-07-18

OAuth/SSO federation, real-time push notifications, and a batch of backend "quick win" modules (exception log, email channel, password history, server monitor, cache invalidation) — plus a capability-based entity-base split that finally makes org-scoped + audited + hard-delete expressible together.

### Added

- **SMS login hardening** (referencing XiHan.BasicApp's second-factor design): two independently-toggleable features, both **off by default** and runtime-configurable from the config center's security tab —
  - **SMS second factor** (`sys.security.mfa.enabled`): after the password (and captcha/lockout) checks pass, users with a bound phone must confirm an SMS code. The login endpoint signals this with new error code `SmsCodeRequired` (40009) carrying a challenge id; `POST /api/v1/auth/login/sms` (+ `/resend`) completes the login. Users without a phone log in as before, so flipping the switch can never lock anyone out (the seeded super admin has no phone).
  - **Passwordless SMS sign-in** (`sys.security.smsLogin.enabled`): `POST /api/v1/auth/sms/send` + `POST /api/v1/auth/sms/login`; the login page shows an SMS tab when enabled (surfaced via the anonymous site-info endpoint).
  - Server-side abuse controls throughout (XiHan's visible gap): per-phone resend cooldown (60s) and daily cap (10) enforced in the backend, codes are single-use with 5 attempts / 5-minute TTL, the send endpoint honors the image-captcha toggle, and unknown/duplicate/disabled phones get an indistinguishable success-shaped response (anti-enumeration). Codes live only in cache — **no schema change**.
  - **`ISmsSender` abstraction** with a dev-friendly `LoggingSmsSender` default (codes go to the backend log; no vendor SDK enters the kernel). Consumers register a real provider before `AddTenonAdmin()`: `services.AddSingleton<ISmsSender, AliyunSmsSender>();` — locked by a new `ReplaceabilityTests` case.
- **External login (OAuth/SSO)**: a full `IExternalAuthProvider` framework (Core) with a built-in, zero-new-package `OidcExternalAuthProvider` (discovery document + JWKS + signed `id_token`, PKCE S256 — works with Keycloak/Entra/Authing) plus two independent satellite packages, `TenonAdmin.Auth.WeCom` and `TenonAdmin.Auth.DingTalk` (bare `HttpClient`, no vendor SDK). `GET /api/v1/auth/external/providers` lights up login-page buttons; `authorize`/`callback` hand back a one-time exchange ticket so tokens never ride the redirect URL; the personal center gets bind/unbind/list. `AuthService.LoginByExternalAsync` is a new template method (resolve identity → find binding → unbound-identity policy is reject-by-default or auto-provision → reuse the existing token-issuing tail). New `sys_user_external` table, unique on `(Provider, Subject)`. New error codes 40013–40017. Design tradeoffs recorded in `docs/adr/0002`.
- **Real-time push (SignalR)**: two previously polling/lazy signals are now instant — notice publish pushes `notice-changed` (the bell badge updates immediately instead of on a 30s poll) and session revocation pushes `force-logout` (a kicked user is logged out immediately instead of on their next 401). Zero new backend package (SignalR ships in the shared framework). `IRealtimePublisher` (Core) defaults to a no-op implementation, off by default via `AdminRealtimeOptions.Enabled`, so the feature is fully opt-in and replaceable; the frontend's `useRealtime` composable falls back silently to polling if the connection fails. Recorded in `docs/adr/0003`.
- **Capability-based entity bases** (#10): `BaseEntity` split so audit fields no longer imply soft-delete — new `AuditEntity` (Id + audit columns, no delete flag) and `OrgAuditEntity` (`AuditEntity` + org-scoped, still no soft-delete), enabling the previously-inexpressible "org-scoped + audited + hard-delete" combination. `IRepository<T>.DeleteAsync` is now capability-aware (soft for `ISoftDelete` entities, physical otherwise); `RestoreAsync` throws for non-soft entities. Every built-in `Sys*` entity stays on `BaseEntity`/`DataEntity`, so existing soft-delete behavior is byte-for-byte unchanged.
- **`PrimaryId` entity base** (#8/#9): detail/child tables (e.g. a master-detail sub-table) can now carry just a snowflake `Id` without the audit quartet or soft-delete flag — `BaseEntity` now inherits from it.
- **Exception log**: unhandled (non-`AdminException`) exceptions are now recorded to `sys_exception_log` (path/method/trace id/type/message/stack trace, best-effort) without suppressing the 500 response; new tab on the log page.
- **`IEmailSender` channel**: mirrors `ISmsSender` — a dev-friendly `LoggingEmailSender` default and a built-in `SmtpEmailSender` (BCL `SmtpClient`, used automatically once `TenonAdmin:Email:Host` is configured). No schema change, no page.
- **Password history reuse prevention**: new `sys_password_history` table + `IPasswordHistoryService`, gated by `sys.security.password.historyCount` (default `0` = off). Applies to self-service and admin-initiated password changes; account creation only records, never checks. New error code 42025.
- **Server monitor page**: `IMonitorService` reports process/host metrics (environment, process, GC, thread pool, disks) from pure BCL APIs — new page with manual refresh.
- **Cache invalidation admin page**: since the default in-memory cache provider can't enumerate keys (and keys/values can carry PII/OTPs), this ships four targeted, DB-id-driven invalidation actions instead of a key browser — flush auth permissions, flush dict cache, flush config cache, and bump the portal generation. Tradeoffs recorded in `docs/adr/0001` (an entity-diff-log feature was evaluated and dropped as redundant with the existing operation log).
- **Reusable detail-page pattern**: a convention (`views/**/detail.vue` → routed tab, or an in-place swap within the list tab) plus a shared `DetailPage` shell, so consumers can build per-record detail views without modals/drawers. Foundation only — no product page wired to it yet.
- Frontend polish: a route-navigation loading bar; tab middle-click-to-close and pin-to-keep-open; a dev-only "copy config" button in the settings drawer that copies the current theme defaults as JSON; external-link and iframe menu node types (no backend change — a menu's `path`/`component` combination decides the behavior).

### Changed

- **Breaking (source)**: `IAuthService` now has five new members beyond 0.1.2's baseline — SMS (`LoginBySmsChallengeAsync` / `ResendSmsChallengeAsync` / `SendSmsLoginCodeAsync` / `LoginByPhoneAsync`) and external login (`LoginByExternalAsync`). Consumers **subclassing `AuthService`** (the documented path) are unaffected — every new constructor and template-method parameter is optional. Consumers implementing `IAuthService` from scratch must add all five methods. New auth error codes 40009–40017; SMS send throttling reuses `TooManyRequests` (40008).

### Fixed

- **Instant-upload file dedup**: a hash-match probe now inserts one row per referencing user/record instead of reusing an existing row, so deleting one reference can no longer orphan another reference's file or its physical blob (`FileGcService` now checks for surviving same-path rows before deleting from storage); a related idempotency gap, where a repeated probe from the same user leaked an extra row, is also closed.
- **External login (post-implementation review)**: deleting a user now unbinds their external identities in the same transaction (previously left a dangling `sys_user_external` row that permanently locked that external identity out of both login and re-binding); login-mode `state` is now bound to the initiating browser via an `HttpOnly`/`SameSite=Lax` cookie to close a login-CSRF gap; the built-in OIDC provider now refuses HTTP discovery metadata outside `Development`.
- The iframe menu's `src` is now captured once instead of recomputed on every route change (previously could reload or leak state across tabs); the server monitor page filters out zero-capacity disks.

---

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
