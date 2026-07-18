# Routing & Dynamic Menus

You add a menu in menu administration, fill in the component path, hit save — and it becomes a real, clickable page, with no routing table you wrote by hand anywhere in between. The frontend's routes come from two unrelated sources: a **static shell** frozen at build time, and **dynamic routes** rebuilt at runtime from the current app's menu tree after login.

How the portal decides which app to enter, and how the guards stitch the two sides together, aren't covered here — that's the job of [Multi-App Portal & Router Guards](/frontend/portal-guards).

```text
staticRoutes (router/routes.ts)         buildRoutesForModule (useAuthMenu.ts)
  ├─ /login                               fetch personalApi.menu(moduleId)
  ├─ /module  (app chooser)               flatten the menu tree
  └─ /  → layout (default.vue)            for each Menu-type node:
        ├─ /personal/profile                component string → /src/views/**/*.vue
        ├─ /personal/password               router.addRoute('layout', { name: 'menu-{id}', ... })
        ├─ /personal/notice
        ├─ /personal/sessions
        └─ /:pathMatch(.*)*  (404)

Frozen at build time, unchanged          Rebuilt once each on login / app switch /
between deploys.                          hard refresh.
```

The static side never changes between deploys. The dynamic side is driven entirely by the menu tree the currently-selected app returns — different users, different roles, different apps all end up with a different set of routes hanging off `layout`.

## Static routes

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
      { path: '/personal/sessions', name: 'personal-sessions', component: namedPage('personal-sessions', () => import('@/views/personal/sessions.vue')) },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('@/views/error/404.vue')), meta: { public: true } },
    ],
  },
]
```

Several choices here are deliberate:

- **`/` has no static `redirect`.** A `redirect` is evaluated at route-resolve time, which runs *before* the global guard — and at that point the menu tree usually isn't built yet, so any landing spot computed there is guaranteed wrong. Where `/` actually lands is decided by the guard in `router.beforeEach` (see [Multi-App Portal & Router Guards](/frontend/portal-guards)).
- **The 404 is nested inside the shell, not at the top level.** Mistype a URL and the sidebar, tab bar, and logout button are all still there — the user isn't flung out onto a bare page.
- **`/personal/notice` and `/personal/sessions` are static routes, not menu items.** Both are guarded on the backend by `[ActiveSession]` (any logged-in user can read them, no specific permission code needed) — making them menus would mean seeding them and then granting them to every role, pure busywork. Their entry points are the "view all" link on the header's notification bell and the header user dropdown.

## Dynamic routes: menu tree → real routes

Everything under `layout` other than those four static personal pages and the 404 fallback comes from `buildRoutesForModule` in `useAuthMenu.ts`:

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

The whole chain: fetch the current app's menu tree (`personalApi.menu(moduleId)`), flatten it into a one-dimensional array, and for every node whose `type` is `MenuType.Menu` (a `Catalog` has no page and a `Button` isn't a route — both are skipped) take its `component` string and look it up against the map built by `import.meta.glob('/src/views/**/*.vue')`. The convention is direct: a menu's `component` field is the file path relative to `src/views`, minus the `.vue` extension — so `system/user/index` maps to `/src/views/system/user/index.vue`.

**A missing component produces no conspicuous error at all — it just quietly drops the menu item from the routing table.** If `node.component` doesn't match any key in the glob map, `buildRoutesForModule` logs one `console.warn` and skips the node — no route is registered, so the menu link (even if it still renders) either 404s on click or simply doesn't appear, with no sign to the ordinary user of what went wrong. To keep menu administrators from stepping on this, `useAuthMenu.ts` also exports `viewComponentPaths` — every valid glob key converted back into that same `component` string format — which feeds the "component path" field in the menu-admin form as a dropdown, instead of leaving people to type it by hand.

Each registered route carries `name: 'menu-{id}'` and `meta.keepAlive: true`, and is hung under the `layout` parent via `router.addRoute('layout', ...)`. Every name added this way is tracked through `registerDynamic(name)`, so it can be torn down precisely on logout or app switch (see `resetRouter` in `router/index.ts`).

## Page caching & named components

`layouts/default.vue` caches pages like this:

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive`'s `:include` matches by the rendered component's **`name`**. For a `<script setup>` single-file component, Vue infers that `name` from the filename — and with dozens of identically-named `index.vue` files across `src/views/**`, those inferred names collide with each other and don't line up with the route's own name (`menu-{id}`). `router/namedPage.ts` plugs that hole:

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

Static or dynamic, every page component is wrapped through `namedPage`, giving it an explicit `name` equal to the route name — which is precisely what lets `:include="tabs.cachedNames"` (really an array of `TabItem.name`, i.e. route names) match it. The wrapper is memoized in a `Map` keyed by name and rebuilt only when the underlying **loader reference** changes: `import.meta.glob` returns the same stable function per file, so editing an unrelated menu and triggering a full `buildRoutesForModule` rebuild still reuses the same component object for routes whose `component` path didn't change — their `keep-alive` cache entries are left untouched, not forced to remount. It also wraps the lazy component in a single `<div class="page-view">` root node, because `default.vue`'s `<transition mode="out-in">` requires a single element root, while plenty of page templates are themselves multi-root (a main body plus a few side-by-side modals).

`stores/tabs.ts` adds a safety net on top of this: its `cachedNames` getter filters the tab list down to those where `router.hasRoute(n)` is true, so during the brief window after a menu rebuild — when an old tab's route hasn't been re-registered yet — `keep-alive` is never asked to match a name that doesn't exist. `refreshTab(name)` forces a genuine remount (bypassing the cache) by setting `excludeName` and bumping `reloadKey`; `default.vue` watches `reloadKey` and briefly `v-if`-unmounts the router outlet before restoring it.

::: tip Two things you won't find here
There's no progress bar anywhere in the routing pipeline (no NProgress or similar). And the document title isn't set by a guard — it's set once when `App.vue` mounts, and again by the site-config page when the title changes, never per-navigation.
:::

To walk this whole pipeline from scratch — create the view component, seed a menu, get the component path right — see [Add a Frontend Page](/guide/frontend-page).
