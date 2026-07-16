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

**Previous:** [Router Guards](/frontend/routing/guards)

## Where to next

- [Frontend Structure](/frontend/structure) — how views, components, and stores are laid out.
- [Permission Directive (`v-auth`)](/frontend/permission) — gating buttons and elements by permission code.
- [Add a Frontend Page](/tutorial/frontend-page) — a hands-on walkthrough of adding a new menu-backed page end to end.
