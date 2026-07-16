# Authentication and Security

The kernel turns login, tokens, sessions, brute-force protection, and log redaction into default behavior — the services started by three lines of `Program.cs` already come with all of these wired in, no extra plumbing needed. This page walks through each default behavior along with its configuration keys and services. Most policies have both a deployment-time Options default and a runtime override via `SysConfig` (change configuration, not code).

## JWT tokens

The access token is a short-lived JWT that's never persisted; the refresh token is long-lived, stored server-side only as a hash, and supports rotation and revocation (`Core/Security/ITokenProvider.cs`). Configured under `TenonAdmin:Jwt` (`AdminJwtOptions`):

| Config key | Default | Description |
| --- | --- | --- |
| `TenonAdmin:Jwt:SecretKey` | `null` | Signing key, at least 32 bytes |
| `TenonAdmin:Jwt:Issuer` | `TenonAdmin` | Issuer (`iss` claim) |
| `TenonAdmin:Jwt:ExpireMinutes` | `120` | Access token lifetime (minutes) |
| `TenonAdmin:Jwt:RefreshExpireMinutes` | `10080` | Refresh token lifetime (minutes, 7 days) |

**The signing key resolves through three paths** (`JwtKeyResolver.cs`):

- **`SecretKey` is configured** (required for production): used directly; if shorter than 32 bytes, startup throws immediately and refuses to start — a weak key can be brute-forced to forge a super-admin token.
- **Not configured + production environment**: throws immediately and refuses to start (fail-fast). If production silently fell back to an auto-generated development key, multiple replicas would each sign with their own key, causing random 401s, and a leaked key would let anyone forge arbitrary tokens.
- **Not configured + development environment**: generates a 64-byte cryptographically random key and persists it to `{ContentRoot}/data/dev-jwt.key`, so the signing/validation key survives restarts and previously issued tokens stay valid — while printing a prominent warning. This file sits alongside the default SQLite database in the data directory, and is naturally covered by `.gitignore`.

::: warning Production requires a configured signing key
This is a security baseline, not a suggestion. Production will simply refuse to start without an explicitly configured `TenonAdmin:Jwt:SecretKey`.
:::

**Validation parameters** (`TenonAdminSetup.cs`): `MapInboundClaims = false` (preserves the original `sub`/`sid`/`sadm`/`org` claim names); `ValidateAudience = false` (single monolithic backend, audience unused); `ValidateLifetime = true`; `ClockSkew` tightened to 30 seconds (default is 5 minutes; tightened to match short-lived tokens); `NameClaimType = unique_name` (`User.Identity.Name` = login account).

**Access / refresh token lifetimes are runtime-configurable.** At issuance, the effective minutes come from `ISecurityPolicyProvider.GetSessionTtlAsync()`, which reads `SysConfig` first and falls back to the JWT defaults:

| Runtime config key | Fallback default |
| --- | --- |
| `sys.security.session.accessMinutes` | `Jwt:ExpireMinutes` |
| `sys.security.session.refreshMinutes` | `Jwt:RefreshExpireMinutes` |

## Captcha

Login captchas are handled by `CaptchaService` plus three built-in generators with zero drawing-library dependencies. Configured under `TenonAdmin:Security:Captcha` (`AdminCaptchaOptions`):

| Config key | Default | Description |
| --- | --- | --- |
| `...:Captcha:Enabled` | `false` | Whether login captcha is enabled |
| `...:Captcha:Type` | `char` | `char` (character SVG) / `path` (stroke-outline glyph) / `math` (arithmetic) |

**Off by default**: three-line zero-config API login works out of the box; account-level login lockout already blocks the primary brute-force vector, so captcha is an opt-in browser-side hardening measure — turn it on as needed for the Web template / production.

Overridable at runtime via `SysConfig` (changes take effect immediately): `sys.security.captcha.enabled` (whether validation is enforced), `sys.security.captcha.type` (which type is issued). Falls back to the Options default when unset.

**One-time tickets** (`CaptchaService.cs`): stored in cache in plaintext (2-minute TTL), issued with a GUID v7 as the ticket Id; validated with an **atomic get-and-remove** —

```csharp
// Concurrent requests carrying the same captchaId — only one gets a non-null value back,
// preventing a single captcha from being amplified into N guesses
var stored = await cache.GetAndRemoveAsync<string>(CacheKeys.Captcha(captchaId!));
AdminException.ThrowIf(stored is null, ErrorCode.CaptchaExpired);   // 40002
AdminException.ThrowIf(!string.Equals(stored, code, StringComparison.OrdinalIgnoreCase),
    ErrorCode.CaptchaWrong);   // 40003
```

The ticket is invalidated regardless of whether the check succeeds or fails — the same captcha can never be replayed or guessed multiple times. Image/slider/behavioral captchas can be swapped in ahead of time by self-registering `ICaptchaProvider`.

## Login lockout (brute-force protection)

`LoginLockService` is invoked at the **very front of login**, before credential validation — during a lockout, even the correct password is rejected. Configured under `TenonAdmin:Security:LoginLock` (`AdminLoginLockOptions`):

| Config key | Runtime key | Default | Description |
| --- | --- | --- | --- |
| `...:LoginLock:MaxFailCount` | `sys.security.loginLock.maxFailCount` | `5` | Consecutive password failures before lockout; `<=0` disables it |
| `...:LoginLock:LockMinutes` | `sys.security.loginLock.lockMinutes` | `10` | Lockout duration, also the sliding expiry window for the failure count |

The failure count is stored in cache, incremented atomically with its TTL refreshed each time: continued failures push the window out, while stopping lets the count expire after `LockMinutes` and unlock automatically. **Only "wrong password" counts toward lockout** — wrong captcha, already-locked, disabled account, etc. don't increment it, avoiding both an indefinitely extended lockout window and accidental collateral lockout (`AuthService.OnLoginFailedAsync`).

::: tip Account normalization must match the database
The lockout counter's key is normalized first (trim whitespace + lowercase); its equivalence class must be at least as coarse as the database's matching equivalence class. Otherwise case/trailing-space variants (which MySQL's `utf8mb4_0900_ai_ci` / PAD SPACE collation would treat as the same row) split into separate counters, letting an attacker bypass lockout and guess indefinitely.
:::

**Blocking account enumeration** (`AuthService.ValidateUserAsync`): "account doesn't exist" and "wrong password" both throw `ErrorCode.PasswordWrong` (indistinguishable in the response), and when the account doesn't exist, an equivalent-cost dummy hash still runs — making response timing indistinguishable too, closing both side channels at once.

## Sessions and force-logout

Sessions are managed by `SessionService` (design §15): sessions are persisted to the DB (source of truth) plus cached (hot path); the refresh token is stored only as a SHA-256 hash; all timestamps are UTC. At login, a GUID v7 is generated as the `sessionId`, written into the token's `sid` claim, and used as the stable anchor for listing online users and for force-logout.

**Force-logout takes effect immediately.** The authorization pipeline checks whether the session behind `sid` is still active on every request (see [Request Pipeline](./request-pipeline.md), step ②). When an admin kicks a user from "Online Users":

```csharp
public virtual async Task RevokeAsync(string sessionId)
{
    // mark the session row's RevokedAt
    // mark the refresh token Status = Revoked
    await cache.RemoveAsync(CacheKeys.Session(sessionId));   // cache removed → next check queries DB, finds it revoked → 401
}
```

The kicked user's access token, even if not yet expired, gets a 401 on the next request. Disabling/deleting a user goes through `RevokeAllForUserAsync`, taking down all of their sessions.

**Concurrency policy** (`TenonAdmin:Security:Session`, `AdminSessionOptions`):

| Config key | Default | Description |
| --- | --- | --- |
| `...:Session:Mode` | `Multi` | `Multi` (multiple devices coexist) / `Single` (new login kicks the old one) |
| `...:Session:MaxConcurrent` | `0` | Max concurrent sessions; when `>0`, exceeding it revokes the oldest login first; `0` means unlimited |

Quota trimming uses "**insert first, then converge**": the new session is inserted into the DB before trimming runs, so two concurrent logins both see each other's row and both compute the same "keep only the newest N" answer — convergence happens naturally, without relying on an in-process lock, which is what lets it work correctly across multiple replicas (a single-process lock wouldn't kick an old session on a different replica).

**Refresh-token reuse detection** (`SessionService.RefreshAsync`): a rotated token reappearing counts as a replay — the entire session is revoked (attacker and legitimate user are both logged out; safety takes priority). Rotation uses a conditional update (only sets `Used` if still `Active`), which doubles as concurrency protection.

## Password policy

`SecurityPolicyProvider.GetPasswordPolicyAsync()` reads each value from `SysConfig` first, falling back to defaults:

| Runtime config key | Default |
| --- | --- |
| `sys.security.password.minLength` | `8` |
| `sys.security.password.requireUpper` | `true` |
| `sys.security.password.requireLower` | `true` |
| `sys.security.password.requireDigit` | `true` |
| `sys.security.password.requireSpecial` | `false` |

If unmet, throws `ErrorCode.PasswordTooWeak`, with `args` carrying the specific requirements for the frontend to display. Passwords are hashed with PBKDF2 (`Pbkdf2PasswordHasher`).

**Default initial password** (`TenonAdmin:Security:DefaultInitialPassword`): defaults to `null` → when creating a user or resetting a password, a cryptographically random strong password is generated per account, closing off the known weakness of "a fixed default password shipped in a public NuGet package." A password reset returns the random password to the admin to relay on the spot.

::: tip Super-admin password on first startup
If `TenonAdmin:Seed:AdminPassword` is configured, that value is used; if not (the default), a random password is generated and **printed prominently to the startup log once** — only during the startup run that actually creates the account; subsequent startups don't print it again (printing an already-invalidated random password would only mislead).
:::

## Request rate limiting

Rate-limited by **client IP** using a fixed window, mounted via a built-in `IStartupFilter` that calls `UseRateLimiter` — no manual middleware wiring needed. Configured under `TenonAdmin:Security:RateLimit` (`AdminRateLimitOptions`):

| Config key | Runtime key | Default | Description |
| --- | --- | --- | --- |
| `...:RateLimit:Enabled` | `sys.security.rateLimit.enabled` | `true` | Deployment-time hard master switch; when `false`, no rate limiting regardless of DB config |
| `...:RateLimit:WindowSeconds` | `sys.security.rateLimit.windowSeconds` | `60` | Window length (seconds) |
| `...:RateLimit:PermitPerWindow` | `sys.security.rateLimit.permitPerWindow` | `300` | Global: requests per window per IP (blocks flooding) |
| `...:RateLimit:AuthPermitPerWindow` | `sys.security.rateLimit.authPermitPerWindow` | `20` | A stricter tier for auth endpoints (`/api/v1/auth/*`), blocking online brute-forcing |

`Enabled` is a deployment-time hard master switch; when `true`, the actual on/off state and thresholds are runtime-tunable via `SysConfig`.

::: warning Behind a reverse proxy, the IP seen is the proxy's
When deploying behind a proper gateway, wire up the `ForwardedHeaders` middleware to parse `X-Forwarded-For` first — otherwise all clients behind the same proxy share a single rate-limit partition.
:::

## Log redaction

Operation logs record all write operations by default (read operations and anonymous endpoints excluded). Input parameters are redacted before being written to the DB, preventing plaintext passwords from ending up in logs (`SensitiveDataMasker.cs`, invoked by `OperationLogFilter`):

```csharp
var paramJson = SensitiveDataMasker.Mask(context.ActionArguments);
```

**Redaction is by field name, not by value**: any property whose name contains `password` / `pwd` / `secret` / `token` / `credential` (case-insensitive, substring match — `newPassword`, `access_token` both match) has its value replaced with `***`, recursively across nested objects and arrays. Serialization failures (including non-serializable inputs like `IFormFile`) don't block the request — a placeholder string `<unserializable>` is logged instead.

Login logs (`AuthService`) record the raw input account (even when the account doesn't exist) plus the specific failure code, to support investigating brute-force attempts or account probing; IP/UA are filled in by the logging service from the current request. **Passwords are never logged, under any circumstance.**
