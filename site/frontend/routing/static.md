# Static Routes

`router/routes.ts` defines exactly one top-level tree:

```ts
export const staticRoutes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: () => import('@/views/login/index.vue'), meta: { public: true } },
  { path: '/module', name: 'module', component: () => import('@/views/module/index.vue'), meta: { title: '选择应用' } },
  {
    path: '/',
    name: 'layout',
    component: () => import('@/layouts/default.vue'),
    children: [
      { path: '/personal/profile', name: 'personal-profile', component: namedPage('personal-profile', () => import('@/views/personal/profile.vue')) },
      { path: '/personal/password', name: 'personal-password', component: namedPage('personal-password', () => import('@/views/personal/password.vue')) },
      { path: '/personal/notice', name: 'personal-notice', component: namedPage('personal-notice', () => import('@/views/personal/notice.vue')) },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('@/views/error/404.vue')), meta: { public: true } },
    ],
  },
]
```

A few deliberate choices here:

- **`/` has no static `redirect`.** A `redirect` is resolved at route-resolve time, which runs *before* the global guard — at that point the menu tree may not be built yet, so any redirect computed there would be wrong. Where `/` actually lands is decided inside `router.beforeEach` instead (see [Guards](/frontend/routing/guards) below).
- **The 404 route is nested inside the shell, not top-level.** Mistyping a URL keeps the sidebar, tabs, and logout button on screen instead of dropping the user onto a bare page.
- **`/personal/notice` is a static route, not a menu item.** It's guarded by `[ActiveSession]` on the backend (any logged-in user can read it, no specific permission needed) — turning it into a menu would mean seeding it and granting it to every role for no reason. Its entry point is the "view all" link under the header's notification bell.

**Previous:** [Routing & Dynamic Menus](/frontend/routing/)
**Next:** [Dynamic Routes: Menu Tree → Real Routes](/frontend/routing/dynamic)
