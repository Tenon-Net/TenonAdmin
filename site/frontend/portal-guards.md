# Multi-App Portal & Router Guards

A user may be authorized for several apps (modules) at once — so after login, should you drop them straight into an app, or show a chooser and let them pick? This page covers two interlocking things: how the portal makes that decision, and how the guard stitches the static shell, the dynamic routes, and the portal state together on every navigation.

## Which app to enter after login: enterInitial

TenonAdmin's shell is a multi-app portal: each user is authorized for some set of apps, and a nine-square chooser in the top-right switches between them at any time. What decides, after login or a hard refresh, whether to "go straight into an app" or "show the chooser" is `enterInitial()` in `composables/useModule.ts`:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin })).catch(() => ({ sadm: false })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

The module list, permission codes, and super-admin flag are fetched in parallel. The latter two fail closed: the moment `personalApi.permissions()` fails, `permissionsLoaded` stays `false`, and the `v-auth` directive treats that as "hide it" rather than granting access it can't be sure of; if `profile` can't be fetched, the user is treated as ordinary, so nobody gets mistaken for a super admin. This step doesn't block entry to the portal — you can still get in without permissions, every button except a super admin's is just treated as "no access" for now. A super admin goes through the separate fail-open branch in `hasPerm` keyed on `isSuperAdmin`, so their buttons still show even when the permission-code fetch failed — the server-side `sadm` claim backstops it either way.

Once that data is in, `enterInitial` runs an "which app to enter" ladder, top to bottom, first hit wins:

- **No apps assigned at all** → show the chooser, with an "no apps assigned" empty-state hint on the chooser page.
- **A remembered app that's still in your app list** → go straight into it. That "remembered app" is `auth.currentModuleId`, the *only* persisted field in the auth store (`buildRoutesForModule` writes it every time you enter an app). A hard refresh or a deep link landing back in the app you were last in is entirely down to this rung.
- **Exactly one app** → go straight in, no chooser needed.
- **A default app is configured** (`defaultModuleId`, settable on the chooser page via `setDefault`) and it's in the list → go straight in.
- **None of the above** → show the chooser.

Switching apps takes a different path. `switchModule(moduleId)` re-runs `enter()` (rebuilding that app's dynamic routes — internally it's the same `buildRoutesForModule(moduleId)` covered on the [Routing & Dynamic Menus](/frontend/routing) page), clears the tabs store (a new app means the tab bar should start from scratch), and replaces the current route with the new app's `homePath`. `homePath` is an auth-store getter: it prefers the module's own `defaultRoute`, falls back to the first leaf of the menu tree, and failing that lands back on `/module` — an app with no menus configured has no home page to speak of, and sending the user back to the chooser beats crashing them into a path that doesn't belong to this app with a 404.

## The guard: every navigation runs through beforeEach

`router/index.ts`'s `beforeEach` is the seam that stitches the static shell, the dynamic routes, and the portal state together:

```ts
router.beforeEach(async (to) => {
  const user = useUserStore()
  const auth = useAuthStore()

  if (to.name === 'login') return user.isLoggedIn ? { path: '/', replace: true } : true
  if (!user.isLoggedIn) return { path: '/login', replace: true }

  if (user.userInfo?.mustChangePassword) {
    return to.path === '/personal/password' ? true : { path: '/personal/password', replace: true }
  }

  if (!auth.routesReady) {
    try {
      const { useModule } = await import('@/composables/useModule')
      const res = await useModule().enterInitial()
      if (res.chooser) return to.name === 'module' ? true : { path: '/module', replace: true }
      if (to.name === 'module') return true
      if (to.path === '/') return { path: auth.homePath, replace: true }
      return to.fullPath
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  if (to.path === '/') return { path: auth.homePath, replace: true }
  return true
})
```

It handles four things, in order:

**Login redirect.** An already-logged-in user visiting `/login` is bounced back to `/`; an unauthenticated user going anywhere but `/login` is sent to `/login`. `/login` is the one auth-free page.

**Forced password change.** Once `mustChangePassword` is true (it comes back on the first login after an admin creates or resets an account), every navigation except `/personal/password` itself is intercepted and redirected there. This check is placed deliberately *before* the dynamic-route rebuild below: the password page is a static route and renders without the menu tree, so letting it through first avoids the "rebuild → pick app → bounced back to password again" loop. Once the password change succeeds, the existing flow forces a logout and re-login, and the backend clears the flag.

**The refresh / deep-link rebuild.** Dynamic routes live only in the router's in-memory route table — they aren't persisted. On a hard refresh or a directly-opened deep link, `auth.routesReady` is inevitably `false` and none of the `menu-{id}` routes are registered yet. The guard detects this and calls `enterInitial()` right here — which both rebuilds the routes and fills in `auth.modules`, so the portal's decision and the guard's rebuild share one and the same call. With the result in hand the guard then decides where to go: chooser result → `/module` (already headed to `/module` → let it through directly, because the data the chooser page needs is now ready and it must not be bounced back to `/`, or once a default app is set there'd be no way in to change it); target is `/` → resolve to `auth.homePath` directly; anything else → re-return `to.fullPath` so the same URL resolves again now that its route exists. If `enterInitial()` throws, the session is cleared and the user is sent back to `/login`, rather than left stranded on a half-built page.

Two spots here that don't return `to.fullPath` are worth noticing. When the target is `/`, returning `to.fullPath` would be a redirect to itself — and `/` no longer has a static `redirect`, so Vue Router would flag it as an infinite redirect. The other trap is subtler: this rebuild logic can't short-circuit on `to.meta.public`, because a not-yet-registered dynamic route first hits the catch-all (404), which carries the `public` flag — honor `public` and let it through, and the user sees a bogus 404 instead of the correct, rebuilt page.

**`/` always lands on `auth.homePath`.** On normal navigation with the routes already in place, visiting `/` is likewise handed to the guard to compute the home page. This check can't be written as a static `redirect` on the `layout` route, for the same reason as before — `redirect` is evaluated at resolve time, before this guard runs, when the menu tree (and therefore `homePath`) isn't ready yet, so any landing spot computed there is guaranteed wrong.

After navigation is confirmed, `afterEach` records visited pages as tabs, skipping three categories: anything marked `meta.public`, the three fixed names `login`/`module`/`not-found`, and any route not hung under `layout` (those don't belong to any app's workspace and shouldn't leave a trace in the tab bar):

```ts
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

How the dynamic routes actually grow out of the menu tree — how `buildRoutesForModule` turns each menu node's `component` string into a real lazy-loaded component, and how `namedPage` gives it a stable identity so `keep-alive` recognizes it — is the subject of [Routing & Dynamic Menus](/frontend/routing); this page is only about the portal's "which app to enter" decision, and how the guard pulls it in on every navigation.
