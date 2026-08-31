# Changelog

This project follows [Semantic Versioning](https://semver.org/).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Release cadence: **development happens on `dev`, releases happen on `main`** — merge `dev` into `main` first, then tag `v*` **on `main`**. Tags are branch-independent, so tagging elsewhere would trigger the release workflow just the same; there's a gate for that: if the tagged commit isn't on `main`, the release is rejected.

Once a tag triggers `backend-release`, it first runs build + tests + template smoke test (`dotnet new tenon-app` must restore and compile successfully), and only pushes to nuget.org if everything is green.

The step-by-step release runbook (version bump, verify, merge to `main`, tag) lives in [`docs/releasing.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/releasing.md).

> When releasing, **both halves' version numbers must be updated together**: the backend version is injected from the tag via `-p:Version`, while `web/package.json`'s `version` is a build-time constant **shown in the login page footer**. Forget to update it, and the version the user sees in the UI won't match the package they installed.

> **This file is the source of truth for what shipped**, not the GitHub Release page: `backend-release` creates that page with `gh release create --generate-notes`, which drafts "What's Changed" from merged PRs only. Feature commits pushed straight to `dev` (common in this repo) never show up there, while unrelated PRs (docs, CI) do — so the release page can look complete while missing the actual content. The runbook's post-release step now replaces that draft with the matching section of this file.

## Unreleased

### Fixed

- **A killed instance no longer blocks its own restart for a full lease TTL.** `WorkerIdLeaseGuard` releases the `WorkerId` lease on graceful shutdown, but a process that is killed outright — Visual Studio "Stop Debugging", `kill -9`, a container restart — never runs `StopAsync`, so the next start hit its own leftover lease and refused to boot with *"WorkerId N 已被节点 … 租约持有"* until that lease expired (30 s at the default `Jobs:HeartbeatSeconds`). `sys_worker_lease` now records the holder's host name, and the guard checks whether the holding process is still alive: a lease left behind by an exited process **on the same host** is taken over immediately. A holder that is still running, or one on any other host, still fails fast exactly as before — a remote pid says nothing about a remote process, so it is always treated as alive. Takeover is a conditional update, so two instances racing to reclaim the same dead lease cannot both win. The very first claim — when no lease row exists yet — is covered the same way: `WorkerId` carries a unique index, so an instance that loses the insert re-reads and retries instead of crashing startup with the raw constraint violation (QA27 follow-up).

## 0.6.0 - 2026-08-12

A 36-finding QA sweep of the kernel, closed in five batches. Most of it is authorization boundaries: the data-scope guarantee that only ever covered business `DataEntity` tables now also covers user, org and file management, and the role system grew an explicit delegation boundary so a non-superadmin holding the role menu can no longer mint themselves a privileged role.

**Four changes alter behaviour that existing installs depend on.** Read *Changed* before upgrading — one of them stops the app from starting.

### Changed

- **`AdditionalDatabases[].DbType` is now required.** It used to default to `"Sqlite"`, so copying a MySQL connection string into a secondary connection and forgetting the type produced a dialect mismatch that only surfaced on the first query. Startup validation now rejects an empty value. **An install that omits `DbType` will fail to start until the type is filled in** (QA29).
- **The user import template addresses references by code, not display name.** `OrgCode` / `PositionCode` / `RoleCodes` / `DirectorAccount` replace the old name columns; an unresolvable value is now a cell error (`ImportCellRefNotFound`) instead of silently binding to whichever duplicate name sorted first. Overwrite also checks that the *target user's current org* is inside the caller's scope, and role grants run through the same delegation policy as the UI. **Templates downloaded from an older version no longer parse** — re-download from the template endpoint (QA19).
- **Assigning a role now requires that role to be marked delegatable.** `SysRole.IsDelegatable` plus `IRoleGrantPolicy` split the role *definition* surface from the role *granting* surface: defining, renaming, deleting a role and configuring its menus or data scope are superadmin-only, and a non-superadmin may only grant roles explicitly marked delegatable, and only to users inside their own data scope. The column is nullable and `NULL` counts as *not* delegatable, so **every role that exists today becomes non-delegatable on upgrade** — a superadmin has to tick the box for any role that non-superadmin administrators are expected to hand out (QA09, QA36).
- **User and org management honour the caller's data scope.** The user list and add/update paths filter by `OrgId ∈ scope`, and the org tree returns in-scope orgs plus their ancestors so the tree still has a root to render from. Positions stay global, being a company-level vocabulary rather than an org asset. **A non-superadmin who could previously see every account now sees only their own scope** (QA08).

### Removed

- The anonymous `POST /api/v1/auth/mfa/challenge/verify` endpoint. Neither template ever called it; TOTP login goes through `POST /api/v1/auth/login/totp`. It could burn an in-flight MFA challenge and disclosed a `userId` on an unauthenticated surface, so it is gone rather than fixed (QA03).

### Fixed

Authorization and disclosure:

- File management is owner-only for non-superadmin: the list filters by uploader, download and delete validate ownership, batch delete checks every target before touching anything, and a cross-owner reference answers `FileNotFound` so the endpoint doesn't confirm whether the file exists. The check applies to *authenticated* callers only — anonymous signed `/view` links are a capability URL guarded by their signature, and have nothing to filter by (QA15).
- `[ActiveSession]` endpoints now resolve and write the data scope, the same way `[RolePermission]` does. Previously the scope context was simply unset on those routes, and an unset context reads as *unrestricted* — so a consumer querying a `DataEntity` from a login-only endpoint saw every org (QA07).
- Self-service password change revokes the account's other sessions, keeping the current one (QA04).
- Marking a notice read checks that the notice is visible to the caller, instead of accepting any id (QA24).
- The SignalR hub validates the session id at connection time and aborts without joining any group, so a force-logged-out client with an unexpired JWT stops receiving pushes rather than staying connected until the token expires (QA25).
- Avatar URLs are validated as local signed file links through the new `IAvatarUrlValidator`; the field previously accepted any string and rendered it into an `img src` (QA25).
- Passwordless SMS login returns one error code whether the phone belongs to an enabled account or not. The wrong-code and expired-code paths were distinguishable, which turned the login step into a phone-number oracle even though code *sending* was already enumeration-safe. Password login's SMS challenge keeps its remaining-attempts feedback, the caller's identity being established there already (QA06).
- MFA bind and recovery reuse the cached dummy hash for unknown accounts, matching the login path, so response time no longer separates "no such account" from "wrong secret" (QA05).
- A disabled role no longer contributes to the portal's module and menu tree. Permission codes already excluded it, so the sidebar offered entries whose endpoints answered 403 (QA11).
- Import row limits apply to the JSON `validate` and `commit` entry points and to the error-report endpoint, not just to the streaming preview that well-behaved clients happen to go through first (QA17).
- Overwrite import with a blank role column keeps the target's existing roles instead of clearing them; an optional column means "don't change", not "set to empty" (QA18).
- Export cells starting with `=`, `+`, `-` or `@` are escaped in the writer, covering every export path at once (QA19).

Data integrity and operations:

- Seed dictionaries, dictionary items and config keys are protected from deletion (`Id < 1000` marks kernel seed data). Deleting the gender dictionary used to be one click, and it silently emptied user-form dropdowns; deleting a `sys.security.*` key silently fell back to `Options` defaults while the config centre still looked authoritative. **Consumer seeds must therefore use `Id >= 1000`** — now written down in `skills/create-entity.md` (QA13).
- Dictionary item values are unique per type (QA13).
- The dictionary lookup used by every form dropdown is `[ActiveSession]` rather than a permission-coded route. It is a cross-module hot path, and granting it per-menu meant every new module that used a dictionary had to remember to re-grant it. Write operations stay permission-coded (QA12).
- A disabled dictionary type returns no items, instead of continuing to serve its enabled entries (QA14).
- Deleting an org or position is refused while active users reference it, and users can no longer delete or disable themselves. A batch containing a superadmin reports `SuperAdminProtected` ahead of the self-deletion guard, the more specific answer of the two (QA10).
- A job's panic alert sends its in-app notice and its e-mail independently. They shared one `try`, so once QA25 started validating notice recipients, a rejected notice target also swallowed the e-mail — the channel most likely to actually reach someone at 3am.
- Soft-deleting a user or role preserves its associations, so restoring from the recycle bin restores the permissions too; the associations are cleaned on permanent deletion instead (QA23).
- With the SQL job gate closed, existing SQL jobs can still have their schedule and name edited — only payload changes are refused. System jobs now lock `HandlerKind`, `HandlerName`, `Props` and `Name` while leaving trigger and run configuration editable (QA20, QA21).
- Snowflake `WorkerId` collisions fail fast. A new `SysWorkerLease` table backs `WorkerIdLeaseGuard`, which claims the id at startup, renews it, and releases it on shutdown; a second instance configured with the same id refuses to start with a readable error instead of silently minting colliding ids (QA27).
- `AddTenonAdminWorker` scans the Services assembly for entities, matching the HTTP composition root. A Worker deployment with CodeFirst enabled used to create only the SqlSugar-layer table and then fail on every query against a missing kernel table (QA28).
- `WorkerIdLeaseGuard.StopAsync` no longer lets a logging failure escape. Lease release is best-effort, but the warning it logged on failure could itself throw during shutdown once the logging providers were disposed, and `Host.StopAsync` then aggregated it into whichever test was disposing its host.

Frontend, both templates:

- The recycle bin has a `job` tab; the backend supported restoring soft-deleted jobs but the UI listed no way to reach them (QA22).
- `LoginOutput` carries `isSuperAdmin`, and the stores fall back to it when the profile request fails. A superadmin usually holds no explicit permission codes, so a failed profile call used to hide every write button until a refresh (QA31).
- The `v-auth` directive is reactive. It removed the element once on mount, so a permission refresh could neither restore a hidden button nor hide a newly-revoked one until the route remounted — the React `<Can>` component already re-rendered (QA32, QA35).
- Login skin names come from i18n keys rather than hardcoded Chinese (QA32, QA34).
- `vite preview` pins its port in both templates, matching the dev server. Without `strictPort` it silently moves to the next free port, and the docs and scripts that name a fixed port then open a different application (QA30, QA33).

### Added

- `job` gets its own structured tab in the config centre of both templates, instead of appearing among free-form "other" keys where its retention setting could be deleted like a custom row (QA13).
- `ReplaceabilityTests` covers a core service registered *before* `AddTenonAdmin()`. The suite mostly used post-registration `Replace`, which stays green even if a `TryAdd` regresses to a plain `Add` — the guarantee the suite exists to protect (QA29).
- `FileOwnerTests` runs. All four tests covering the QA15 owner isolation fix shipped as `[Fact(Skip = ...)]`, leaving a P1 control with no executing coverage; the fixtures were rebuilt and the suite now runs 694 tests with nothing skipped.

## 0.5.4 - 2026-08-06

### Fixed

- **CodeFirst column upgrades on tables that already have data** (#31). `SysUser.ForceTotp`/`TotpEnabled` and `SysSession.AbsoluteExpiresAt` are now nullable database columns — SQL Server rejects `ADD`ing a `NOT NULL` column without a `DEFAULT` to a non-empty table, so upgrading a live kernel install used to fail on this dialect. Existing rows read back as `NULL`; the read path treats that as the pre-upgrade default (MFA flags `false`, absolute expiry falling back to `ExpiresAt`) without changing the CLR type of the already-published public properties. Covered by a new regression test, `CodeFirstNullableUpgradeTests` (drops the columns from a seeded table, then asserts CodeFirst re-adds them and old rows still read correctly), now also run in the SqlServer dialect-sensitive CI subset.
- Documented the underlying rule for anyone adding columns to an already-shipped entity: `docs/coding-standards.md` §1.3, a new "已有表加列" section in `skills/create-entity.md`, and a tip box in the [deployment guide](https://tenon.52moyu.net/guide/deployment/) explaining why evolved columns must be nullable.

## 0.5.3 - 2026-08-03

Two consumer-facing surfaces land on the same patch: **same-process secondary databases** (SqlSugar multi-ConfigId) and **branded external login** (GitHub / personal WeChat packages + dual-template UI, with pending-link hardening).

### Added

- **Same-process additional databases (multi-ConfigId)** (#28). Configure `TenonAdmin:AdditionalDatabases` with a unique `ConfigId` (reserved name `TenonAdmin` is the main DB). Access secondaries with `db.AsTenant().GetConnection(configId)`; `IRepository<T>` and CodeFirst/seed still hit main only. Soft-delete, data-scope, and audit AOP are **off by default** on secondaries and opt-in per connection (`ApplySoftDeleteFilter` / `ApplyDataScopeFilter` / `ApplyAuditAop`). Site guide: [Configure Multiple Databases](https://tenon.52moyu.net/zh/guide/multi-database).
- **Optional satellite packages `TenonAdmin.Auth.GitHub` and `TenonAdmin.Auth.WeChat`.** Same shape as WeCom/DingTalk: install the package, call `AddTenonAdminGitHubAuth` / `AddTenonAdminWeChatAuth` before `AddTenonAdmin()`. GitHub uses OAuth App with fixed code `github` and scope `read:user`; personal WeChat uses website-app `qrconnect` with code `wechat` and Subject = `unionid` only (no openid fallback — empty unionid fails the exchange).
- **Branded external-login UI on both templates** (login strip + personal bindings, zero-shared). Known provider codes map to local SVG brand icons; system config gains a third-party login tab driven by `GET /api/v1/auth/external/providers/all` for enable/display toggles. Frontend brand map also reserves icons for codes without a kernel package yet (e.g. gitee/qq placeholders).
- **Pending-link claim for unbound SSO.** When an external identity is resolved but not bound, the callback issues a short-lived pending-link plus a browser-only binder cookie (`tn_oauth_pending`); after password/SMS login the client calls `POST /api/v1/auth/external/pending-link/claim`. Claim requires the same browser (binder match); wrong binder does not burn the ticket. Covered by integration tests and ADR 0007 / `docs/external-login-brand/`.

### Fixed

- **WeCom / DingTalk providers** use `IHttpClientFactory` and clearer exchange error mapping (aligned with the new GitHub/WeChat packages).
- **MultiConfigIdTests on non-SQLite CI legs**: dual-connection fixtures now force the active `TestDb` dialect onto secondary `AdminDatabaseConnectionOptions` (default `DbType` is Sqlite and was pairing with MySQL/Postgres connection strings).

### Changed

- OpenAPI schemas on `web/` and `web-react/` regenerated for external-auth providers/all and pending-link/claim; temporary path casts in the API clients dropped.
- Construction-only external-login review artifacts removed; keep decisions, ledger, and QA checklist under `docs/external-login-brand/`.

## 0.5.2 - 2026-07-31

Optional application security lands as **independent, off-by-default switches** — not a full MLPS Level-3 profile. Also fixes SQL Server long-text mapping that broke Chinese error text in the job scheduler suite.

### Added

- **Optional TOTP (authenticator) second factor**, off by default. Runtime master switch `sys.security.totp.enabled` (config center, same style as captcha) plus deploy floor `Security:Totp:*`. Self-service bind (`POST /api/v1/auth/mfa/bind/start|complete`) with one-time recovery codes; admin force-TOTP and clear MFA; login signals `40018` / `40020` and recovery path. Both Vue and React templates: Account Security page, bind UI with QR, login Modal for forced unbound users (no permanent login-page bind link), re-auth modal for sensitive writes.
- **Optional Cookie + CSRF session mode** (`Security:Session:CookieMode`, default false). HttpOnly refresh cookie, double-submit CSRF (`tenon_csrf` / `X-Tenon-CSRF`), dual-template silent refresh. Configurable idle / absolute session caps when you want them.
- **Narrow secret protection** for TOTP seeds (`IDataProtectionKeyProvider` / `ISecretProtector`) and diagnostic security baseline precheck API (not a certification report).
- Product direction documented as **general admin kernel + optional security** (ADR 0006); full Level3 multi-phase roadmap retired. Site page [Auth & Security](https://tenon.52moyu.net/zh/backend/auth-security) covers TOTP and CookieMode.

### Fixed

- **SQL Server long-text columns map to Unicode `nvarchar(max)`** via SqlSugar `CodeFirst_BigString` (#26). Bare `text` was non-Unicode and corrupted Chinese `ErrorText` from job orphan reaping on the full SqlServer suite (#25). Affects job / op-log / notice / exception-log large string columns.
- **React offline menu icons** load after the lazy icon pack finishes (`AppIcon` re-render + test mock hoist), so sidebar icons no longer stay blank until a folder is expanded.
- **User-admin UX polish**: wider edit form, operations column order (edit / delete / more), force-TOTP and clear-MFA copy; personal-security admin hints gated by permission.

### Changed

- Removed product paths for MFA bind invites, InitGrant, emergency reset, and idle-account auto-disable heavy plays. Historical `Profile=Level3` is transition-only; new deploys should use the independent keys above.
- README / `CONTEXT.md` terminology updated for optional security; agent config notes under `docs/agents/security-optional-config.md`.

### Removed

- Construction-only agent docs for the retired Level3 phase-1 program (review/closeout prompts). Keep ADR 0006 and the optional-security config / UI checklist for maintainers.

## 0.5.1 - 2026-07-28

A patch for the scheduled-jobs surface that shipped in 0.5.0: an unset snowflake `WorkerId` no longer crashes every scheduler tick, and job PUT on PostgreSQL no longer returns a broken body.

### Fixed

- **Scheduler node heartbeat no longer throws when `TenonAdmin:Id:WorkerId` is unset** (#24). SqlSugar evaluates expression-tree members when building `SetColumns`; the previous `idOptions.WorkerId ?? 0` ran against a null options object and flooded the tick loop with exceptions. Locals (`workerId`, `pid`, `hostName`, `startTime`) are hoisted before the expression so the tree only sees constants and locals.
- **Job update (PUT) on PostgreSQL no longer returns a non-JSON body.** `JobService.UpdateAsync` switched to the same `Updateable(entity).IgnoreColumns(...)` pattern used elsewhere (Dict/User), and the nullable `NextRunTime` CAS is split into two branches so a ternary does not land in the expression tree.
- **Multi-replica smoke is less flaky after failover.** Caddy upstreams gain health checks and `lb_try_duration`; the multi-smoke script polls until the 5s job has three runs instead of a fixed 25s wait. Both frontend OpenAPI clients pick up the missing `nodeInstanceId` field that had drifted from the contract.

### Changed

- Runtime architecture diagrams and README previews refreshed for in-kernel jobs, optional WorkerHost, and the SignalR hub path.
- Documented the MLPS assessment boundary (ADR-0005) and matching domain terms in `CONTEXT.md`.

## 0.5.0 - 2026-07-29

A feature release: **scheduled jobs land in the kernel**, with no new dependency and no extra process by default. Both frontend templates ship the management UI; an optional Worker host keeps jobs running when the API is down.

### Added

- **Scheduled jobs, in the kernel, with no new dependency.** Write an `IAdminJob`, register it with one `TryAddEnumerable` line, and the admin UI can drive it on a cron — no package to install, nothing to configure, no extra process. Also ships two payload kinds that need no code at all: HTTP (behind an SSRF fence) and SQL (off by default; enabling it admits that job-edit rights are DBA rights). The cron engine is self-written, six fields with seconds first and the full `* , - / ? L W #` syntax; behaviour was checked against Furion's TimeCrontab with live probes on both sides, and the deliberate divergences are recorded in `docs/scheduling-ledger.md` §4.1.
  - "Jobs must not stop when the backend stops" resolves into three shapes, none of which need a code change: the schedule survives a restart (misfire policy decides whether a missed occurrence is caught up), two API replicas elect a leader through a database lease so one dying doesn't stop the schedule, and an optional `samples/WorkerHost` (three-line `Program.cs`, `AddTenonAdminWorker`) keeps jobs running with the API down.
  - **Double firing is prevented by a claim, not by the lease.** Every fire compare-and-sets the row's `NextRunTime`; an old leader waking from a GC pause finds the slot already advanced and gets nothing. The lease only decides who scans, which is also why the dual-replica smoke test can't prove the claim — with one scanner there's nothing to race. That test kills the leader to prove takeover; the claim itself is covered by `JobClaimTests`.
  - Built-in log-cleanup dogfood job, 13 HTTP endpoints under `/api/v1/sys/job`, ErrorCode `47xxx`, and a top-level **任务调度** menu with jobs / run log / monitor pages on both templates (plus a CronEditor with live preview). `GET /handlers` returns `{ handlers, sqlEnabled }` so the form can disable SQL when the gate is off; run-log paging accepts `FireInstanceId` so the retry drawer does not silently miss siblings.
  - Consumer guide: [Scheduled Jobs](https://tenon.52moyu.net/zh/guide/scheduled-jobs) and `skills/create-job.md`. Execution ledger: `docs/scheduling-ledger.md`; design decision: `docs/adr/0004-scheduling-in-kernel-self-built.md`.

### Fixed

- **Operation-log masking now covers header-shaped fields.** Masking matches on field names (`password`, `token`, …), and a scheduled HTTP job carries its whole header set in one `headers` value — a bearer token therefore landed in `sys_op_log.ParamJson` in plain text. `header`, `authorization`, `apikey` and `cookie` joined the keyword list.
- **Same-name node restarts no longer leave SerialSkip jobs stuck forever.** Orphan running rows were reaped only when the node name's heartbeat went stale; a restart that reuses the same `NodeName` refreshes the heartbeat and leaves the previous process's open run looking alive, so every later tick is skipped. Nodes and run logs now carry a process `InstanceId`; reaping keys on name + instance. Concurrent capacity / local serial checks also enter the fire path under one gate (`TryFireAndTrack`) instead of check-then-act outside it; the local busy slot is released in the fire task's `finally` so an `await FireAndTrack` that finishes cannot still look busy to the next call. API and Worker hosts share the same `AdminJobsOptions` validation so a Worker cannot start with a silently broken SSRF CIDR list.
- **Vue job UI walkthrough fixes:** the monitor trend legend no longer covers the x-axis dates, and the create form defaults cron to `0 0 0 * * ?` so the editor no longer looks filled while the model is empty.
- **React table toolbars put primary actions on the left**, matching the Vue template (`headerTitle` for add / batch-delete; pro settings stay on the right).

### Changed

- **Job create/edit forms are denser on both templates:** collapsible basic / trigger / handler / advanced sections, two-column advanced rows, and React CronEditor preview styling aligned with Vue.

## 0.4.0 - 2026-07-26

A feature release: xlsx import/export lands as an optional package, and both frontend templates ship the wizard that drives it. Nothing in the core four packages changed shape — install nothing and the kernel behaves exactly as it did in 0.3.3.

### Added

- **`TenonAdmin.Excel` optional satellite package** for xlsx import/export: MiniExcel (read/write) + DocumentFormat.OpenXml (template dropdowns only). Core defines the contracts (`IImportProfile` / `IExportProfile` / `IImportRunner` / codec trio); the satellite supplies codecs. Install the package and call `AddTenonAdminExcel()` **before** `AddTenonAdmin()` — without it every codec call fails loud with `46001`. Kernel demos: user import (dict + name-based FK + org scope) and user / op-log export; both frontend templates ship an import wizard + export column picker (zero-shared). Consumer guide: `skills/wire-import-export.md` and site page [Wire Import/Export](https://tenon.52moyu.net/zh/guide/import-export). Execution ledger: `docs/excel-ledger.md`.

### Fixed

- **The duplicate-key lookup is batched under the database parameter limit.** A profile resolves existing rows with `keys.Contains(...)`, which compiles to one SQL parameter per business key — SQL Server caps a statement at 2100 parameters and older SQLite at 999, while `MaxImportRows` defaults to 5000. The default configuration therefore invited an import that threw somewhere past two thousand rows. `IImportRunner` now chunks the lookup at 500 keys and merges the results, so a profile copied from the consumer guide inherits the fix instead of the bug. No test caught this: the row-limit test lowers the cap to 2 and sends 3 rows, and no test ever sent thousands.
- **The import preview no longer presents "already exists" as an error.** Re-running the wizard against a database that already held the batch reported every row as broken and painted the business-key cells red — under the Overwrite strategy, a screen of red hid the fact that every row was about to update normally. `ImportRunner.CommitAsync` already draws this line (`46010` is excluded from the hard errors and routed by `DuplicateStrategy`); only the preview failed to mirror it. Each template gained its own `utils/importDup.ts` (duplicated on purpose — the two templates are zero-shared).
- **The user import's enabled-status column now offers a dropdown** instead of a free-text box. The kernel already seeded a reusable two-state `common_status` dict that nothing consumed, while the import column carried no `DictTypeCode`. Attaching the existing dict delivers the template dropdown, the wizard's `DictSelect`, and label→value translation at once — no contract, codec, or frontend change.
- **The wizard's dict dropdown no longer clips its options.** The preview grid's columns are narrow, so a popup sized to its trigger rendered the options as truncated stubs (measured: a 66px trigger in the Vue template). Both templates decouple popup width from trigger width in the wizard only.

### Changed

- **Each frontend template now has one tested blob-download helper** (`utils/download.ts`) instead of the same nine lines inlined in the wizard, the user page, and the op-log page. Both ways of getting them wrong are silent under typecheck, lint, and build: skip `revokeObjectURL` and the object URL leaks; skip the download name and the file lands as a uuid.
- **The Vue template derives its primary-color ramp in one place** (`theme/mix.ts`). The Naive theme overrides and the CSS-variable writer each hand-rolled the same mixing numbers, so changing one and forgetting the other desynchronized raw CSS from Naive components with nothing failing anywhere. The React template keeps its own copy, as the self-contained templates require.
- **The template smoke test scaffolds with a hyphenated project name** and statically asserts the generated `Dockerfile` names no sanitized-name literal. The 0.3.3 bug shipped past this job precisely because it scaffolded as `Probe`, a name with nothing for `dotnet new` to sanitize.
- Documentation: a site page and consumer skill for wiring import/export onto your own entity, runtime i18n architecture diagrams in the READMEs, and `TenonAdmin.Excel` added to the package overview (the architecture and structure pages still counted eight packages and omitted it).

## 0.3.3 - 2026-07-25

A patch release for the consumer's very first `docker build`: a project generated with a hyphenated name now actually builds. Found by the `tenon-example` reference application while deploying it to production.

### Fixed

- **A generated `tenon-app`'s `Dockerfile` now builds regardless of the project name.** `dotnet new tenon-app --output <hyphenated-name>` (e.g. `tenon-example`) sanitizes hyphens to underscores when substituting the project name into file *content*, but uses the literal name when renaming files — so the generated `Dockerfile` referenced a `.csproj`/`.dll` (`tenon_example`) that never existed, and `docker build` failed with "file not found" for any consumer who picked a hyphenated project name, a common convention. The `Dockerfile` no longer contains the project name literal at all: it publishes via a `*.csproj` glob and resolves the entry assembly at container start from its `.deps.json` sibling (the one file dependency DLLs never carry), so it works for any project name without per-consumer edits.

## 0.3.2 - 2026-07-24

A patch release for the consumer's very first command: a project generated by `dotnet new tenon-app` now actually starts under `dotnet run`. Found by the `tenon-example` reference application while consuming the published `0.3.1` packages.

### Fixed

- **A generated `tenon-app` now starts under a plain `dotnet run`**: the template shipped no `Properties/launchSettings.json`, so `dotnet run` resolved to `Production`, where automatic CodeFirst schema creation is off by design (§12). First startup threw `InvalidOperationException` on the twelve missing seed tables and left an empty `admin.db` behind — directly contradicting the template README's zero-configuration promise of automatic tables and seeds. The template now ships a launch profile pinning `ASPNETCORE_ENVIRONMENT=Development` and the `http://localhost:5100` address the Vue dev proxy already targets, and its README warns against deleting the file and points production schema creation at `EnableCodeFirstInProduction` or a DBA-provisioned schema.

### Changed

- **The template smoke test was upgraded from "it compiles" to "it runs"**: `templates/smoke-test.ps1` now executes `dotnet run` verbatim after the build — injecting no environment variables — and requires HTTP 200 from `/health`. The bug above shipped past seven green CI checks precisely because `template-smoke` only built the generated project and never started it.

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
