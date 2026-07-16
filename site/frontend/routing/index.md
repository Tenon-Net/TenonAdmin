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

- [Static Routes](/frontend/routing/static)
- [Dynamic Routes: Menu Tree → Real Routes](/frontend/routing/dynamic)
- [Multi-App Portal](/frontend/routing/portal)
- [Router Guards](/frontend/routing/guards)
- [Keep-Alive & Named Pages](/frontend/routing/keep-alive)

**Next:** [Static Routes](/frontend/routing/static)
