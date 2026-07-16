# Replaceability Model

Replaceability is the kernel's central concern: every service is interface-backed, long methods are split into `virtual` steps, and everything is registered with `TryAdd`, so a consumer can replace any single piece without forking. This is embodied in three constraints, locked down as a contract by the "six-piece set" of `ReplaceabilityTests`.

## Constraint one: `TryAdd` registration, first registrant wins

Built-in services are registered exclusively with `TryAdd*`, never `Add*`. `TryAdd`'s semantics are "if the container already has a registration for this interface, don't add another" — so if a consumer registers the same interface **before** `AddTenonAdmin()`, their implementation wins and the built-in one is skipped.

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
services.TryAddSingleton<IIdGenerator>(sp =>
    new SnowflakeIdGenerator(sp.GetService<AdminIdOptions>()?.WorkerId ?? 0, sp.GetService<TimeProvider>()));
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

Take `AuthService` as an example — "assembling the login output" is an independent `virtual` step in the login flow, which a consumer can override by subclassing:

```csharp
// User overrides the login-output assembly step (template-method override)
private sealed class OverridingAuthService(/* same constructor dependencies as the base class */)
    : AuthService(/* pass-through dependencies */)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = "OVERRIDDEN" };
}
```

The rest of the login flow (captcha validation, failure lockout, password verification, token issuance, session creation) runs entirely through the base class's original logic — only output assembly is replaced. Overriding one step versus copying the whole method matters: when the kernel is upgraded, the former doesn't miss an upstream fix because you copied an old method body.

## Constraint three: business assembly mounting

A consumer's entities and controllers are mounted into the kernel via `options.ApplicationAssemblies`, extending it without modifying the kernel: entities join CodeFirst table creation, and controllers get `AddApplicationPart`-ed into the same MVC pipeline. See [Layered Architecture](./architecture.md#how-a-consumers-entities-and-controllers-plug-in) for details.

Combined with module disabling, a consumer can even **take over** the routes of a built-in module: after disabling the built-in `Dict` module, their own `CustomDictController` can claim the `/api/v1/sys/dict/*` route.

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyModule).Assembly);
});
```

## The "six-piece set" locks these down as a contract

`backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs` is the regression lock for the replaceability mechanism — the test names are deliberately fixed by design, verifying the three constraints above as a contract, not as ordinary tests:

| Test | What it locks down |
| --- | --- |
| `ReplaceService_ShouldUseUserImplementation` | Consumer `Replace`s `IPasswordHasher`; the container resolves the consumer's implementation |
| `OverrideAuthStep_ShouldAffectLoginFlow` | Overriding one `virtual` step of `AuthService` changes the login flow's result |
| `DisabledModule_ShouldRemoveBuiltInController` | A disabled module's built-in controller is removed (404); non-disabled ones remain |
| `CustomController_ShouldOwnSameRouteAfterModuleDisabled` | After disabling a built-in module, a consumer controller takes over the same route |
| `CustomSeedData_ShouldRunOnceAndBeIdempotent` | Consumer seed data inserts once on first startup and is idempotent on subsequent startups |

::: tip Check these before touching the kernel
These test cases are the executable version of a product promise. Before modifying `TryAdd` registrations, `virtual` splits, or the assembly-mounting path, confirm they're still green — if they go red, some replacement point has been silently broken.
:::

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
