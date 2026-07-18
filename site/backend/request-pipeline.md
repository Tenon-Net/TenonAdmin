# Request Pipeline

An admin kicks someone out of the Online Users list. That user's JWT hasn't expired, and their very next request comes back 401 anyway. The token has no idea it was revoked — the authorization gate re-checks the session on every request. The whole pipeline divides its labor this way: business code stays unaware, and the behavior is settled once by the kernel, at a fixed spot.

## Overview

```text
HTTP request
  │
  ├─①  Authentication   Microsoft JWT Bearer
  │          Claims are not remapped (sub / sid / sadm / unique_name)
  │          Framework 401 challenge → unified envelope (40006)
  │
  ├─②  [RolePermission]   Authorization filter
  │          Unauthenticated → 401; super admin sadm → pass through
  │          Validates session sid is still active (force-logout takes effect immediately)
  │          Permission code = {METHOD}:/{route template}, matched against the user's permission code set
  │
  ├─③  Data scope   Resolves the effective org set, writes it into IDataScopeContext
  │
  └─④  Result envelope   Bare return dto → Result<T>
             AdminException / ErrorCode → envelope (numeric code, never localized text)
```

## ① Authentication: Microsoft JWT Bearer

The kernel uses `Microsoft.AspNetCore.Authentication.JwtBearer` directly, without building its own auth stack. Wired up in `TenonAdminSetup.cs`:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SymmetricSecurityKey>((o, signingKey) =>
    {
        o.MapInboundClaims = false;   // keep original claim names, no legacy remapping
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Jwt.Issuer,
            IssuerSigningKey = signingKey,
            ValidateAudience = false,          // single monolithic backend, audience not used
            ValidateLifetime = true,           // validate exp / nbf
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
        };
        o.Events = new JwtBearerEvents { OnChallenge = /* see below */ };
    });
```

**Claims are not remapped.** `MapInboundClaims = false` turns off .NET's default legacy behavior of rewriting `sub` into a long XML-namespace URI, so claim names in the token are preserved as-is. The kernel's custom claim names are centralized in `TokenClaimNames` (`Core/Security/ITokenProvider.cs`):

| Claim | Constant | Meaning |
| --- | --- | --- |
| `sub` | `JwtRegisteredClaimNames.Sub` | User primary key |
| `sid` | `TokenClaimNames.SESSION_ID` | Session identifier (force-logout anchor) |
| `sadm` | `TokenClaimNames.SUPER_ADMIN` | Super-admin flag (value `"true"` bypasses authorization directly) |
| `org` | `TokenClaimNames.ORG_ID` | Owning org Id (data-scope anchor) |
| `unique_name` | `JwtRegisteredClaimNames.UniqueName` | Login account, mapped to `User.Identity.Name` |

**Framework 401s are reshaped into the unified envelope.** By default, when a token is missing or expired, JwtBearer returns an empty 401 whose body isn't in the kernel's envelope format. `OnChallenge` intercepts it and rewrites it into the same `Result<T>` shape used by business responses:

```csharp
OnChallenge = async ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
};
```

`ErrorCode.TokenInvalid` corresponds to numeric code **40006**. This way, whether the frontend hits "token expired" or "no permission," it receives an isomorphic envelope it can handle uniformly by code.

::: tip Deny by default
Controller endpoints require authentication by default via `MapControllers().RequireAuthorization()`, respecting `[AllowAnonymous]`. Anonymous endpoints like login and captcha are explicitly opened up; everything else passes through authentication first.
:::

## ② `[RolePermission]`: the permission code IS the route

Authorization is handled by `RolePermissionAttribute` (implementing `IAsyncAuthorizationFilter`). It **takes no parameters and no permission strings**. Magic strings like `"sys:user:add"` never appear in code. Authorization is granted by checking routes in the role-menu UI.

The filter runs in a fixed order internally:

```csharp
// 1. Must have passed JWT authentication
if (user.Identity?.IsAuthenticated != true)
    → 401 + 40006

// 2. Session-activity check: the session behind sid was revoked/expired → 401 (applies to super admins too)
var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
if (!await sessions.IsActiveAsync(sessionId))
    → 401 + 40006

// 3. Super admin passes through directly + unrestricted data scope
if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
{
    scopeContext.Current = DataScopeResult.Unrestricted;
    return;
}

// 4. Regular user: resolve data scope and write it into the context (see ③)
// 5. Permission code match
var code = PermissionCode.Build(method, routeTemplate);
if (!codes.Contains(code)) → 403 + 41001
```

**Permission code = normalized route.** `PermissionCode.Build` is the single source of truth:

```csharp
public static string Build(string httpMethod, string? routeTemplate) =>
    $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";
// e.g.: GET:/api/v1/ping
```

Using the **route template** rather than the actual path means parameterized routes (`user/{id}`) get a stable permission code that doesn't vary with the parameter value. The same `Build` function is shared across three call sites — authorization matching, `MenuController.Routes`' route listing (which feeds the permission-code dropdown on the menu form), and the default operation name for operation logging — preventing "the code computed at authorization time" from silently drifting one character out of sync with "the code stored on the menu" due to case or slash differences.

**Session-activity checks make force-logout take effect immediately.** Step 2 calls `ISessionService.IsActiveAsync(sid)` on every request. When an admin kicks a user from "Online Users," that session's cache entry is removed and the DB row is marked revoked — the kicked user's access token, even if not yet expired, gets a 401 on the very next request. Super admins aren't exempt either.

::: tip `[ActiveSession]`: endpoints for any logged-in user
Endpoints like personal center or logout — usable by any logged-in user without a specific permission code — carry `ActiveSessionAttribute` instead. It performs only steps 1 and 2 above (authentication + session-activity check), skipping the permission-code match. Using `[Authorize]` alone without it means an unexpired token still works even after the session was force-revoked — so any endpoint that needs force-logout to take effect immediately must carry `[ActiveSession]`.
:::

## ③ Data scope: resolved and injected into `IDataScopeContext`

During authorization (steps 3 and 4), the current user's **effective data scope** is resolved as a side effect and written into `IDataScopeContext`:

```csharp
// Super admin
scopeContext.Current = DataScopeResult.Unrestricted;

// Regular user (cache-backed)
var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
scopeContext.Current = await dataScopeProvider.ResolveAsync(userId, abort);
```

This is written during authorization (before the action runs) because DB queries inside the action need it. Once written, subsequent queries against `DataEntity` within the same request are automatically filtered by org set through SqlSugar's global query filter — business code never writes a single filter condition. See [Multi-Org Data Scope](./data-scope.md) for the full mechanism.

## ④ Result envelope: even bare returns get wrapped

At the response stage, the kernel uniformly wraps output into the `Result<T>` envelope and converts business errors into it too.

**Success: bare `return dto` gets auto-wrapped.** `ResultEnvelopeFilter` (an `IAsyncResultFilter`) lets business controllers `return dto;` directly and still get a unified envelope, without hand-writing `Result.Ok(...)` everywhere:

```csharp
public static bool TryWrap(IActionResult result, out ObjectResult wrapped)
{
    wrapped = null!;
    if (result is not ObjectResult obj) return false;        // File/StatusCode etc. left untouched
    if (obj.Value is IResultEnvelope) return false;          // already an envelope, pass through
    if (obj.StatusCode is int sc && (sc < 200 || sc >= 300)) return false; // non-2xx not wrapped
    wrapped = new ObjectResult(Result<object?>.Ok(obj.Value)) { StatusCode = obj.StatusCode };
    return true;
}
```

Only **successful (2xx) bare `ObjectResult`s** get wrapped; File, StatusCode, and error results are left untouched. Built-in controllers still explicitly return `Result<T>` (to keep the OpenAPI contract accurate) — this filter is a no-op for them.

::: warning Contract caveat
The filter wraps the envelope at result-execution time, which is invisible to ApiExplorer. For an endpoint that bare-returns `dto`, its OpenAPI 200 schema documents `dto` (no envelope shell), while at runtime it's actually `Result<dto>`. If such an endpoint's frontend types are generated via `npm run gen:api`, **explicitly declare the return type as `Result<T>`** (matching the built-in controllers), or the frontend will mistake `data` for the top-level dto.
:::

**Failure: `AdminException` → envelope.** Expected business failures (wrong credentials, wrong captcha, no permission…) throw `AdminException`, converted by `AdminExceptionFilter` into an HTTP 200 with a business-code envelope:

```csharp
public void OnException(ExceptionContext context)
{
    if (context.Exception is not AdminException ex) return;
    logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    context.Result = new ObjectResult(Result<object>.From(ex));
    context.ExceptionHandled = true;
}
```

Business failures are logged at **Information** level (not an error, doesn't trigger alerting). Other exceptions are not intercepted here — they fall through to the framework's default 500 handling, preserving the full stack trace, because a genuine program defect should fail loudly.

**Errors are numeric codes, never localized text.** The envelope carries `{ code, msgKey, args, message }`, where `code` is the numeric value of the `ErrorCode` enum. i18n happens on the frontend, keyed by code — the backend never returns Chinese/English error copy.

## Walking through one call

Take "delete a role" as an example:

1. **Authentication** — validates the JWT signature and expiry, reads out `sub` / `sid` / `sadm`.
2. **`[RolePermission]`** — session `sid` is still active; not a super admin; permission code `DELETE:/api/v1/sys/role/{id}` is present in the user's permission code set → pass through. The user's data scope is also written into `IDataScopeContext` at this point.
3. **Data scope** — the repository's write-path guard first queries through the scope-filtered path to confirm the target row is within scope; attempting to modify/delete a row from another org returns 0 rows and is rejected.
4. **Result envelope** — the controller's `return` result is wrapped into `Result<T>`; if an `AdminException` was thrown along the way, it's converted into a business-code envelope instead.
