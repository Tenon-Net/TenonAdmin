# External Login (SSO)

The first time someone scans in through WeCom, they get rejected with `OAuthAccountNotBound`. That's the default policy, not a misconfiguration. The kernel's standing position is that accounts are provisioned by an admin, never self-registered — and external identity follows the same rule. To let SSO provision accounts on its own, the switch has to be turned on explicitly, per provider.

## Three providers, two packaging models

| Provider | Where it lives | How it connects |
| --- | --- | --- |
| `oidc` | Built into the kernel (AspNetCore layer) | Standard OIDC — works with Keycloak, Entra, Authing, Auth0, and anything else that speaks the protocol |
| `wecom` | Optional package `TenonAdmin.Auth.WeCom` | WeCom desktop QR / web authorization |
| `dingtalk` | Optional package `TenonAdmin.Auth.DingTalk` | DingTalk desktop QR / web authorization |

Built-in OIDC adds zero new dependencies: discovery document, JWKS, and `id_token` signature verification all ride on the `Microsoft.IdentityModel.*` stack that JwtBearer already pulls in. Each vendor package depends only on `Core` plus Microsoft.\*, talking to the vendor's API over a bare `HttpClient`. That's what lets them ship on their own release cadence without dragging a vendor SDK into the kernel.

The two optional packages follow the usual rule — register before `AddTenonAdmin()`. They coexist with the built-in provider, keyed by `Code`:

```csharp
builder.Services.AddTenonAdminWeComAuth(builder.Configuration);
builder.Services.AddTenonAdminDingTalkAuth(builder.Configuration);
builder.Services.AddTenonAdmin(builder.Configuration);
```

## Configuration lives in two places

Secrets and operational settings are deliberately kept apart.

**Connection details and secrets go in `appsettings`**, the same pattern as Database, Jwt, and Email — secrets never land in the database:

```jsonc
{
  "TenonAdmin": {
    "ExternalAuth": {
      "Oidc": [ { "Code": "keycloak", "Authority": "...", "ClientId": "...", "ClientSecret": "..." } ]
    }
  }
}
```

`GET /api/v1/auth/external/providers` only returns the non-secret fields (code, display name, icon) — just enough for the frontend to light up a button.

**Operational settings go through `sys_config`**, changeable at runtime from the config page, keyed by provider code:

| Config key | Default | Controls |
| --- | --- | --- |
| `sys.externalauth.{code}.enabled` | Enabled | Whether this provider is turned on |
| `sys.externalauth.{code}.provisioning` | Deny | Whether an unbound identity's first login provisions a new account |
| `sys.externalauth.{code}.defaultRoleIds` | Empty | Roles granted on auto-provisioning |
| `sys.externalauth.{code}.defaultOrgId` | Empty | Org assigned on auto-provisioning |

Leave every key unset and the default behavior is **enabled + deny provisioning**. Touch only `appsettings` and you already have a working, binding-first SSO setup.

Reading these keys is consolidated in `ISysUserExternalService` — both the controller and `AuthService` call through it rather than each reading config keys on their own.

::: tip No provider-admin page, on purpose
The backend doesn't create a provider table or an admin page for it. Vendor secrets are deployment infrastructure at heart — storing them in the database would mean encryption at rest, masking, and a whole CRUD surface for a marginal gain that doesn't justify the added attack surface or the work. If a standalone provider-admin page is ever genuinely needed, the frontend can layer one on without any backend changes.
:::

## What happens to an unbound identity

The `sys_user_external` table is unique on `(Provider, Subject)` — it records which external identity maps to which local user. When a first external login finds no binding, the `provisioning` switch decides what happens next:

- **Deny** (default): throws `OAuthAccountNotBound` (40016). The user needs a local account first, then binds the external identity from their personal profile page.
- **Auto-provision (JIT)**: creates a local account with a random placeholder password and no forced password change, assigning the role and org from the two config keys above.

This resolution step is `virtual`. A policy like "auto-link by email to an existing account" just means overriding it — no kernel changes needed.

## Endpoints

All mounted under `api/v1/auth/external`:

| Endpoint | Purpose |
| --- | --- |
| `GET providers` | List available providers, for the frontend to render login buttons |
| `GET {provider}/authorize` | Get the redirect URL, carrying a one-time state |
| `GET {provider}/callback` | Where the vendor calls back |
| `POST exchange` | Exchange a one-time ticket for a token |
| `GET bindings` | The current user's bound external identities |
| `POST {provider}/bind` | Bind an external identity |

Both `state` and the one-time ticket reuse the same pattern as SMS verification codes: stored in the cache, consumed via an atomic `GetAndRemoveAsync`, valid exactly once.

Once external login resolves a `SysUser`, it hands off to `AuthService.CreateTokenAsync` — the same tail end used by password login and SMS login: session creation, token issuance, all of it shared. So session concurrency policy, force-logout, and refresh-token rotation apply to it identically.

## Callback and token-exchange example

`authorize` and `callback` are both full-page browser redirects, not JSON endpoints a frontend can `fetch`. `authorize` sends a 302 to the IdP's authorization page; once the IdP has authenticated the user, it redirects back to `callback`, which 302s again to the frontend's result page (`FrontendResultPath`, `/oauth/callback` by default) with whatever the next step needs, in the query string:

```
Success: GET /oauth/callback?ticket=<one-time ticket>
Failure: GET /oauth/callback?error=40015
```

The frontend reads `ticket` off that result page and exchanges it for a token — this is the step that's actually a `fetch`-able endpoint:

```bash
curl -X POST http://localhost:5100/api/v1/auth/external/exchange \
  -H "Content-Type: application/json" \
  -d '{"ticket":"<ticket from the callback redirect>"}'
```

The response envelope has the same shape as [password login](/guide/getting-started):

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

The ticket is single-use — `exchange` consumes it via an atomic `GetAndRemoveAsync`, and a second exchange attempt gets `OAuthStateInvalid` (40014).

## Error codes

| Code | Name | When |
| --- | --- | --- |
| 40013 | `OAuthProviderDisabled` | This provider has been switched off at runtime |
| 40014 | `OAuthStateInvalid` | The state doesn't match, or has already been consumed |
| 40015 | `OAuthExchangeFailed` | The token exchange with the vendor failed |
| 40016 | `OAuthAccountNotBound` | No binding exists, and this provider doesn't allow auto-provisioning |
| 40017 | `OAuthAlreadyBound` | This external identity is already bound to a different account |

Per the [frontend contract](/frontend/api-contract) convention, each of these codes needs a matching `msgKey` string configured in both language packs. Miss one, and the backend's consistency test turns red.
