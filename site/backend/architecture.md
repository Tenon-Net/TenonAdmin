# Layered Architecture and Package Dependencies

TenonAdmin is composed of eight NuGet packages; the five forming the core chain may only depend downward — upper layers can reference lower ones, never the reverse. Reverse that direction in any one layer and both replaceability and the dependency boundary collapse together. The other three all hang off `Core` as optional side-branches outside that chain.

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

Off to the side, all three optional packages depend only on `Core` — none of Core/SqlSugar/Services/AspNetCore reference any of them back:

```text
TenonAdmin.Caching.Redis   Optional: RedisCacheProvider (StackExchange.Redis-backed ICacheProvider), opt-in via
                            AddTenonAdminRedisCache(configuration) called *before* AddTenonAdmin().
TenonAdmin.Auth.WeCom      Optional: an IExternalLoginProvider implementation for WeCom QR-code login.
TenonAdmin.Auth.DingTalk   Optional: an IExternalLoginProvider implementation for DingTalk QR-code login.
   ↑
TenonAdmin.Core
```

Neither login satellite package carries a single third-party runtime dependency beyond Microsoft.* — the dependency red line holds for optional packages too, not just the core chain.

Responsibilities and dependency direction per layer:

| Package | Responsibility | Depends on | Third-party runtime dependency |
| --- | --- | --- | --- |
| `TenonAdmin.Core` | Contracts, Options, `Result<T>`, `ErrorCode`, `AdminException`, `IIdGenerator` | None | Microsoft.* only |
| `TenonAdmin.SqlSugar` | `SqlSugarScope` singleton, `IRepository<>`, `BaseEntity`/`DataEntity`, CodeFirst, seeding | Core | SqlSugarCore |
| `TenonAdmin.Services` | `Sys*` entities, service implementations, RBAC, data scope, event bus | SqlSugar, Core | SqlSugarCore |
| `TenonAdmin.AspNetCore` | JWT, authorization filters, built-in controllers, global filters, `AddTenonAdmin` | Services, SqlSugar, Core | Microsoft.AspNetCore.* |
| `TenonAdmin` (meta-package) | Aggregation entry point | AspNetCore | — |
| `TenonAdmin.Caching.Redis` (optional) | `RedisCacheProvider` — Redis-backed `ICacheProvider` | Core only | StackExchange.Redis |
| `TenonAdmin.Auth.WeCom` (optional) | `IExternalLoginProvider` for WeCom QR-code login | Core only | Microsoft.* only |
| `TenonAdmin.Auth.DingTalk` (optional) | `IExternalLoginProvider` for DingTalk QR-code login | Core only | Microsoft.* only |

`TenonAdmin.Caching.Redis` doesn't introduce a new mechanism — it's the kernel's `TryAdd` replaceability, applied to the cache provider. A consumer calls `AddTenonAdminRedisCache(configuration)` before `AddTenonAdmin()`, which `TryAddSingleton`s a `RedisCacheProvider` that wins the race and replaces the kernel's default in-process `MemoryCacheProvider`. Skip the call, or don't set `TenonAdmin:Cache:Provider=Redis`, and the kernel's in-process default keeps working unchanged.

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

`AddTenonAdmin` is the composition root: it binds configuration first, then calls down through each layer. It's the only thing consumers see.

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
2. **Validate the snowflake worker ID.** If Redis caching is chosen (implying multiple instances) but `TenonAdmin:Id:WorkerId` isn't set explicitly, startup throws immediately — turning a silent primary-key collision into a readable startup error. For why two instances sharing a `WorkerId` actually collide on the primary key, the snowflake ID's bit layout is spelled out in [Data Layer and Auditing](./data-layer.md).
3. **Current-user + data-scope context.** The HTTP-side implementations `HttpContextCurrentUser` and `HttpContextDataScopeContext` are `TryAdd`-registered here first, taking precedence over the `AsyncLocal`-based fallback in the SqlSugar layer.
4. **Call down into lower layers.** `AddTenonAdminSqlSugar(options.Database, entityAssemblies)` wires the data layer, `AddTenonAdminServices()` wires the domain services.
5. **Host integration.** JWT key resolution, authentication/authorization, MVC controllers + global filters, CORS, rate limiting, OpenAPI, health checks.

```csharp
// Inside TenonAdminSetup.AddTenonAdmin, wiring the data and domain layers below it
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddTenonAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()]);
services.AddTenonAdminServices();
```

Incidentally, each layer can be assembled independently: `AddTenonAdminSqlSugar` is a public entry point, callable on its own against a bare container (used by tests, and by consumers who only need the data layer). Because of this, it resolves optional dependencies with `GetService` rather than `GetRequiredService` internally — no logger factory means it silently doesn't log, rather than turning into a required dependency that prevents startup.

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
