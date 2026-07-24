# Routing & Dynamic Menus

web-react's route table is a plain array derived from the menu tree and handed to `useRoutes`. Change the menu tree and React re-renders and re-matches the routes on its own — no imperative `addRoute`, and none of the current-URL re-resolution the Vue version needs. Routes come from two sources: a static shell fixed at build time, and dynamic routes derived from the menu tree after login.

How the portal decides which app to enter, and how the guards stitch the two sides together, both belong to [Multi-App Portal & Guards](/frontend-react/portal-guards); this doesn't touch them.

```text
static routes (App.tsx / Protected.tsx)     dynamic routes (buildRoutes ← menuTree)
  ├─ /login  /oauth/callback  public         useRoutes([ ...buildRoutes(menuTree) ])
  └─ /*  → <Protected>                        for each Menu node:
        ├─ /module  chooser, outside shell      component string → /src/views/**/*.tsx
        └─ <LayoutShell>                          menuToRouteDescriptors  decide which
              ├─ ...buildRoutes(menuTree)          buildRoutes             emit RouteObject
              ├─ /personal/*  5 static pages
              ├─ /  → <Navigate to={home}>
              └─ *  → <NotFoundPage>

fixed at build time, unchanged between deploys.   re-derived whenever menuTree changes
                                                  (login / switch app / F5).
```

"Derived" is the load-bearing word. The Vue side hangs each dynamic route with an imperative `router.addRoute`, and after a rebuild it has to re-resolve the current URL by hand. Here the routes are a pure function of `menuTree`, and `useRoutes(routes)` re-matches automatically as `menuTree` changes — there's no window where "the route is mounted but the current URL hasn't re-matched."

## Static routes

Static routes come in two tiers. The outer tier lives in `App.tsx`: `/login` and `/oauth/callback` are public pages, everything else under `/*` goes to `<Protected>`. Inside the protected area sits a second tier:

```tsx
useRoutes([
  // chooser is full-screen, outside the layout shell
  { path: '/module', element: <ModuleChooser /> },
  {
    element: <LayoutShell />,
    children: [
      ...buildRoutes(menuTree),            // dynamic routes derived from the menu
      { path: '/personal/profile',  element: <ProfilePage /> },
      { path: '/personal/password', element: <PasswordPage /> },
      { path: '/personal/notice',   element: <NoticePage /> },
      { path: '/personal/sessions', element: <SessionsPage /> },
      { path: '/personal/bindings', element: <BindingsPage /> },
      { path: '/', element: <Navigate to={home} replace /> },   // home derives from menuTree
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
```

A few of these choices are deliberate:

- **`/` has no hard-coded redirect target.** The landing point is `<Navigate to={home}>`, where `home` is computed by `homePath(authStore)` through a `useMemo` that depends on `menuTree`. Before the menu tree is ready `home` falls back to `/module`; once it's ready it points at the current app's home. A static `redirect` would resolve to the wrong target while the menu tree is still empty.
- **404 sits inside the shell.** `*` is a child of `<LayoutShell>`, so a mistyped URL still shows the sidebar, tab bar, and logout button instead of dumping the user onto a bare page outside the app.
- **`/module` beating the dynamic `*` is not about array order.** react-router ranks by route specificity: the static segment `/module` is inherently more specific than the wildcard `*`, and it still wins even moved to the end of the array (verified). Putting it first is only for readability.
- **The five personal pages are static routes, not menu items.** They all go through `[ActiveSession]` on the backend — any logged-in user may read them, no specific permission code required. Turning them into menu items would mean seeding each one and granting it per role, pure busywork. Their entry points are the header user dropdown and the notification bell, not the sidebar menu.

## Dynamic routes: menu tree → real routes

Everything under `<LayoutShell>` beyond those five static personal pages and the 404 fallback comes from `buildRoutes(menuTree)`. The function flattens the menu tree and computes one route per node whose `type` is `MenuType.Menu`. A `Catalog` only organizes the hierarchy and has no page of its own; a `Button` isn't a route; both are skipped. The convention is direct: a menu's `component` field is the file path relative to `src/views`, minus the `.tsx` suffix. So `system/user/index` maps to `/src/views/system/user/index.tsx`.

### Decision and materialization, split in two

`buildRoutes` is split internally into two pieces, each with one job:

```ts
// menuRoutes.ts —— decision: which nodes get a route, and what kind (view / iframe / missing)
menuToRouteDescriptors(tree, hasView): RouteDescriptor[]

// buildRoutes.tsx —— materialization: descriptor → react-router RouteObject
//   view    → page wrapped in React.lazy + Suspense
//   iframe  → the shared IframeView
//   missing → a diagnosable MissingRoute
```

The split exists so the decision can be unit-tested without dragging in react-router. `menuToRouteDescriptors` holds the only real branching in the dynamic-route path, and it never touches `import.meta.glob` or `console` directly: `hasView` (does the component exist) and `warn` (alert on a missing component) are both injected as parameters. That's what makes the "should a missing component still get a route, and should it warn" branch assertable.

### The component path is the file path

The components that can materialize into pages are collected into a lookup by `import.meta.glob('/src/views/**/*.tsx')`. The `component` field is matched against that table, and on a hit its lazy loader is taken. Admins don't type this path by hand: `buildRoutes.tsx` exports `viewComponentPaths`, which turns every valid key in the glob table back into a `component` string of the same shape and feeds it to the "component path" dropdown in the menu-management form. Because the dropdown is derived from the glob, it can't drift away from the real files.

### Missing component: a diagnosable route, not a silent drop

When `component` isn't found in the glob table, the handling here differs from Vue. Vue simply drops the menu item and registers no route, so clicking it lands on a 404 and the admin can't tell what went wrong. This side still builds a route, but renders it as `MissingRoute`: a `role="alert"` line on the page naming exactly which component is missing. Both sides also log a `console.warn`. Leaving a visible diagnostic is easier to trace than silently losing a menu item. And with the dropdown above, the path is hard to mistype in the first place.

### The glob exclusion list

That glob carries a list of exclusions, none of them casual:

```ts
import.meta.glob([
  '/src/views/**/*.tsx',
  '!/src/views/**/*.spec.tsx',
  '!/src/views/login/**',    '!/src/views/module/**',
  '!/src/views/oauth/**',    '!/src/views/error/**',
  '!/src/views/embed/**',    '!/src/views/personal/**',
  '!/src/views/_placeholder/**',
])
```

Every excluded group is **statically** imported somewhere else: the login page, app chooser, OAuth callback, 404, and the five personal pages are static routes imported directly; the iframe view is statically imported by `buildRoutes`; `_placeholder` is an internal stub. Leaving them in the glob has two consequences: the same file gets both a static and a dynamic import, which Vite can't code-split (that's the source of the build warning); and the admin could pick a broken page in the "component path" dropdown (the iframe view has no src, a placeholder is an empty shell). When you add a static-route page, exclude it here too, or the conflict comes back.

## External links and embeds: no new menu type

`MenuType` has only three values: `Catalog`/`Menu`/`Button`. Neither external links nor iframe embeds add an enum value; they reuse the existing `Path`/`Component` fields and let `isHttpUrl()` (does the string start with `http(s)://`) tell the intent apart.

| Effect you want | How to configure it | How the runtime handles it |
| --- | --- | --- |
| External-link menu | `Path` = full URL, `Component` empty | `buildRoutes` skips it, no route; the menu still shows it, and a click does `window.open` on the layout side |
| Embedded iframe menu | `Path` = internal path, `Component` = full URL | Builds one shared `IframeView` route; the URL in `Component` is passed in as `src` |

The external-link case has an asymmetry: `buildRoutes` skips it and builds no route, yet `menuItems` still puts it in the menu with the URL as its `key`. The reason is that an external link needs to be visible and clickable in the menu without occupying an internal route. On click, `isHttpUrl(key)` recognizes it and `window.open` opens a new tab.

On the iframe side React skips a bit of care the Vue version needs. Vue stores the URL in `route.meta.iframeSrc` and has to snapshot it once in `setup`, so that a `keep-alive` cache hit doesn't reactively recompute `src` to empty. Here `src` is a prop baked onto `<IframeView src={...} />` when the route is built; on a cache hit the element is reused as-is and the prop never changes, so there's no reactive-recompute hole to plug.

## Page caching: a hand-rolled keep-alive

React has no equivalent of Vue's `<keep-alive>`, so `KeepAliveOutlet` is hand-rolled. It uses a `Map<path, element>` to keep the "should-cache" open tabs permanently mounted, and hides inactive pages with `display:none` instead of unmounting them. Switch away and back and the component tree is still there — state, scroll position, and unsubmitted form fields all survive. `noCache` pages (details and other transient pages) don't enter the Map and render live: unmount on leave, remount and refetch on revisit. When a tab is closed, its cache entry is evicted per `aliveKeys`.

The biggest difference from Vue is "what the cache matches on." Vue's `<keep-alive :include>` matches by a component's `name`, but the dozens of `index.vue` files under `src/views/**` infer colliding names that also don't match the route names, so Vue has to use `namedPage` to give each page an explicit `name` equal to its route name. Here the cache is keyed directly by route `path`, which is already unique, so that entire `namedPage` naming machinery is unnecessary.

Refreshing a tab (`refreshTab`) has to force a remount, and deleting the cache alone isn't enough. The store sets an `excludeKey` and increments `reloadKey`; `KeepAliveOutlet` picks that up, swaps the version number in that path's div `key`, and deletes its cache entry. Only a changed `key` makes React unmount and remount; delete the Map entry while `key` stays the same and React reuses the same fiber, leaving the state uncleared.

The page-switch entrance animation is applied only to the currently visible page, using a CSS `animation` rather than a `transition`. When a div flips from `display:none` back to `block`, the `animation` re-runs, whereas a `transition` doesn't fire across a `display` change — so switching away and back still animates in, without any remount and without disturbing the cache.

## No convention-based detail routes

The Vue version has a convention: `views/**/detail.vue` auto-generates a `/<module>/:id/detail` route. **web-react has no equivalent.** There's no `registerDetailRoutes`-style scan in the router directory; detail views don't rely on a convention route but expand in place within the list page, via a dialog or drawer (`system/user` edits in a Modal, `system/log` in a Drawer). If you genuinely need a standalone detail page with a route param, configure it explicitly like any other page — don't expect a convention to generate it.

::: tip Two things that aren't here
There's no progress bar in the routing path (no NProgress or similar library). `document.title` isn't set per-navigation by a guard either: it's set once when `App` loads the site config, and again by the system-config page when the site title changes, never in step with each navigation.
:::

To walk one chain end to end from scratch, start with [Structure & Assembly](/frontend-react/structure); the side-by-side comparison of the two templates is in [Frontend Templates](/guide/frontend-templates).
