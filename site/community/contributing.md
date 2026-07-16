# Contributing Guide

TenonAdmin has two halves: `backend/` (.NET 10 kernel + sample host + tests) and `web/` (Vue 3 + Naive UI admin template). You can change either independently, or both together.

## Before you start

- Fork the repo and clone it locally.
- **Development happens on the `dev` branch; `main` only accepts release merges** — target your PR at `dev`, not `main`. `dev` is merged into `main` and tagged only at release time (see [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)).
- File bugs / feature requests through one of the three GitHub Issue templates (Bug report / Feature request / Question) — the repo has blank issues disabled. **Do not** open a public issue for a security vulnerability; see "Security issues" below.

## Local development environment

Backend (run from the repo root; the solution file is `.slnx`, not `.sln`):

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx                       # xUnit + WebApplicationFactory, defaults to SQLite
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~DataScopeTests"   # run a single test class
dotnet run   --project backend/samples/MinimalHost         # zero-config run, http://localhost:5100
```

Running tests against MySQL (matches one leg of the CI matrix):

```bash
TENON_TEST_DBTYPE=MySql TENON_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/TenonAdmin.slnx
```

Frontend (run from the `web/` directory):

```bash
npm run dev          # Vite, :5173, proxies /api and /openapi to backend :5100 (override with TENON_API_TARGET)
npm run build         # vue-tsc --noEmit && vite build
npm run lint          # oxlint (lint:fix to autofix)
npm run typecheck     # vue-tsc --noEmit
npm run gen:api       # regenerate src/api/schema.d.ts from a running backend's /openapi/v1.json
```

If running both sides separately is a hassle, `dev.bat` at the repo root launches backend + frontend together in two separate windows (installing `web/` dependencies on first run); `stop.bat` stops them.

::: warning Don't hand-edit schema.d.ts
`web/src/api/schema.d.ts` is a contract file generated from the backend's OpenAPI. If you change an endpoint, run `npm run gen:api` first (requires the backend to be running) — don't hand-write this file.
:::

## Centralized package versioning

Backend dependency versions are all collected in [`backend/Directory.Packages.props`](https://github.com/Tenon-Net/TenonAdmin/blob/main/backend/Directory.Packages.props)'s `<PackageVersion>` — add or bump dependencies there, **not** by pinning a version in an individual `.csproj`. Shared build/NuGet metadata (author, repo URL, license, etc.) lives in `backend/Directory.Build.props`.

## Commit messages: English Conventional Commits

The repo's code comments and docs are in Chinese, but **git commits are always in English**, formatted as `type(scope): subject`:

```text
fix(web): hide permission-gated buttons for users without access
feat(backend): add targeted notification delivery
docs: translate comments in root config and script files
refactor(services): split login flow into virtual steps
```

Common `type` values: `feat` / `fix` / `docs` / `refactor` / `test` / `chore`. `scope` is usually `web` / `backend`, or a more specific module name.

## Running tests: both legs need to be green

CI (`backend-ci.yml`) runs build + test on push/PR touching `backend/**`, across a database matrix of `[sqlite, mysql, sqlserver, postgres]` (`fail-fast: false`, so one red leg doesn't hide the others), plus a Redis service container (for the contract-test portion of `RedisCacheTests`) and a `template-smoke` job (verifying `dotnet new tenon-app` can restore + build cleanly — the first command a consumer runs after getting the package). Before touching `backend/**`, at minimum get the default SQLite leg and the MySQL leg green locally — `TestDb.cs` derives an isolated database per test from env vars like `TENON_TEST_DBTYPE`, so tests don't interfere with each other.

Frontend CI (`web-ci.yml`) runs `npm ci` → `npm run lint` → `npm run build` on push/PR touching `web/**` (build already includes `vue-tsc` type checking, so there's no need to run `typecheck` separately).

::: tip The six-piece test suite is a contract, not an ordinary test
`ReplaceabilityTests` (the "six-piece set" from the design doc) locks in the replaceability guarantees around TryAdd coverage, virtual-method overriding, and business-assembly mounting. When you change DI registration or `TenonAdminSetup`-related code and this suite goes red, it usually means you've broken a consumer's replacement path — don't bypass or delete the tests; figure out which guarantee got broken first.
:::

## PR workflow

1. Branch off `dev` for your feature.
2. Keep each change focused on one thing; follow the commit conventions above.
3. Run the build/test/lint for the relevant side locally.
4. Open a PR targeting `dev`; CI (`backend-ci` / `web-ci`, depending on which side changed) must be fully green.
5. If you're using Claude Code or another AI agent to help develop, the repo has conventions for issue triage, domain docs, and business-development skills — see [Agent Skills and AI-Assisted Development](./agent-skills).

## Security issues

**Do not report security vulnerabilities through a public issue.** TenonAdmin distributes as a NuGet package with built-in auth, RBAC, and multi-org data permissions — a public report would disclose a 0-day to every downstream consumer before a patch exists.

Please use [GitHub private vulnerability reporting](https://github.com/Tenon-Net/TenonAdmin/security/advisories/new) instead. Maintainers will respond within **7 days** and coordinate the fix and disclosure timeline with you. See [SECURITY.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/SECURITY.md) for details.

## License

TenonAdmin is open-sourced under the [Apache License 2.0](https://github.com/Tenon-Net/TenonAdmin/blob/main/LICENSE); code you submit is contributed under the same license by default.
