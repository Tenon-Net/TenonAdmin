# Project Structure & Startup

This page maps `backend/`'s solution layout, the six packages under `src/`, how the sample host boots, and how the test suite verifies both a bare kernel and a consumer with its own business module. For the *why* behind the architecture (dependency direction, replaceability, request pipeline), see [Architecture](/backend/architecture) — this page is about structure and startup, that one is the deep dive.

## Solution layout

`backend/TenonAdmin.slnx` groups every project into three solution folders:

| Folder | Contents |
| --- | --- |
| `samples/` | `MinimalHost` — the zero-config sample host used for local dev and manual verification |
| `src/` | The six shipped packages |
| `tests/` | `TenonAdmin.Tests` (the test suite) and `TenonAdmin.TestHost` (a minimal consumer host) |

`src/` holds the packages described in depth on the [Architecture](/backend/architecture) page — here's just enough to orient you:

| Package | One-line purpose |
| --- | --- |
| `TenonAdmin.Core` | Core contracts, zero runtime dependencies: `Result<T>`, `ErrorCode`, snowflake ID, security/extension-point interfaces |
| `TenonAdmin.SqlSugar` | Data layer: single SqlSugar instance, CodeFirst table creation, idempotent seeding, audit/soft-delete/data-scope global filters, generic repository |
| `TenonAdmin.Services` | Domain services: auth / RBAC / org / data scope / dict / config / logging / upload business services and entities |
| `TenonAdmin.AspNetCore` | Host integration: one-call `AddTenonAdmin`/`MapTenonAdmin` wiring, JWT auth, `[RolePermission]` authorization, built-in controllers and filters |
| `TenonAdmin` | Meta-package: installing this alone pulls in the whole kernel (AspNetCore + Services + SqlSugar + Core) |
| `TenonAdmin.Caching.Redis` | Optional: `StackExchange.Redis`-backed `ICacheProvider`, opt-in before `AddTenonAdmin()` |

## Central package versioning

`Directory.Packages.props` sets `ManagePackageVersionsCentrally=true` — every `.csproj` references a package by name only (`<PackageReference Include="..." />`), and this single file pins the version. A few pins carry an explicit CVE rationale in their comment rather than just tracking upstream:

- `SQLitePCLRaw.bundle_e_sqlite3` is explicitly bumped to `3.0.3` because the version Microsoft.Data.Sqlite transitively pulls (2.1.10/2.1.11) hits a SQLite CVE (NU1903 GHSA-2m69-gcr7-jv3q); 3.0.x carries the fix.
- `Microsoft.OpenApi` is explicitly bumped to `2.7.5` because the version `Microsoft.AspNetCore.OpenApi` 10.0.9 transitively pulls (2.0.0) hits a high-severity CVE (NU1903 GHSA-v5pm-xwqc-g5wc, affecting 2.0.0-preview.11 through 2.7.4); 2.7.5 is the first patched version.
- `Microsoft.Extensions.DependencyInjection.Abstractions` is bumped to `10.0.5` because `StackExchange.Redis` 3.0.11's transitive dependency on `Logging.Abstractions` 10.0.5 requires `DI.Abstractions` ≥10.0.5 — without the bump the centralized 10.0.0 pin conflicts and NuGet reports an NU1605 downgrade.

`Directory.Build.props` sets the shared build and package metadata for every project:

- `TargetFramework` is `net10.0`, with `Nullable` and `ImplicitUsings` both enabled.
- `GenerateDocumentationFile` is on and `CS1591` is suppressed via `NoWarn` — packages ship with XML doc comments (part of the package's value for a consumer stepping into kernel source), but public members aren't forced to have one to avoid a wall of warnings.
- NuGet metadata is centralized here too: `Version` (`0.1.1`, overridden at publish time via `-p:Version` from the release tag), `PackageLicenseExpression` (`Apache-2.0`), and `PackageTags` (`admin;rbac;sqlsugar;aspnetcore;scaffold;kernel`).
- SourceLink is wired via `PublishRepositoryUrl`/`EmbedUntrackedSources`/`IncludeSymbols` (`snupkg` format) so consumers can step into kernel source while debugging. `ContinuousIntegrationBuild` only turns on when `GITHUB_ACTIONS` is set — it normalizes embedded source paths, which would otherwise scramble local debugging.

::: tip `IsPackable` defaults to false
`Directory.Build.props` sets `IsPackable` to `false` by default; each package under `src/` opts back in explicitly. Sample and test projects inherit the default and are never packed.
:::

## Sample host

`backend/samples/MinimalHost/Program.cs` is the real bootstrap consumers copy from:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdminRedisCache(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

`AddTenonAdminRedisCache` is called here too, ahead of `AddTenonAdmin()` — but it's a no-op unless `TenonAdmin:Cache:Provider` is set to `Redis` in configuration, so the zero-config experience (SQLite, in-process cache) is unchanged by its presence. Drop that one line and you're at the three-line, truly zero-config baseline.

Its `appsettings.json` keeps file logging off by default (`TenonAdmin:Logging:File:Enabled: false` — diagnostics rely on stdout collection instead) and sets standard ASP.NET Core log levels. `appsettings.Development.json.example` is the template for the gitignored `appsettings.Development.json`; it holds just the seed super-admin account name and an empty password (left blank so the kernel prints a random one on first startup). `Properties/launchSettings.json` pins the dev URL to `http://localhost:5100` with `ASPNETCORE_ENVIRONMENT=Development`.

## Test infrastructure

`backend/tests/` has two distinct projects:

- **`TenonAdmin.TestHost`** is a minimal *consumer* host, not part of the automated test suite — it registers itself via `options.ApplicationAssemblies.Add(typeof(Program).Assembly)` so its own entities, seed data, and controllers (`SampleWidget`, `SampleDoc`, `CustomDictController`) exercise the same consumer-mounting path `WebApplicationFactory<Program>`-driven tests hit. See [Building a Business Module](/guide/business-module) for what it demonstrates.
- **`TenonAdmin.Tests`** is the actual xUnit suite, run via `dotnet test`.

The multi-database matrix lives in `TenonAdmin.Tests/TestDb.cs`. It reads `TENON_TEST_DBTYPE` (`MySql` / `SqlServer` / `PostgreSQL`; unset defaults to SQLite) plus a matching connection-string env var per engine (`TENON_TEST_MYSQL`, `TENON_TEST_SQLSERVER`, `TENON_TEST_POSTGRESQL`). For non-SQLite engines, each test run's database name is deterministically derived from an `identity` string via a SHA-256 hash (`tenon_it_` + first 16 hex chars) — the same identity always maps to the same database, which supports idempotent "start against the same database twice" test cases. Because SqlSugar's CodeFirst only creates tables, not databases, `TestDb` creates and drops the database itself through raw `MySqlConnection`/`SqlConnection`/`NpgsqlConnection` calls to the server before SqlSugar ever touches it.

## Config section overview

Everything binds from the `TenonAdmin` section of `appsettings.json` into `TenonAdminOptions` (`backend/src/TenonAdmin.Core/Options/TenonAdminOptions.cs`):

| Property | Sub-options type | Example default |
| --- | --- | --- |
| `Database` | `AdminDatabaseOptions` | `DbType = "Sqlite"`, `ConnectionString = "Data Source=./data/admin.db"`, `EnableCodeFirst = true` |
| `Cache` | `AdminCacheOptions` | `Provider = "Memory"`, `KeyPrefix = "tenon:"`, `PermissionMinutes = 20` |
| `Seed` | `AdminSeedOptions` | superadmin account/password seeding |
| `Jwt` | `AdminJwtOptions` | signing key/issuer/expiry |
| `Security` | `AdminSecurityOptions` | session concurrency policy |
| `Upload` | `AdminUploadOptions` | storage root, size cap, extension allowlist |
| `Api` | `AdminApiOptions` | disabled-module list |
| `DemoMode` | `bool` | `false` — when `true`, only GET/HEAD/OPTIONS are allowed, all writes rejected with error code `41002` |
| `Id` | `AdminIdOptions` | `WorkerId` — `null` by default (falls back to 0); must be set explicitly per instance when horizontally scaled |
| `Logging` | `AdminLoggingOptions` | file logging diagnostics, off by default |

`ApplicationAssemblies` is the one exception — a `List<Assembly>` set in code (as shown in the sample host and `TestHost` snippets above), not bound from configuration, since assembly references can't come from JSON.

With the structure mapped, the natural next step is to see how these packages assemble together — [Layered Architecture and Package Dependencies](/backend/architecture) starts from the dependency direction; the CLI commands for building and testing live in [Contributing](/community/contributing).
