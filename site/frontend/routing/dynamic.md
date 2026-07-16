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

**Previous:** [Static Routes](/frontend/routing/static)
**Next:** [Multi-App Portal](/frontend/routing/portal)
