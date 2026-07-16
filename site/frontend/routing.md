# Routing & Dynamic Menus

Routes in the frontend come from two independent sources: a small **static shell** defined at build time, and a much larger set of **dynamic routes** rebuilt at runtime from whichever app's menu tree the backend hands back after login. This page walks through both, how the multi-app portal decides which app to enter, the guards that stitch it together, and why pages need a stable identity for `keep-alive` to work.

## Overview

```text
staticRoutes (router/routes.ts)        buildRoutesForModule (useAuthMenu.ts)
  ├─ /login                              fetches personalApi.menu(moduleId)
  ├─ /module  (app chooser)              flattens the tree
  └─ /  → layout (default.vue)           for each Menu node:
        ├─ /personal/profile               component string → /src/views/**/*.vue
        ├─ /personal/password               router.addRoute('layout', { name: 'menu-{id}', ... })
        ├─ /personal/notice
        └─ /:pathMatch(.*)*  (404)

Fixed at build time.                    Rebuilt on every login / app switch / hard refresh.
```

The static side never changes between deploys. The dynamic side is entirely driven by whatever menu tree the currently-selected app returns — different users, different roles, different apps all end up with a different set of routes registered under `layout`.

## In this section

- [Static Routes](/frontend/routing)
- [Dynamic Routes: Menu Tree → Real Routes](/frontend/routing)
- [Multi-App Portal](/frontend/portal-guards)
- [Router Guards](/frontend/portal-guards)
- [Keep-Alive & Named Pages](/frontend/routing)



---

<!-- TODO(rewrite): merged from static.md -->

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

- **`/` has no static `redirect`.** A `redirect` is resolved at route-resolve time, which runs *before* the global guard — at that point the menu tree may not be built yet, so any redirect computed there would be wrong. Where `/` actually lands is decided inside `router.beforeEach` instead (see [Guards](/frontend/portal-guards) below).
- **The 404 route is nested inside the shell, not top-level.** Mistyping a URL keeps the sidebar, tabs, and logout button on screen instead of dropping the user onto a bare page.
- **`/personal/notice` is a static route, not a menu item.** It's guarded by `[ActiveSession]` on the backend (any logged-in user can read it, no specific permission needed) — turning it into a menu would mean seeding it and granting it to every role for no reason. Its entry point is the "view all" link under the header's notification bell.



---

<!-- TODO(rewrite): merged from dynamic.md -->

# Dynamic Routes: Menu Tree → Real Routes

Everything under `layout` other than the three static personal pages and the 404 catch-all comes from `useAuthMenu.ts`'s `buildRoutesForModule`:

```ts
const views = import.meta.glob('/src/views/**/*.vue') as Record<string, () => Promise<Component>>

export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  resetRouter()
  for (const node of flatten(tree)) {
    if (node.type !== MenuType.Menu || !node.component || !node.path) continue
    const key = `/src/views/${node.component.replace(/^\/+/, '')}.vue`
    const loader = views[key]
    if (!loader) {
      console.warn('[menu] 缺少视图组件:', node.component, '→', key)
      continue
    }
    const name = `menu-${node.id}`
    if (router.hasRoute(name)) router.removeRoute(name)
    router.addRoute('layout', {
      path: node.path.startsWith('/') ? node.path : `/${node.path}`,
      name,
      component: namedPage(name, loader),
      meta: { title: node.title, icon: node.icon, keepAlive: true },
    })
    registerDynamic(name)
  }
  auth.routesReady = true
}
```

The pipeline: fetch the tree for the given app (`personalApi.menu(moduleId)`), flatten it, and for every node whose `type` is `MenuType.Menu` (catalogs and buttons are skipped — catalogs have no page, buttons aren't routes), resolve its `component` string against a `import.meta.glob('/src/views/**/*.vue')` map. The convention is direct: a menu's `component` field is the view path relative to `src/views`, minus the `.vue` extension — `system/user/index` resolves to `/src/views/system/user/index.vue`.

**A missing component doesn't break anything visibly — it just silently drops the menu item.** If `node.component` doesn't match any glob key, `buildRoutesForModule` logs a `console.warn` and skips the node entirely; no route is added, so the menu link (if it renders at all) would 404 or simply not appear, with no indication to the end user of what went wrong. To keep menu administrators from hitting this blind, `useAuthMenu.ts` also exports `viewComponentPaths` — every valid glob key, normalized to the same `component` string format — which feeds the component-path field in the menu-admin form as a dropdown instead of free text.

Each registered route gets `name: 'menu-{id}'` and `meta.keepAlive: true`, and is added under the `layout` parent with `router.addRoute('layout', ...)`. Every name added this way is tracked via `registerDynamic(name)` so it can be torn down precisely on logout or app switch (see `resetRouter` in `router/index.ts`).



---

<!-- TODO(rewrite): merged from keep-alive.md -->

# Keep-Alive & Named Pages

`layouts/default.vue` caches pages with:

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive`'s `:include` matches by the rendered component's **`name`**. For a `<script setup>` single-file component, Vue infers that name from the filename — and with dozens of `index.vue` files across `src/views/**`, those inferred names collide with each other and don't match the router's own name for the route (`menu-{id}`). `router/namedPage.ts` closes that gap:

```ts
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const hit = cache.get(name)
  if (hit?.loader === loader) return hit.comp

  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  const comp = defineComponent({ name, render: () => h('div', { class: 'page-view' }, h(inner)) })
  cache.set(name, { loader, comp })
  return comp
}
```

Every static and dynamic route is wrapped through `namedPage`, giving its component an explicit `name` equal to the route name — so `:include="tabs.cachedNames"` (an array of `TabItem.name`, i.e. route names) can actually match it. The wrapper is memoized in a `Map` keyed by name and only rebuilt if the underlying **loader reference** changes: `import.meta.glob` returns a stable function per file, so editing an unrelated menu and triggering a full `buildRoutesForModule` rebuild reuses the same component object for routes whose `component` path didn't change — keeping their `keep-alive` cache entry intact instead of forcing a remount. It also wraps the lazy component in a single `<div class="page-view">` root, because `default.vue`'s `<transition mode="out-in">` requires a single element root and several page components render multiple sibling roots (a main body plus modal dialogs).

`stores/tabs.ts` complements this: its `cachedNames` getter filters the tab list down to `router.hasRoute(n)`, so during the brief window after a menu rebuild but before a stale tab's route is re-registered, `keep-alive` isn't asked to match a name that doesn't exist yet. `refreshTab(name)` forces a genuine remount (bypassing the cache) by setting `excludeName` and bumping `reloadKey`, which `default.vue` watches to briefly `v-if`-unmount the router view before restoring it.

::: tip Not here
Two things you might expect to find in the router aren't there. There's no route-level progress bar (no NProgress or equivalent) anywhere in this pipeline. And the document title isn't set by a guard — it's set once in `App.vue` on mount and again by the site-config page when the title changes, not per-navigation.
:::


## Where to next

- [Frontend Structure](/frontend/structure) — how views, components, and stores are laid out.
- [Permission Directive (`v-auth`)](/frontend/permission) — gating buttons and elements by permission code.
- [Add a Frontend Page](/guide/frontend-page) — a hands-on walkthrough of adding a new menu-backed page end to end.
