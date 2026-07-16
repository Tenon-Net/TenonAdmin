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

**Previous:** [Multi-App Portal](/frontend/routing/portal)
**Next:** [Keep-Alive & Named Pages](/frontend/routing/keep-alive)
