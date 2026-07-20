# Realtime Notifications

Realtime is off by default — `TenonAdmin:Realtime:Enabled` defaults to `false`, and with it off the system behaves exactly as it always has: the notice badge still polls every 30 seconds, and a force-logged-out session still waits for its next request to hit a 401. Turning it on only upgrades those two paths from eventually-consistent to instant. It's a UX enhancement layered on top of the existing contract, not a new one.

## Toggle & configuration

```jsonc
{
  "TenonAdmin": {
    "Realtime": {
      "Enabled": true,             // default false
      "HubPath": "/hub/realtime"   // default value — change it and the frontend has to change too, see below
    }
  }
}
```

While disabled, the kernel never calls `AddSignalR()`, never maps the hub, and never opens a long-lived connection — `IRealtimePublisher` resolves to `NoopRealtimePublisher` in the Services layer. Business code calls it exactly the same way; nothing just happens to occur.

SignalR ships as part of the ASP.NET Core shared framework — it's already there via `FrameworkReference`. So this feature **introduces no new NuGet dependency**. That's precisely why it lives in the kernel proper instead of being split into an optional package: it doesn't cross the "only SqlSugarCore + Microsoft.\*" runtime-dependency line.

## What gets pushed

The kernel pushes exactly two events, both **pure signals** carrying no payload:

| Event | Triggered by | What the frontend does on receipt |
| --- | --- | --- |
| `notice-changed` | `NoticeService` publishes a notice | Re-fetch the unread badge count |
| `force-logout` | `SessionService.RevokeAsync` | Clear the session, show a message, redirect to login |

Leaving the payload for the client to re-fetch is deliberate. The push channel's only job is to shout "something changed" — the actual content still comes from the same endpoints it always did. That way, if the push channel goes down, the worst case is falling back to polling, never a notice appearing on screen that nobody can actually look up.

The hub exposes **no client-callable methods at all** — it's push-only, one direction.

## Contract

```csharp
public interface IRealtimePublisher
{
    Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken ct = default);
    Task NotifyAllAsync(string @event, object? data = null, CancellationToken ct = default);
    Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken ct = default);
}
```

When a connection is established, `TenonHub.OnConnectedAsync` adds it to two groups based on its claims: `user-{sub}` and `session-{sid}`. The three methods push to all of one user's online connections, to everyone, and to one specific login session, respectively.

### Force-logout targets the session, not the user

This is the reason `NotifySessionAsync` exists as a separate method. What an admin kicks from the "Online Users" page is **one login session**, not a person — the same account's other session, still open on their phone, shouldn't be caught in the blast radius. So force-logout only pushes to `session-{sid}`.

The trigger point is consolidated in one place, `SessionService.RevokeAsync`. Manual force-logout, concurrency-limit eviction, refresh-token-reuse detection, and the `RevokeAllForUserAsync` path used when disabling or deleting a user all funnel through it — wire up that one spot and every case is covered.

## Authentication: token via query string

The hub carries `[Authorize]` and still runs on JwtBearer. But a browser's WebSocket handshake can't carry an `Authorization` header, so the token has to travel as a query parameter instead:

```
/hub/realtime?access_token=<token>
```

`OnMessageReceived` only honors that query parameter **scoped to the hub's own path**:

```csharp
var accessToken = ctx.Request.Query["access_token"];
if (!string.IsNullOrEmpty(accessToken) &&
    ctx.HttpContext.Request.Path.StartsWithSegments(options.Realtime.HubPath))
    ctx.Token = accessToken;
```

Ordinary API endpoints get no such relaxation — their token handling is untouched. A token that ends up in a URL can end up in a gateway's access logs, so this carve-out is locked down to exactly one path, keeping that cost confined to the hub alone.

## Multiple replicas: the built-in implementation is in-process

`SignalRRealtimePublisher` pushes through `IHubContext<TenonHub>`, which only reaches connections on the **same replica**. With two replicas running, a notice published from replica A never reaches a user connected to replica B.

The kernel doesn't ship a backplane for this — that would mean pulling in Redis, and Redis here is an optional package. The degraded path is already there by design: notices still fall back to the 30-second poll and stay eventually consistent; a force-logout that doesn't cross replicas falls back to the lazy 401 on next request. For instant delivery across replicas, a consumer layers it onto SignalR themselves:

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis("localhost:6379");
```

## Replacing it

The real implementation is registered with `TryAddSingleton` **before** `AddTenonAdminServices()` runs, so it wins over the Services layer's Noop. A consumer who wants their own `IRealtimePublisher` — pushing to an MQ instead of SignalR, say — registers it the same way, before `AddTenonAdmin()`, first registration wins:

```csharp
builder.Services.AddSingleton<IRealtimePublisher, MyMqPublisher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

This replacement path is locked in by the ["six-piece set" tests](/backend/replaceability).

## Frontend

`web/src/composables/useRealtime.ts` is the client — it `start()`s when the authenticated shell (`default.vue`) mounts and `stop()`s on unmount. The `@microsoft/signalr` version is pinned to match .NET 10.

::: warning Changing HubPath doesn't change the frontend automatically
The client's path is **hardcoded** as `` `${baseUrl}/hub/realtime` ``. Change `TenonAdmin:Realtime:HubPath` on the backend to something else, and the frontend simply can't connect — silently. It reads this as "realtime isn't enabled" and falls back to polling without raising any error. Changing the path means changing it on both sides.
:::

A failed initial connection is **silent by design**. When the backend has realtime disabled, the hub path returns 404. The client doesn't retry and doesn't surface anything — it just falls back to `NoticeBell`'s 30-second poll and the lazy 401-on-next-request logout. So when the frontend and backend disagree about whether realtime is on, what you see is degraded functionality, not an error.

In dev, Vite needs to proxy `/hub` with `ws: true` — that's already set up in `web/vite.config.ts`.

## What isn't verified

The test project deliberately doesn't pull in `Microsoft.AspNetCore.SignalR.Client` to drive a real `HubConnection`. The unit tests lock down the wiring: default resolves to Noop, `RevokeAsync` and `PublishAsync` trigger a push, the hub returns 401 (not 404) once enabled and 404 once disabled, and the six-piece replaceability contract holds.

The full push path has been smoke-tested with a one-off Node script talking directly to `MinimalHost`, but that isn't part of CI. Go in knowing the split: the wiring has tests behind it, the transport layer doesn't.
