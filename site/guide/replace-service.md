# Replace Built-in Services

You want to change some behavior of the kernel — swap the password-hashing algorithm, slot a step into the login flow, replace the built-in dictionary module wholesale with your own. This page lays out which routes there are and how to take each one. No fork, no copying kernel code.

First ask yourself one question: how big is the change you're making?

- **Replace an entire service implementation** (say, PBKDF2 → argon2, or the in-process cache → Redis) → register your own implementation ahead of `AddTenonAdmin()`.
- **Change just one step in a flow** (log something extra after login, route credential checks through LDAP) → subclass the built-in service and override that one `virtual` method.
- **Drop a whole built-in module and take it over yourself** (the dictionary module's API doesn't fit at all) → disable its controller and claim the same route with your own controller.

And one related thing: seeding your own business tables with initial data, via consumer seeds. All four are below.

This page covers *how* to replace; why these routes work at all (the three constraints — `TryAdd` first-registration-wins, the `virtual` step split, and assembly mounting — plus the "six-piece" tests that lock them in) is in [The Replaceability Model](/backend/replaceability).

## Replace an entire service: register ahead of AddTenonAdmin

Every built-in service in the kernel is registered with `TryAdd*` — meaning "if the container already has this interface, don't add it." So all you do is register your own implementation *before* `AddTenonAdmin()`, and when the kernel's `TryAdd` sees the slot already taken, it steps aside automatically.

Take swapping the password-hashing algorithm:

```csharp
// Consumer Program.cs
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => /* your algorithm */;
    public bool Verify(string password, string hashedPassword) => /* your verification */;
}

// Register your own first — claim the interface
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
// Then call the kernel — TryAdd sees the existing registration and skips the built-in Pbkdf2PasswordHasher
builder.Services.AddTenonAdmin(builder.Configuration);
```

::: warning The wrong order fails silently
Put it *after* `AddTenonAdmin()` and the built-in implementation has already taken the slot, so your `TryAdd` is skipped — no error, but the replacement doesn't take. To be order-independent, use `builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>())`, which "overrides an existing registration" and wins even when written after `AddTenonAdmin()`.
:::

Common replacement points:

| Interface | Default implementation | When to swap |
|---|---|---|
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | For bcrypt / argon2 |
| `ICacheProvider` | `MemoryCacheProvider` | For Redis (the `TenonAdmin.Caching.Redis` package is exactly this pattern) |
| `IFileStorage` | `LocalFileStorage` | For OSS / S3 |
| `IAuthService` | `AuthService` | To customize the whole login flow |
| `IDataScopeProvider` | `DataScopeProvider` | To customize data-scope rules |
| `IIdGenerator` | `SnowflakeIdGenerator` | For DB auto-increment / GUID v7 |

The full list is every `TryAdd` line in `backend/src/TenonAdmin.Services/ServicesSetup.cs` — every interface registered there is a replacement point.

## Change just one step: subclass and override a virtual

Replacing the whole service means re-injecting all of its dependencies, and most of the time you don't want to change that much. The kernel splits its long methods into a handful of small `protected virtual` steps (the template method); you subclass and override only the step you want to change, and the rest runs the base class as-is.

`AuthService.LoginAsync` is the template (`backend/src/TenonAdmin.Services/Auth/AuthService.cs`): it just orchestrates a handful of `virtual` steps — failed-attempt lockout check → captcha → `ValidateUserAsync` credential check → disabled/locked policy → password expiry → `CheckSmsSecondFactorAsync` SMS second factor → token issuance → `OnLoginSucceededAsync` success hook → `BuildLoginOutput` output assembly. To wire in LDAP, override only `ValidateUserAsync`; to add a field to the login response, override only `BuildLoginOutput`; to make MFA mandatory even for phone-less users, override only `CheckSmsSecondFactorAsync` (the kernel default skips them — see [SMS verification](/backend/auth-security#sms-verification-second-factor-passwordless-sign-in)):

```csharp
// Change only the output-assembly step; the rest of the login logic (captcha/lockout/credential check/token issuance) runs the base class as-is
public sealed class MyAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens,
    ISessionService sessions, ILogService logService, ILoginLockService loginLock,
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = $"{user.Name}({user.Account})" };
}

// Register with Replace, order-independent
builder.Services.AddTenonAdmin(builder.Configuration);
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>());
```

To find overridable steps, open the target service's source and search `protected virtual` — those methods are the openings left for you. When overriding, call `base.Xxx()` first to keep the original logic, then append your own. The payoff of overriding one step instead of copying the whole block: on a kernel upgrade you automatically pick up upstream fixes to that base step, rather than missing them because you copied an old version of the method body.

## Drop a whole module: disable + take over the route

If a built-in module's controller doesn't fit at all, you can lift it out wholesale and claim the same route with your own controller. Disabling goes through `Api.DisabledModules`:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(Program).Assembly);   // mount your business assembly (see below)
    o.Api.DisabledModules = ["Dict"];   // can also go through config TenonAdmin:Api:DisabledModules
});
```

The disabled controller's routes are no longer registered, the original endpoints return 404, and your same-route controller can take over:

```csharp
[ApiController]
[Route("api/v1/sys/dict")]   // same route as the disabled built-in DictController
public class CustomDictController : ControllerBase { /* your dictionary logic */ }
```

Only controllers annotated with `[Module("Name")]` can be disabled, currently these six: `Dict`, `Upload` (literally what's disabled is the file controller `/api/v1/sys/file` — the module name isn't the route), `Notice`, `Log`, `Config`, `Dashboard`. The auth, user, org, role, menu, and portal controllers don't carry this annotation — there's no switch to turn them off, because turning them off would lock everyone out of the whole system.

Don't confuse `Api.DisabledModules` with the portal's "apps/modules" (the `SysModule` table): the former is a build-time route switch, the latter is runtime data. The latter has its own guardrails — the built-in `system` app hosts all the admin pages, and disabling it through the management API is refused (error code 42013 — the portal would be cut off with no UI path to recover); an app that still has menus attached can't be deleted (42023, or those top-level directories' `ModuleId` would dangle and the whole subtree would vanish from the portal).

## Seed your own entities: consumer seeds

Your business tables can also carry initial data that's inserted automatically on first startup and idempotent on repeat startups. Implement the generic `ISeedData<TEntity>` (the non-generic `ISeedData` is just an empty marker for DI collection — don't implement it directly):

```csharp
public class ProductSeed : ISeedData<BizProduct>
{
    public IEnumerable<BizProduct> HasData() =>
    [
        new() { Id = TenonSeedIds.ConsumerMin, Name = "Default product", Code = "default", Sort = 0, Enabled = true },
    ];
}

// Register in your own Program.cs
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, ProductSeed>());
```

A seed row's fixed Id must fall within the **consumer-reserved range `[1000, 4095]`** (constants in `TenonAdmin.Core.TenonSeedIds`: `ConsumerMin`=1000, `ConsumerMax`=4095). `[1, 999]` belongs to the kernel's built-in seeds, and `4096` upward is the snowflake runtime ID range — going out of range or colliding is rejected on the spot by the startup check (`DatabaseInitializer`), and the app won't start rather than swallowing it silently.

::: warning Forgetting to register means it silently never runs
The kernel doesn't scan assemblies for seeds (`options.ApplicationAssemblies` only handles entity table creation and controller mounting — it doesn't touch seeds). Seeds must be registered explicitly; miss this line and the seed doesn't run, with no error either.
:::

That `ApplicationAssemblies` line is the master switch for consumer wiring: it both joins your entities into CodeFirst table creation and brings your controllers into the same MVC pipeline — for the full chain, see [Add a Business Module](/guide/business-module). Before you actually start replacing anything, the five cases in `backend/tests/TenonAdmin.Tests/ReplaceabilityTests.cs` verify each of the four mechanisms above as a contract; following their shape to add a regression test around your own replacement is the safest bet.
