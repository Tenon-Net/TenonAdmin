# Multi-App Portal & Router Guards

Which app a user lands in after login is decided by a ladder, tried in order: the remembered app, the only app, the default app — and only when none of those holds does the chooser appear. A user may be authorized for several apps (modules) at once, so that landing point can't be hard-coded; it's computed for the current user.

> Prerequisite: complete the [frontend tutorial](/frontend/getting-started) and keep its Position Preview menu.

## Reach the same page through three entry points

Open Position Preview from the sidebar, reload its copied URL, then sign out and paste that URL into the address bar. The first two should return to the same page. The third must visit login first and restore the target only after the current application's menu is available. If the account has multiple applications, switch once and confirm that an application without this menu does not retain the old page tab.

Those entries exercise ordinary navigation, hard-reload reconstruction, and unauthenticated interception. `enterInitial` and `beforeEach` make all three converge on the authorized menu state described below.

## Which app to enter after login: enterInitial

TenonAdmin's shell is a multi-app portal: each user is authorized for some set of apps, and a nine-square chooser in the top-right switches between them at any time. What decides, after login or a hard refresh, whether to "go straight into an app" or "show the chooser" is `enterInitial()` in `composables/useModule.ts`:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi.profile()
      .then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null }))
      .catch(() => ({ sadm: useUserStore().userInfo?.isSuperAdmin ?? false, avatar: null })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  const user = useUserStore()
  if (user.userInfo) user.userInfo.avatar = profile.avatar
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

The module list, permission codes, and profile load in parallel. When `personalApi.permissions()` fails, `permissionsLoaded` remains false and `v-auth` hides ordinary permission-gated buttons. When the profile request fails, the store retains the super-admin snapshot supplied at login; with no snapshot it falls back to an ordinary account. Neither auxiliary failure blocks portal entry. The client uses trusted state it already has or a more conservative default, while the server-side `sadm` claim and permission filters remain the final authorization boundary.

Once that data is in, `enterInitial` runs an "which app to enter" ladder, top to bottom, first hit wins:

- **No apps assigned at all** → show the chooser, with an "no apps assigned" empty-state hint on the chooser page.
- **A remembered app that's still in your app list** → go straight into it. That "remembered app" is `auth.currentModuleId`, the *only* persisted field in the auth store (`buildRoutesForModule` writes it every time you enter an app). A hard refresh or a deep link landing back in the app you were last in is entirely down to this rung.
- **Exactly one app** → go straight in, no chooser needed.
- **A default app is configured** (`defaultModuleId`, settable on the chooser page via `setDefault`) and it's in the list → go straight in.
- **None of the above** → show the chooser.

Switching apps follows a separate path. `switchModule(moduleId)` calls `enter()` again, using the same `buildRoutesForModule(moduleId)` described in [Routing & Dynamic Menus](/frontend/routing), then clears the tab store and replaces the current route with the target app's `homePath`. That getter prefers the module's `defaultRoute`, falls back to the first menu leaf, and finally returns `/module`. An app with no menu has no valid home page, so returning to the chooser avoids navigating to a path owned by another app.

## The guard: every navigation runs through beforeEach

`router/index.ts`'s `beforeEach` is the seam that stitches the static shell, the dynamic routes, and the portal state together:

```ts
router.beforeEach(async (to) => {
  const user = useUserStore()
  const auth = useAuthStore()

  if (!user.accessToken && (user.cookieSession || user.refreshToken)) {
    const ok = await ensureAccessToken()
    if (!ok) user.clear()
  }

  if (to.name === 'login') return user.isLoggedIn ? { path: '/', replace: true } : true
  if (to.meta.public && to.name !== 'not-found') return true
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
      const resolved = router.resolve(to.fullPath)
      if (resolved.name && resolved.name !== 'not-found') {
        return { name: resolved.name, params: resolved.params, query: to.query, hash: to.hash, replace: true }
      }
      return { path: to.path, query: to.query, hash: to.hash, replace: true }
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  if (to.path === '/') return { path: auth.homePath, replace: true }
  return true
})
```

It handles five states, in order:

**Restore a cookie session.** In a Level 3 cookie session, the access token exists only in memory and disappears on F5. Before deciding that the user is logged out, the guard uses the persisted `cookieSession` marker and the HttpOnly refresh cookie to obtain a new access token silently. A body session has already rehydrated its token from storage, so this step completes immediately.

**Public routes and login redirects.** `/login`, OAuth callbacks, and MFA binding or recovery pages can load before dynamic routes are ready. A logged-in user visiting the ordinary login page returns to `/`; a login flow carrying `pendingLink` or `totpChallenge` clears stale session state and remains on login. The catch-all 404 is marked public but cannot short-circuit the guard, because an unresolved dynamic deep link temporarily matches it before route reconstruction.

**Forced password change.** Once `mustChangePassword` is true (it comes back on the first login after an admin creates or resets an account), every navigation except `/personal/password` itself is intercepted and redirected there. This check is placed deliberately *before* the dynamic-route rebuild below: the password page is a static route and renders without the menu tree, so letting it through first avoids the "rebuild → pick app → bounced back to password again" loop. Once the password change succeeds, the existing flow forces a logout and re-login, and the backend clears the flag.

**Rebuild on refresh or deep link.** Dynamic routes live only in router memory. On a hard refresh or direct deep link, `routesReady` is false, so the guard calls `enterInitial()` to reconstruct routes and module data. A chooser result goes to `/module`, while `/` goes to `homePath`. For an ordinary deep link, the guard runs `router.resolve(to.fullPath)` against the updated matcher and re-enters by the resolved route name; if no real dynamic route matches, it retries the original path. This prevents the navigation from retaining the catch-all record resolved before reconstruction. A failed rebuild clears login state and returns to login.

The root path cannot retry itself because Vue Router would flag an infinite redirect. The public-route test also cannot admit `not-found` unconditionally, since that match may be only the temporary catch-all seen before dynamic routes are restored.

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

## Verify the guard through four entry paths

After changing the guard, exercise four paths: an unauthenticated deep link must return to login; an account marked for password change may enter only the password page; an account with one app should enter it directly; and an account with several apps but no usable default should remain on the chooser. Finally, reload a dynamic page in the browser. It should return to the same address after rebuilding routes instead of stopping at 404. Together these checks cover authentication, forced password change, app selection, and deep-link restoration.

How the dynamic routes actually grow out of the menu tree — how `buildRoutesForModule` turns each menu node's `component` string into a real lazy-loaded component, and how `namedPage` gives it a stable identity so `keep-alive` recognizes it — is the subject of [Routing & Dynamic Menus](/frontend/routing); this page is only about the portal's "which app to enter" decision, and how the guard pulls it in on every navigation.
