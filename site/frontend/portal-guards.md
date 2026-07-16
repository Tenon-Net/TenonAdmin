# Multi-App Portal

TenonAdmin's shell is a multi-app portal — a user can belong to several apps (modules) and switch between them from a chooser. `useModule().enterInitial()` (in `useModule.ts`) is what decides, right after login or on a hard refresh, whether to drop the user straight into an app or show the chooser:

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] })),
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

Modules, permission codes, and the profile flag are fetched in parallel. Permission codes and the super-admin flag fail closed on error — a failed `personalApi.permissions()` call leaves `permissionsLoaded = false`, and the `v-auth` directive treats that as "hide everything" rather than granting access it can't confirm.

The entry decision, in order:

1. **No modules assigned** → chooser (with an empty-state hint).
2. **A remembered app** (`auth.currentModuleId`, the *only* field persisted from the auth store) that's still in the user's module list → re-enter it. This is what makes a hard refresh or a deep link land back in the same app instead of bouncing to the chooser.
3. **Exactly one module** → auto-enter it, no chooser needed.
4. **A default module is configured** (`defaultModuleId`, settable via `setDefault`) → auto-enter it.
5. **Otherwise** → show the chooser.

`switchModule(moduleId)` re-runs `enter()` for the new app, clears the tabs store (a fresh app means a fresh tab bar), and replaces the current route with the new app's `homePath`.



---

<!-- TODO(rewrite): merged from guards.md -->

# Router Guards

`router/index.ts`'s `beforeEach` ties the static and dynamic sides together:

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

- **Login redirect** — `/login` bounces an already-logged-in user back to `/`; an unauthenticated user going anywhere else is sent to `/login`.
- **Forced password change** — if `mustChangePassword` is set, every navigation except `/personal/password` itself is redirected there. This check runs *before* the dynamic-route rebuild below, because the password page is a static route and doesn't need the menu tree to render.
- **The refresh / deep-link rebuild.** Dynamic routes only exist in the router's in-memory route table — they aren't persisted. A hard refresh or a fresh deep link always starts with `auth.routesReady === false` and none of the `menu-{id}` routes registered. The guard detects this, calls `enterInitial()` to rebuild them, and then re-decides the outcome: chooser result → `/module`; already headed to `/module` → let it through; target was `/` → resolve to `auth.homePath` directly (returning `to.fullPath` here would be a self-redirect to `/`, which Vue Router treats as an infinite redirect since `/` no longer has a static `redirect`); anything else → re-return `to.fullPath` so the *same* URL resolves again, now that its route exists. If `enterInitial()` throws, the session is cleared and the user is sent back to `/login` rather than left on a half-built page.
- **`/` always resolves to `auth.homePath`.** This can't live as a static `redirect` on the `layout` route for the same reason noted earlier — `redirect` evaluates before this guard runs, before the menu tree (and therefore `homePath`) is known.

`afterEach` records visited pages as tabs, skipping anything marked `meta.public`, the fixed `login`/`module`/`not-found` names, and anything not matched under `layout`:

```ts
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

