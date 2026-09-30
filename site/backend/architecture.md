# Layered Architecture and Package Dependencies

Most applications only need the `TenonAdmin` meta-package and one call to `AddTenonAdmin()`. The package layout matters when you want only the data layer, add an optional capability, or diagnose a dependency conflict. Five core packages depend downward. Redis, Excel, and four login packages depend only on `Core`, while the workflow package depends on `AspNetCore`. The meta-package does not pull in these optional capabilities.

## The core chain — five packages

```text
TenonAdmin.Core        Pure contracts: interfaces (I*Provider, I*Service), Options, Result<T>, ErrorCode, AdminException.
   ↑                   No SqlSugar, no ASP.NET.
TenonAdmin.SqlSugar    Data layer: ISqlSugarClient singleton (SqlSugarScope), IRepository<>, entity base classes,
   ↑                   CodeFirst DatabaseInitializer, seed runner.
TenonAdmin.Services    Domain layer: entities (Sys*), *Service implementations, RBAC / data-scope providers, event bus.
   ↑                   Entities are defined here, not in the SqlSugar layer.
TenonAdmin.AspNetCore  Host integration: AddTenonAdmin / MapTenonAdmin, JWT, [RolePermission] / [ActiveSession]
                       filters, built-in controllers, envelope / exception / operation-log filters.

TenonAdmin             Meta-package: references AspNetCore only. Consumers install this one package and transitively pull in the whole stack.
```

Six lightweight extension packages depend only on `Core`, and the core chain does not reference them back:

```text
TenonAdmin.Caching.Redis   Optional: RedisCacheProvider (StackExchange.Redis-backed ICacheProvider), opt-in via
                            AddTenonAdminRedisCache(configuration) called *before* AddTenonAdmin().
TenonAdmin.Auth.WeCom      Optional: an IExternalAuthProvider implementation for WeCom QR-code login.
TenonAdmin.Auth.DingTalk   Optional: an IExternalAuthProvider implementation for DingTalk QR-code login.
TenonAdmin.Auth.GitHub     Optional: an IExternalAuthProvider implementation for GitHub OAuth Apps.
TenonAdmin.Auth.WeChat     Optional: an IExternalAuthProvider implementation for WeChat Open Platform.
TenonAdmin.Excel           Optional: xlsx read/write and template generation with dropdowns, opt-in via
                            AddTenonAdminExcel() called *before* AddTenonAdmin().
   ↑
TenonAdmin.Core
```

All four login packages reference only `Core` and Microsoft.*; none brings in a vendor SDK. `TenonAdmin.Workflow` is a different kind of optional package. It references `TenonAdmin.AspNetCore` to reuse controllers and the request pipeline, and becomes active only after `AddTenonAdminWorkflow()` and `UseWorkflow()` are called.

Responsibilities and dependency direction per layer:

| Package | Responsibility | Depends on | Third-party runtime dependency |
| --- | --- | --- | --- |
| `TenonAdmin.Core` | Contracts, Options, `Result<T>`, `ErrorCode`, `AdminException`, `IIdGenerator` | None | Microsoft.* only |
| `TenonAdmin.SqlSugar` | `SqlSugarScope` singleton, `IRepository<>`, `BaseEntity`/`DataEntity`, CodeFirst, seeding | Core | SqlSugarCore |
| `TenonAdmin.Services` | `Sys*` entities, service implementations, RBAC, data scope, [event bus](/backend/event-bus) | SqlSugar, Core | SqlSugarCore |
| `TenonAdmin.AspNetCore` | JWT, authorization filters, built-in controllers, global filters, `AddTenonAdmin` | Services, SqlSugar, Core | Microsoft.AspNetCore.* |
| `TenonAdmin` (meta-package) | Aggregation entry point | AspNetCore | — |
| `TenonAdmin.Caching.Redis` (optional) | `RedisCacheProvider` — Redis-backed `ICacheProvider` | Core only | StackExchange.Redis |
| `TenonAdmin.Auth.WeCom` (optional) | `IExternalAuthProvider` for WeCom QR-code login | Core only | Microsoft.* only |
| `TenonAdmin.Auth.DingTalk` (optional) | `IExternalAuthProvider` for DingTalk QR-code login | Core only | Microsoft.* only |
| `TenonAdmin.Auth.GitHub` (optional) | `IExternalAuthProvider` for GitHub OAuth Apps | Core only | Microsoft.* only |
| `TenonAdmin.Auth.WeChat` (optional) | `IExternalAuthProvider` for WeChat Open Platform | Core only | Microsoft.* only |
| `TenonAdmin.Excel` (optional) | `IExcelReader`/`IExcelWriter`/`IExcelTemplateBuilder` for xlsx | Core only | MiniExcel, DocumentFormat.OpenXml |
| `TenonAdmin.Workflow` (optional) | Approval definitions, engine, tasks, and designer APIs | AspNetCore | Microsoft.AspNetCore.* |
| `TenonAdmin.Integration` (optional) | Application credentials, open APIs, outbound calls, and reliable delivery | AspNetCore | Microsoft.AspNetCore.* |

System integration uses the separate optional `TenonAdmin.Integration` package, which depends on `AspNetCore` and is not included by the meta-package. With a release containing this module, call `AddTenonAdminIntegration()` before `AddTenonAdmin`, and call `UseIntegration()` in the kernel options callback. Follow [System Integration](/guide/integration) for applications, credentials, and delivery tasks.

## Choose an entry point by use case

- **Build a complete admin application:** install `TenonAdmin`. It transitively brings in the core chain and is the path used by the quick start and business-module examples.
- **Use only data access:** install `TenonAdmin.SqlSugar` directly and call `AddTenonAdminSqlSugar()`. This does not register JWT, controllers, or host filters.
- **Add Redis, external login, or Excel:** keep the meta-package, install the matching optional package, and call its registration method. Registrations that use `TryAdd` to replace a built-in interface belong before `AddTenonAdmin()`.
- **Enable workflow:** install `TenonAdmin.Workflow`, register `AddTenonAdminWorkflow()`, and call `UseWorkflow()` in the `AddTenonAdmin` options callback. The meta-package does not enable it automatically.

The resulting service container is the quickest check: a complete host resolves built-in controllers; a data-only container resolves `ISqlSugarClient` and `IRepository<>` without authentication or MVC services.

`TenonAdmin.Caching.Redis` doesn't introduce a new mechanism — it's the kernel's `TryAdd` replaceability, applied to the cache provider. A consumer calls `AddTenonAdminRedisCache(configuration)` before `AddTenonAdmin()`, which `TryAddSingleton`s a `RedisCacheProvider` that wins the race and replaces the kernel's default in-process `MemoryCacheProvider`. Skip the call, or don't set `TenonAdmin:Cache:Provider=Redis`, and the kernel's in-process default keeps working unchanged.

`TenonAdmin.Excel` takes the same route. Without the optional package, all three default codecs throw `ErrorCode.ExcelProviderMissing` (`46001`). Install the package and call `AddTenonAdminExcel()` before `AddTenonAdmin()` to register the real implementations. Applications that do not use import/export do not carry its runtime dependencies. See [Wire Import/Export on Your Entity](/guide/import-export).

::: tip Entities live in Services, not in SqlSugar
The data layer only provides `IRepository<>` and entity base classes; the concrete `Sys*` business entities are defined in `TenonAdmin.Services`. This follows from the dependency direction: entities need to reference domain concepts, and the data layer cannot depend upward on the domain layer.
:::

::: warning Runtime dependency red line
The core packages' only third-party runtime dependencies are SqlSugarCore + Microsoft.*. Capabilities that are usually pulled from third-party libraries — logging, snowflake IDs (typically Serilog, Yitter.IdGenerator) — instead ship as single-file implementations inside the kernel (`FileLoggerProvider`, `SnowflakeIdGenerator`), precisely to hold this line.
:::

## One `*Setup.cs` per layer

Each layer's DI wiring is a static extension method, named to match the layer:

- `SqlSugarSetup.AddTenonAdminSqlSugar()` — `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs`
- `ServicesSetup.AddTenonAdminServices()` — `backend/src/TenonAdmin.Services/ServicesSetup.cs`
- `TenonAdminSetup.AddTenonAdmin()` — `backend/src/TenonAdmin.AspNetCore/TenonAdminSetup.cs`

`AddTenonAdmin` is the composition root for a complete host: it binds configuration first, then calls down through each layer. A normal business application uses this one entry point instead of calling every lower-level Setup separately.

```csharp
// backend/samples/MinimalHost/Program.cs — three lines, zero config, full stack
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

## How the composition root calls down through the layers

`AddTenonAdmin`'s assembly order (see `TenonAdminSetup.cs`):

1. **Bind configuration.** `configuration.GetSection("TenonAdmin").Bind(options)`, then run the optional `configure` callback to override, then register `TenonAdminOptions` and its sub-sections (`Database` / `Cache` / `Jwt` / `Security` / `Upload` / `Api` / `Id` / `Logging`) as singletons in the container. Everything defaults, so zero-config startup works.
2. **Snowflake worker ID.** Unset `TenonAdmin:Id:WorkerId` claims a file-lock slot on one machine and a free `sys_worker_lease` row on a shared database. Two instances configured with the same number: the second will not boot. Why a shared `WorkerId` collides on the primary key is in [Data Layer and Auditing](./data-layer.md).
3. **Current-user + data-scope context.** The HTTP-side implementations `HttpContextCurrentUser` and `HttpContextDataScopeContext` are `TryAdd`-registered here first, taking precedence over the `AsyncLocal`-based fallback in the SqlSugar layer.
4. **Call down into lower layers.** `AddTenonAdminSqlSugar(options.Database, entityAssemblies, options.AdditionalDatabases)` wires the data layer (main DB plus optional secondaries), `AddTenonAdminServices()` wires the domain services.
5. **Host integration.** JWT key resolution, authentication/authorization, MVC controllers + global filters, CORS, rate limiting, OpenAPI, health checks.

```csharp
// Inside TenonAdminSetup.AddTenonAdmin, wiring the data and domain layers below it
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddTenonAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);
services.AddTenonAdminServices();
```

Each layer can be assembled independently. A data-only application can call `AddTenonAdminSqlSugar` on its container without registering JWT, controllers, or other host features. The data layer therefore resolves optional dependencies with `GetService`; without a logger factory, it simply emits no logs instead of failing startup.

<a id="how-a-consumers-entities-and-controllers-plug-in"></a>

## How a consumer's entities and controllers plug in

A consumer's business assembly is registered via `options.ApplicationAssemblies` (set in code, not bound from configuration):

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyBusinessModule).Assembly);
});
```

Once registered, this assembly takes two paths through the composition root:

- **Entities join CodeFirst table creation.** The composition root merges the built-in Services assembly with consumer assemblies into the entity-scanning source passed to `AddTenonAdminSqlSugar`, so consumer entities get tables created by `DatabaseInitializer` alongside the built-in ones.
- **Controllers join the same MVC pipeline.** The composition root calls `mvc.AddApplicationPart(assembly)` for each consumer assembly, so consumer controllers go through the same filters (exception envelope, operation logging, bare-return wrapping) and the same authentication/authorization as built-in controllers.

```csharp
// Controllers: built-in + consumer, same MVC pipeline
var mvc = services.AddControllers(o => { /* global filters */ })
    .AddApplicationPart(typeof(TenonAdminSetup).Assembly);   // built-in controllers
foreach (var assembly in options.ApplicationAssemblies.Distinct())
    mvc.AddApplicationPart(assembly);                        // consumer controllers
```

::: warning Handle this path with care
When touching entity scanning or controller registration in `TenonAdminSetup`, make sure both of these mounting paths stay intact. Drop either one and consumer modules silently break: their tables don't get created, their controllers 404 — with no error raised.
:::

## The meta-package is just an aggregation entry point

`TenonAdmin.csproj` itself has no code — just a single `ProjectReference` pointing at `TenonAdmin.AspNetCore`. A consumer installing the meta-package alone transitively pulls in the whole stack: AspNetCore → Services → SqlSugar → Core. For finer-grained control (e.g. needing only the data layer), a consumer can install a lower-layer package directly.
