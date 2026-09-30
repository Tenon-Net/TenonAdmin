# Replaceability Model

Every service is interface-backed, long methods are split into `virtual` steps, and built-ins use `TryAdd`, so an application can replace one piece without forking. Choose the path by the size of the change: replace a whole interface with an early DI registration; change one step by overriding a `virtual` method; own a built-in route by disabling that module before mounting a controller; add entities and endpoints through `ApplicationAssemblies`. These paths can be combined without editing kernel source.

## Constraint one: `TryAdd` registration, first registrant wins

Built-in services are registered exclusively with `TryAdd*`, never `Add*`. `TryAdd`'s semantics are "if the container already has a registration for this interface, don't add another" — so if a consumer registers the same interface **before** `AddTenonAdmin()`, their implementation wins and the built-in one is skipped. That only holds for single-implementation interfaces, though. `ICaptchaProvider` and `ISeedData` go through `TryAddEnumerable` instead, deduplicated by implementation type — the semantics there are "join the set," not "replace." A consumer's pre-registered slider-captcha provider ends up sitting alongside the three built-in ones rather than displacing them; which one actually gets used is decided separately, by `TenonAdmin:Security:Captcha:Type`.

`ServicesSetup` is full of this pattern:

```csharp
// backend/src/TenonAdmin.Services/ServicesSetup.cs
services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
services.TryAddScoped<IAuthService, AuthService>();
services.TryAddScoped<IPermissionProvider, RbacPermissionProvider>();
services.TryAddScoped<IDataScopeProvider, DataScopeProvider>();
services.TryAddScoped<IUserService, UserService>();
```

Same pattern in the data layer:

```csharp
// backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs
services.TryAddSingleton(sp => WorkerIdAssignment.Resolve(sp.GetService<AdminIdOptions>()));
services.TryAddSingleton<IIdGenerator>(sp =>
    new SnowflakeIdGenerator(sp.GetRequiredService<WorkerIdAssignment>().WorkerId, sp.GetService<TimeProvider>()));
services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(SqlSugarRepository<>)));
```

::: warning `TryAdd` depends on registration order
A consumer must register **before** `AddTenonAdmin()` to win. Register after, and the built-in service has already claimed the slot — `TryAdd` silently skips the consumer's registration. No error is raised; the replacement just silently doesn't take effect.
:::

The optional `TenonAdmin.Caching.Redis` package is the canonical example: it `TryAdd`s the Redis implementation of `ICacheProvider` before `AddTenonAdmin()` runs, taking precedence over the kernel's default in-process `MemoryCacheProvider`.

```csharp
// backend/samples/MinimalHost/Program.cs
builder.Services.AddTenonAdminRedisCache(builder.Configuration); // register first, wins the TryAdd
builder.Services.AddTenonAdmin(builder.Configuration);
```

## Constraint two: template methods split into `virtual` steps

Long service methods are broken into a series of `virtual` steps (the template-method pattern). When a consumer wants to change behavior, they subclass the built-in service and override **just one step**, rather than copying the entire method.

`AuthService.BuildLoginOutput` is one example: a subclass can change only login-output assembly while validation, lockout, token, and session steps continue through the base implementation. Authentication subclasses carry an additional security rule: their constructor must forward every capability dependency accepted by the current base class. External login, time, and TOTP dependencies are optional trailing parameters for source compatibility; omitting them still compiles but disables those steps. Use the current constructor and `ReplaceabilityTests.OverridingAuthService` as the source of truth instead of copying an old parameter list.

## Constraint three: business assembly mounting

A consumer's entities and controllers are mounted into the kernel via `options.ApplicationAssemblies`, extending it without modifying the kernel: entities join CodeFirst table creation, and controllers get `AddApplicationPart`-ed into the same MVC pipeline. See [Layered Architecture](./architecture.md#how-a-consumers-entities-and-controllers-plug-in) for details.

Combined with module disabling, a consumer can even **take over** the routes of a built-in module: after disabling the built-in `Dict` module, their own `CustomDictController` can claim the `/api/v1/sys/dict/*` route. The disable step isn't optional — skip it and both controllers end up registered on the same route, which throws an `AmbiguousMatchException` on the first request that hits it. Nothing catches this at startup; it only surfaces the moment a request lands there.

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyModule).Assembly);
    options.Api.DisabledModules = ["Dict"];   // skip this line and it's a route conflict, not a takeover
});
```

## The "six-piece set" locks these down as a contract

`backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs` verifies replaceability as a public contract. It currently covers nine paths, including service replacement, single-step overrides, external providers, module takeover, and consumer seed data:

| Test | What it locks down |
| --- | --- |
| `ReplaceService_ShouldUseUserImplementation` | Consumer `Replace`s `IPasswordHasher`; the container resolves the consumer's implementation |
| `ReplaceSmsSender_ShouldUseUserImplementation` | Consumer `Replace`s `ISmsSender`; the container resolves the consumer's implementation |
| `ReplaceEmailSender_ShouldUseUserImplementation` | Consumer `Replace`s `IEmailSender`; the container resolves the consumer's implementation |
| `ReplaceRealtimePublisher_ShouldUseUserImplementation` | Consumer `Replace`s `IRealtimePublisher`; the container resolves the consumer's implementation |
| `OverrideAuthStep_ShouldAffectLoginFlow` | Overriding one `virtual` step of `AuthService` changes the login flow's result |
| `ExternalAuthProvider_ShouldBePluggable` | A consumer's pre-registered external-login provider shows up in the resolved provider set (additive, doesn't displace the built-in ones) |
| `DisabledModule_ShouldRemoveBuiltInController` | A disabled module's built-in controller is removed (404); non-disabled ones remain |
| `CustomController_ShouldOwnSameRouteAfterModuleDisabled` | After disabling a built-in module, a consumer controller takes over the same route |
| `CustomSeedData_ShouldRunOnceAndBeIdempotent` | Consumer seed data inserts once on first startup and is idempotent on subsequent startups |

::: tip Check these before touching the kernel
These test cases are the executable version of a product promise. Before modifying `TryAdd` registrations, `virtual` splits, or the assembly-mounting path, confirm they're still green — if they go red, some replacement point has been silently broken.
:::

## Two things the kernel won't let you touch

The takeaway from the sections above is "almost anything can be swapped," but the portal's module management has two server-side gates that even direct calls to the admin API can't get around. First, distinguish the two senses of "module": `Api.DisabledModules` from constraint three is a startup-time switch that strips out built-in controllers so you can take over their routes; what's meant here is the application record (`SysModule`) in the multi-app portal, added, edited, and removed through runtime CRUD. The gates are drawn on the latter.

**The built-in system module can't be disabled.** It carries every built-in admin page (org, ops, logs, files), so disabling it cuts the whole portal off — and there's no UI path to bring it back, which amounts to locking yourself out. The frontend's disabled-state interception is only a hint, not a line of defense; the real gate is server-side, keyed on a fixed Id (so it doesn't fall over when the Code changes) and rejecting any attempt to set `Enabled=false`.

**A module with menus can't be deleted.** Delete a module that still has menus hanging off it and those menus' top-level directory `ModuleId` goes dangling, making the entire subtree vanish from the portal. Before deletion, the kernel checks whether any menu belongs to the module and refuses if so, forcing you to first move or delete the attached top-level directories before deleting the module.

```csharp
// backend/src/TenonAdmin.Services/Module/ModuleService.cs
// Disabling a built-in module: judged by fixed Id (42013 ModuleProtected, the same code shared with "not deletable")
AdminException.ThrowIf(id == DefaultModuleSeed.BUILTIN_MODULE_ID && !input.Enabled, ErrorCode.ModuleProtected);

// Deleting a module with menus: query menus through the Db escape hatch (42023 ModuleHasMenus)
AdminException.ThrowIf(
    await modules.Db.Queryable<SysMenu>().AnyAsync(m => m.ModuleId == id),
    ErrorCode.ModuleHasMenus);
```

The menu check uses the existing repository's `modules.Db` instead of adding `IRepository<SysMenu>` to the primary constructor, which would break source compatibility for consumer subclasses. `ModuleProtectionTests` verifies both protections.

## The full pattern for a consumer replacing a service

Take replacing the password-hashing algorithm as an example:

```csharp
// 1. Implement the kernel interface
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => /* your algorithm */;
    public bool Verify(string password, string hash) => /* your verification */;
}

// 2. Register before AddTenonAdmin() (order is what matters)
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

The kernel registers `IPasswordHasher` with `TryAddSingleton`, so with your registration already in the container, the built-in `Pbkdf2PasswordHasher` never gets added. To swap the snowflake ID generator for a database auto-increment or GUID v7, implement `IIdGenerator` and register it up front the same way; to change just one step of a service rather than the whole thing, subclass it and override that one `virtual` step.

After startup, resolve the target interface once and confirm that its runtime type is your implementation, then exercise one real business path. Reading the registration code alone is not enough: reversing the order still lets the application start while silently keeping the built-in implementation, and forgetting to disable a module only fails when a request finally hits the duplicate route.

The step-by-step moves and pitfalls for all four paths — wholesale replacement, overriding a single step, disable-and-take-over, and consumer seed data — are collected in [Replacing Built-in Services](/guide/replace-service); this page only explains why these replacement points hold up.
