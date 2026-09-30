# Multi-App Portal & Router Guards

The whole protected area is watched by a single always-mounted component, `Protected`. Unlike Vue's per-navigation `beforeEach`, it short-circuits at render time on login state, password-change state, and routes-ready state, while the dynamic routes re-match reactively as `menuTree` changes. Which app you land in after login follows an `enterInitial` ladder — remembered, sole, default — and the chooser appears only when none of them holds.

> Prerequisite: complete the [frontend tutorial](/frontend-react/getting-started) and keep its Position Preview menu.

## Reach the same page through three entry points

Open Position Preview from the sidebar, reload its copied URL, then sign out and paste that URL into the address bar. The first two should return to the same page. The third must show login first and restore the protected area only after the menu tree is ready. If the account has multiple applications, switch once and confirm that tabs and dynamic routes rebuild with the application.

Those entries expose ordinary rendering, cookie or token session restoration, and unauthenticated redirect behavior. The `enterInitial` ladder and `Protected` states below explain which layer owns each wait.

## Which app to enter after login: the enterInitial ladder

`enterInitial()` in `composables/useModule.ts` decides whether, after login or a hard refresh, you go straight into an app or get the chooser. It is written as a module-level `async` function, not a hook: both the guard and the chooser page call it, so it must not be bound to any one component's lifecycle.

Entering the portal starts by fetching the module list, the permission codes, and the profile in parallel.

```ts
const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
  personalApi.modules(),
  personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
  personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null })).catch(() => ({ sadm: false, avatar: null })),
])
```

The last two both fail closed. The moment the permission fetch fails, `permissionsLoaded` stays `false`, and `hasPerm` then treats every button as "no permission" and hides it, rather than letting it through while unsure. If the profile can't be fetched, the user is treated as ordinary, so nobody is mistaken for a super admin. None of this blocks entry into the portal. You still get in without permissions; it's just that every button except the super admin's is treated as "no permission" for now. The super admin is the exception: it takes the fail-open branch inside `hasPerm`, with the server's `sadm` claim as the final backstop.

With the data in hand, `enterInitial` walks a decision ladder, top to bottom, first match wins:

```ts
if (modules.length === 0) return { chooser: true }
const remembered = useAuthStore.getState().currentModuleId
if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
if (modules.length === 1) return enter(modules[0]!.id)
if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
return { chooser: true }
```

- **No app assigned at all** → chooser, with an "no app assigned" empty state on the chooser page.
- **A remembered app that is still in the list** → go straight in. `remembered` is `auth.currentModuleId`, the only persisted field in the auth store (`enter` writes it every time you enter an app). A hard refresh or deep link falls back to the last app precisely through this rung. But it must verify the app is still in the list: once an app's permission is revoked, the remembered id points at an app that no longer exists.
- **Exactly one app** → go straight in, no reason to prompt.
- **A default app configured** (`defaultModuleId`, set on the chooser page via `setDefault`) that is in the list → go straight in.
- **None of the above** → chooser.

`enter(moduleId)` does exactly one thing: fetch that app's menu tree and write it into the auth store.

```ts
export async function enter(moduleId: number): Promise<EnterResult> {
  const tree = await personalApi.menu(moduleId)
  useAuthStore.setState({ menuTree: tree, currentModuleId: moduleId, routesReady: true })
  return { chooser: false, moduleId }
}
```

It never touches a route table. The routes are derived reactively from `buildRoutes(menuTree)` through `useRoutes`, and the moment `menuTree` changes the current URL re-matches on its own. How the menu tree grows into routes lives in [Routing & Dynamic Menus](/frontend-react/routing).

`enterInitial` also wraps an in-flight dedup around all this. Under React StrictMode the guard's effect mounts twice (mount, unmount, remount), and several components may trigger it at once, so `enterInitial` gets called concurrently more than once. Caching the in-flight promise merges these calls into a single portal fetch, cleared once it settles, so the next hard refresh fetches again as normal.

```ts
let inflight: Promise<EnterResult> | null = null
export function enterInitial(): Promise<EnterResult> {
  inflight ??= doEnterInitial().finally(() => { inflight = null })
  return inflight
}
```

## The guard is a mounted component, not a navigation hook

Vue runs its guard on every navigation. React uses one `Protected` component over the protected area and handles four states during render; the first matching state decides what appears.

```tsx
if (!sessionChecked) return <Spin />
if (!loggedIn) return <Navigate to="/login" replace />
if (mustChange) {
  return location.pathname === '/personal/password' ? lazyEl(PasswordPage) : <Navigate to="/personal/password" replace />
}
if (!routesReady && !booted) return <Spin />   // enterInitial in flight, show a spinner
return <DynamicRoutes />
```

**Cookie session pending restoration** → show a spinner. After F5, the access token has disappeared from memory, so `tryRestoreCookieSession()` first uses the HttpOnly refresh cookie to obtain a new one. Login state is evaluated only after that attempt, preventing a valid cookie session from being sent to login on reload.

**Not logged in** → off to `/login`. `/login` and the OAuth callback are public static routes at the `App` top level; they never enter `Protected` at all.

**Forced password change** → the password page is locked in, and every other navigation bounces back to `/personal/password`. This check sits deliberately ahead of the route rebuild. The password page is a static route that renders without the menu tree; letting it through first avoids the "rebuild, choose app, bounced back to password page" loop.

**Routes not ready** → a spinner while `enterInitial` runs. Dynamic routes live only in memory and are not persisted. On a hard refresh or a directly opened deep link, `routesReady` is necessarily `false` and no `menu-{id}` route has been derived yet. The rebuild is triggered by an effect, not a navigation hook: once `Protected` mounts and finds `!routesReady && !booted`, it calls `enterInitial()` once to fill the menu tree back into the store.

`booted` records whether portal bootstrap has completed for this mount and cannot be replaced by `routesReady`. The latter becomes true only after entering an app and remains false in chooser state; using it alone would repeat chooser requests indefinitely. If `enterInitial` throws, the session is cleared and the next render falls back to `/login`.

After bootstrap, `DynamicRoutes` renders. It reads `menuTree` and `homePath` and lays the routes out through `useRoutes`:

```tsx
const routes = useMemo(() => [
  { path: '/module', element: <ModuleChooser /> },   // full screen, outside the layout shell
  {
    element: <LayoutShell />,
    children: [
      ...buildRoutes(menuTree),           // menu-derived dynamic routes
      // …five personal-center pages (static)…
      { path: '/', element: <Navigate to={home} replace /> },
      { path: '*', element: lazyEl(NotFoundPage) },
    ],
  },
], [menuTree, home])
return useRoutes(routes)
```

This reactive derivation is exactly why the React side needs no `return to.fullPath` re-resolve trick like Vue's. Vue's dynamic routes are mounted imperatively with `addRoute`, and once mounted the current URL still sits on the old match, so it must be re-resolved by hand. Here the routes are a plain array computed from `menuTree`; the moment `menuTree` changes React re-renders and `useRoutes` re-matches, with no "mounted but unmatched" gap.

The chooser state therefore needs no branch of its own. `enter` was never called, so `menuTree` stays empty, `buildRoutes` derives no dynamic routes, `homePath` falls all the way back to `/module`, and `/module` renders `ModuleChooser`. An empty `menuTree` plus the `homePath` fallback expresses "show the chooser" on its own. The `homePath` fallback order is: the module's own `defaultRoute` first, then the first leaf of the menu tree, then `/module`. An app with no menu configured has no home page to speak of, so sending the user back to the chooser beats letting them hit a path that doesn't belong to the app and get a 404.

`/module` is a static segment, and the shell also holds a catch-all `*`. Which one wins is decided by react-router on route specificity, independent of array order; the static `/module` always wins, and writing it first is only for readability.

## Switching apps and clearing tabs

The nine-square switcher in the top-right corner switches apps at any time, and it goes through `switchModule`, a different path from entering the portal at login.

```ts
export async function switchModule(moduleId: number): Promise<string> {
  await enter(moduleId)
  useTabsStore.getState().clearTabs()
  return homePath(useAuthStore.getState())
}
```

`switchModule` does three things: `enter()` rebuilds the target app's dynamic routes, `clearTabs()` removes tabs owned by the previous app, and the function returns the new app's `homePath`. The chooser page, which owns router context, performs the navigation. Tab synchronization then adds the new home page as the first tab.

Tabs are cleared only when switching apps (`switchModule`) and on logout or account switch (the auth store's `reset`); nowhere else. Tabs are written not in the guard but in `layouts/useTabSync.ts`: it watches the current pathname, and on each change looks up the menu or personal-page metadata to add a tab. `/module` is outside the shell and has no title source, and 404 is the same; neither creates a tab. This is also the React-side stand-in for Vue's `router.afterEach(addTab)`: Vue records tabs with a navigation hook, React with an effect subscribed to `location`.

## Verify the portal through four entry paths

An unauthenticated deep link must return to login; an account marked for password change may enter only the password page; an account with one app should enter it directly; and an account with several apps but no usable default should remain on the chooser. Reload a dynamic page as well: it should return to the same URL, while the StrictMode remount must not issue duplicate concurrent module, permission, and profile loads. After switching apps, old tabs should be gone and the new home page should become the first tab.

How button-level permissions use `hasPerm` and `<Can>` for gating lives in [Permissions & Button Gating](/frontend-react/permission).
