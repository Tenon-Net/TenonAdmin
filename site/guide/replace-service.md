# Replace Built-in Services

When default storage, sign-in behavior, or dictionary endpoints do not fit your business, register extensions in your own host project. Identify the scope of the change, then choose service replacement, a method override, or route takeover without copying the framework for a local requirement.

How big your change is decides which route you take:

- **Replace an entire service implementation** (say, PBKDF2 → argon2, or the in-process cache → Redis) → register your own implementation ahead of `AddTenonAdmin()`.
- **Change just one step in a flow** (log something extra after login, route credential checks through LDAP) → subclass the built-in service and override that one `virtual` method.
- **Drop a whole built-in module and take it over yourself** (the dictionary module's API doesn't fit at all) → disable its controller and claim the same route with your own controller.

Put registration code in your own `Program.cs` and extension classes in separate business-project files. Register initial data through seeds, as shown at the end.

Interfaces, overridable methods, and assembly discovery support these extensions. See [The Replaceability Model](/backend/replaceability) for the design constraints.

## Replace an entire service: register ahead of AddTenonAdmin

Every built-in service in the kernel is registered with `TryAdd*` — meaning "if the container already has this interface, don't add it." So all you do is register your own implementation *before* `AddTenonAdmin()`, and when the kernel's `TryAdd` sees the slot already taken, it steps aside automatically.

The password-hashing interface illustrates registration. Algorithm bodies are placeholders and are not runnable. A real replacement must handle existing password hashes and verify that old accounts can still sign in.

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
A replacement registered with `TryAdd*` after `AddTenonAdmin()` is skipped because the default is already present. The example uses `AddSingleton`, whose behavior differs from `TryAddSingleton`; not all late registrations are silently ignored. Prefer registering replacements before the kernel. For an explicit replacement afterward, use `Replace(ServiceDescriptor...)` with the original service lifetime.
:::

Common replacement points:

| Interface | Default implementation | When to swap |
|---|---|---|
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | For bcrypt / argon2 |
| `ICacheProvider` | `MemoryCacheProvider` | For Redis (the `TenonAdmin.Caching.Redis` package is exactly this pattern) |
| `IFileStorage` | `LocalFileStorage` | For OSS / S3 |
| `IAuthService` | `AuthService` | To customize the whole login flow |
| `IDataScopeProvider` | `DataScopeProvider` | To customize data-scope rules |
| `IIdGenerator` | `SnowflakeIdGenerator` | Customize ID generation within the existing `long` key contract |

Most replacement points are every `TryAdd` line in `backend/src/TenonAdmin.Services/ServicesSetup.cs`. The data layer and host layer each have their own batch too — `IIdGenerator`, for instance, is registered in `backend/src/TenonAdmin.SqlSugar/SqlSugarSetup.cs`. Every interface registered in any of these is a replacement point.

## Change just one step: subclass and override a virtual

Replacing the whole service means re-injecting all of its dependencies, and most of the time you don't want to change that much. The kernel splits its long methods into a handful of small `protected virtual` steps (the template method); you subclass and override only the step you want to change, and the rest runs the base class as-is.

`AuthService.LoginAsync` is the template (`backend/src/TenonAdmin.Services/Auth/AuthService.cs`): it just orchestrates a handful of `virtual` steps — failed-attempt lockout check → captcha → `ValidateUserAsync` credential check → disabled/locked policy → password expiry → `CheckSmsSecondFactorAsync` SMS second factor → token issuance → `OnLoginSucceededAsync` success hook → `BuildLoginOutput` output assembly. To wire in LDAP, override only `ValidateUserAsync`; to add a field to the login response, override only `BuildLoginOutput`; to make MFA mandatory even for phone-less users, override only `CheckSmsSecondFactorAsync` (the kernel default skips them — see [SMS verification](/backend/auth-security#sms-verification-second-factor-passwordless-sign-in)):

```csharp
// Change only the output-assembly step; the rest of the login logic (captcha/lockout/credential check/token issuance) runs the base class as-is
public sealed class MyAuthService(
    IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens,
    ISessionService sessions, ILogService logService, ILoginLockService loginLock,
    ICaptchaService captcha, ISecurityPolicyProvider policy, ISmsOtpService smsOtp,
    IEnumerable<IExternalAuthProvider>? externalProviders = null,
    ISysUserExternalService? externalBindings = null, IRbacService? rbac = null,
    TimeProvider? time = null, IMfaPolicyService? mfaPolicy = null,
    IMfaChallengeService? mfaChallenge = null, AdminSecurityOptions? security = null)
    : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp,
        externalProviders, externalBindings, rbac, time, mfaPolicy, mfaChallenge, security)
{
    protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
        base.BuildLoginOutput(user, pair) with { Name = $"{user.Name}({user.Account})" };
}

// Register with Replace, order-independent
builder.Services.AddTenonAdmin(builder.Configuration);
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthService, MyAuthService>());
```

Find the relevant `protected virtual` method in the target service. This example calls the base method before changing the display name, preserving the other result fields. Its constructor also forwards optional dependencies, including external-login and MFA services, to avoid accidentally bypassing existing security behavior.

Whether to call `base.Xxx()` depends on whether you are adding behavior or replacing the entire step. After upgrading, compare the base signature and verify sign-in, token refresh, and any enabled MFA flow.

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

Only controllers marked with `[Module("Name")]` can be disabled this way, including `Dict`, `Upload`, `Notice`, `Log`, `Config`, `Dashboard`, and `Job`. Check the selected version’s controller annotations. `Upload` maps to file routes under `/api/v1/sys/file`; configure the module name, not the route. Core authentication, user, organization, role, menu, and portal controllers do not have this switch.

Don't confuse `Api.DisabledModules` with the portal's "apps/modules" (the `SysModule` table): the former is a startup route switch, the latter is runtime data. The latter has its own guardrails — the built-in `system` app hosts all the admin pages, and disabling it through the management API is refused (error code 42013 — the portal would be cut off with no UI path to recover); an app that still has menus attached can't be deleted (42023, or those top-level directories' `ModuleId` would dangle and the whole subtree would vanish from the portal).

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

Allocate fixed business seed IDs from `TenonAdmin.Core.TenonSeedIds.ConsumerMin` (1000), below the runtime ID floor returned by `SnowflakeIdGenerator.CurrentFloor()`. Reserve `[1, 999]` for built-in seeds. The runtime cannot identify which project owns a seed, so developers must respect that reserved range.

Startup validation rejects `Id = 0`, IDs at or above the runtime floor, and duplicate IDs for the same entity. Do not reuse historical seed IDs, which may conflict with existing records during upgrades.

::: warning Forgetting to register means it silently never runs
The kernel doesn't scan assemblies for seeds (`options.ApplicationAssemblies` only handles entity table creation and controller mounting — it doesn't touch seeds). Seeds must be registered explicitly; miss this line and the seed doesn't run, with no error either.
:::

Verify replacements through normal application entry points: confirm the extension runs and that existing permissions and error handling still work. For route takeover, check OpenAPI for removal of the original controller and presence of your endpoints. Seeds should insert on first startup without duplicating rows after restart. `ReplaceabilityTests.cs` provides regression-test examples; see [Add a Business Module](/guide/business-module) for full integration.
