# Contributing Guide

Start by choosing the affected product area and the correct target branch. Day-to-day PRs go to `dev`, then run the checks owned by the backend, Vue template, or React template. `main` only receives release merges. An OpenAPI change affects both frontend contracts even when the feature work starts in only one area.

## Before you start

- Fork the repo and clone it locally.
- **Development happens on the `dev` branch; `main` only accepts release merges** — target your PR at `dev`, not `main`. `dev` is merged into `main` and tagged only at release time (see [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)).
- File bugs / feature requests through one of the three GitHub Issue templates (Bug report / Feature request / Question) — the repo has blank issues disabled. **Do not** open a public issue for a security vulnerability; see "Security issues" below.

## Local development environment

TenonAdmin has three independently maintained parts: `backend/` is the .NET 10 kernel, `web/` is the Vue + Naive UI template, and `web-react/` is the React + Ant Design template. Both frontends consume the same API without sharing source code.

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

Vue frontend (run from `web/`, development port 5175):

```bash
npm run dev          # Vite, proxies /api and /openapi to backend :5100 (override with TENON_API_TARGET)
npm run build         # vue-tsc --noEmit && vite build
npm run lint          # oxlint (lint:fix to autofix)
npm run typecheck     # vue-tsc --noEmit
npm run gen:api       # regenerate src/api/schema.d.ts from a running backend's /openapi/v1.json
```

React frontend (run from `web-react/`, development port 5174):

```bash
npm run dev
npm run lint
npm test
npm run build       # tsc --noEmit && vite build
npm run gen:api
```

To start all three services together, run `./dev.sh` on macOS/Linux or `dev.bat` on Windows from the repository root. Stop them with `./stop.sh` or `stop.bat`. The scripts start the backend on 5100, React on 5174, and Vue on 5175, with separate logs under `.dev/`.

::: warning Don't hand-edit schema.d.ts
`web/src/api/schema.d.ts` and `web-react/src/api/schema.d.ts` are both generated from backend OpenAPI and must not be hand-edited. After an endpoint change, run `node scripts/check-contract-drift.mjs` from the repository root; it starts the host and regenerates both schemas.
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

<a id="running-tests-both-legs-need-to-be-green"></a>

## Run every affected check

CI (`backend-ci.yml`) runs build + test on push/PR touching `backend/**`, across a database matrix of `[sqlite, mysql, sqlserver, postgres]`. The matrix sets `fail-fast: false`, so one red leg never hides the others. Alongside it runs a Redis service container, covering the contract-test portion of `RedisCacheTests`, and a `template-smoke` job that verifies `dotnet new tenon-app` can restore + build cleanly — the first command a consumer runs after getting the package. Before touching `backend/**`, at minimum get the default SQLite leg and the MySQL leg green locally. `TestDb.cs` derives an isolated database per test from env vars like `TENON_TEST_DBTYPE`, so tests don't interfere with each other.

Vue CI (`web-ci.yml`) runs lint, Vitest, and build for changes under `web/**`. React CI (`web-react-ci.yml`) runs lint, sharded Vitest, build, and a development-server smoke check for `web-react/**`. The templates trigger independently; backend OpenAPI changes also run `contract-drift.yml` against both generated schemas.

`docker-smoke.yml` watches backend and Vue container paths. Its `single` job verifies empty-database setup, seeding, token issuance, and the reverse proxy. Its `multi` job uses two replicas to verify cross-replica logout, lockout and rate-limit counts, machine IDs, and the real client IP. Browser integration for both Vue and React is covered by `frontend-e2e.yml`.

::: tip The six-piece test suite is a contract, not an ordinary test
`ReplaceabilityTests` (the "six-piece set" from the design doc) locks in the replaceability guarantees around TryAdd coverage, virtual-method overriding, and business-assembly mounting. For the full, current list of exactly what the six-piece set guarantees, see [The Replaceability Model](/backend/replaceability). When you change DI registration or `TenonAdminSetup`-related code and this suite goes red, it usually means you've broken a consumer's replacement path — don't bypass or delete the tests; figure out which guarantee got broken first.
:::

## PR workflow

1. Branch off `dev` for your feature.
2. Keep each change focused on one thing; follow the commit conventions above.
3. Run the build/test/lint for the relevant side locally.
4. Open a PR targeting `dev`. Checks trigger by path, including `backend-ci`, `web-ci`, `web-react-ci`, `contract-drift`, `frontend-e2e`, and container smoke tests. Use the required checks that actually appear on the PR as the source of truth.
5. If you're using Claude Code or another AI agent to help develop, the repo has conventions for issue triage, domain docs, and business-development skills — see [Agent Skills and AI-Assisted Development](./agent-skills).

## Security issues

**Do not report security vulnerabilities through a public issue.** TenonAdmin distributes as a NuGet package with built-in auth, RBAC, and multi-org data permissions — a public report would disclose a 0-day to every downstream consumer before a patch exists.

Please use [GitHub private vulnerability reporting](https://github.com/Tenon-Net/TenonAdmin/security/advisories/new) instead. Maintainers will respond within 7 days and coordinate the fix and disclosure timeline with you. See [SECURITY.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/SECURITY.md) for details.

## License

TenonAdmin is open-sourced under the [Apache License 2.0](https://github.com/Tenon-Net/TenonAdmin/blob/main/LICENSE); code you submit is contributed under the same license by default.
