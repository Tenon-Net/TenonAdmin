# Project Structure & Startup

This page maps `web/`'s directory layout and shows how `main.ts`/`App.vue` boot the app, how the dev proxy talks to the backend, and the scripts you'll use day to day. For the *why* behind the architecture (dynamic routing, data scope, replaceability), see [Core Concepts](/guide/concepts). For the fuller per-directory rules and conventions, see [Frontend Standards](/standard/frontend) — this page is about structure and startup, that one is the checklist.

## Directory layout

All paths are relative to `web/src/`.

| Directory | Purpose |
|---|---|
| `api/` | `client.ts` (typed `openapi-fetch` wrapper) + `index.ts` (API calls grouped by domain) + generated `schema.d.ts` |
| `assets/` | Static assets (SVGs, etc.) |
| `components/` | Reusable components (ProTable, FormContainer, the Dict* suite, and more — see `web/COMPONENTS.md`) |
| `composables/` | UI-library-agnostic `use*` logic |
| `directives/` | Custom directives — `auth.ts` defines `v-auth` |
| `layouts/` | Layout shell: header, sidebar, tabs, settings drawer |
| `lib/` | Small setup helpers — `icons.ts` exports `setupIcons()` |
| `locales/` | i18n resources plus the `i18n` instance (`index.ts`) |
| `router/` | Static routes (`routes.ts`) plus dynamic route injection (`index.ts`) |
| `stores/` | Pinia stores (`app`, `user`, `tabs`, and more) |
| `styles/` | Design tokens (`tokens.css`) and global CSS (`index.css`) |
| `theme/` | Naive UI theme overrides (`naive-theme.ts`, `accents.ts`, `mix.ts`) |
| `types/` | Hand-written types (`menu.ts`, `api.ts`) |
| `utils/` | Helpers (`error.ts`, `chunkUpload.ts`, `tree.ts`, `ua.ts`) |
| `views/` | Pages, organized by module |

Two files sit above `src/`: `main.ts` (the entry point) and `App.vue` (the root component), covered next.

## Bootstrap sequence

`main.ts` wires the app up in this order:

```ts
const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

const app = createApp(App)
app.use(pinia) // must precede router — the router guard reads from stores
app.use(router)
app.use(i18n)
app.directive('auth', vAuth)

app.provide(PRO_TABLE_DEFAULTS, createProTableDefaults({ labels: computed(...) }))
setupIcons()
app.mount('#app')
```

1. **Pinia**, with `pinia-plugin-persistedstate` installed, registered *before* the router — the router guard reads store state.
2. **Router**, then **i18n**.
3. **`v-auth` directive** registered globally (`directives/auth.ts`) — gates elements by permission code.
4. **ProTable defaults**: `PRO_TABLE_DEFAULTS` is provided with a `computed` set of labels (search/reset/refresh/density/column settings, etc.) built from `i18n.global.t`. Because it's a `computed` that reads the active locale, switching languages updates every table's labels immediately — no page has to pass `:labels` manually.
5. **`setupIcons()`** registers the offline icon sets and local SVGs and warms the `ph` icon set. This is non-blocking: once registered, `<Icon>` renders from local data with no external CDN request.
6. **Mount** to `#app`.

Two stylesheets — `styles/tokens.css` and `styles/index.css` — are imported at the top of `main.ts`, before any of the above runs.

`App.vue` is the mount target and handles what `main.ts` doesn't:

- Wraps everything in `n-config-provider`, passing `:theme`/`:theme-overrides` from the `useTheme()` composable and `:locale`/`:date-locale` computed from the app store's locale (naive-ui's `zhCN`/`enUS` and `dateZhCN`/`dateEnUS`).
- Nests `n-message-provider` > `n-dialog-provider` > `router-view` inside the config provider.
- On `onMounted`, calls `loadSite()` (from `useSite()`) to fetch anonymous, site-wide branding info once, then sets `document.title = site.title` if present.
- Watches the app store's `locale` and keeps `i18n.global.locale.value` in sync (`immediate: true`), so a locale change anywhere in the app propagates to translations right away.

## Dev proxy

`vite.config.ts` proxies API calls so the browser only ever talks to `:5173`:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

The backend defaults to port 5100 in dev. To point the dev server at a different backend instance, set `TENON_API_TARGET` before starting Vite. The backend's CORS defaults to deny-all — this proxy is what makes same-origin requests work locally.

`vite.config.ts` also `define`s `__APP_VERSION__` from `package.json`'s `version` at build time, shown in the login page footer — it's baked in at build, not backend-configurable.

::: tip Sibling-package local dev
If you're developing `tenon-naive-iconify-picker` or `tenon-naive-pro-table` alongside this repo, setting `NIP_LOCAL=1` or `NPT_LOCAL=1` before `npm run dev` aliases those packages to a sibling repo's source for HMR. Not relevant unless you're working on those packages directly.
:::

## Common scripts

Run from `web/`:

| Script | Command |
|---|---|
| `npm run dev` | `vite` — dev server on `:5173` |
| `npm run build` | `vue-tsc --noEmit && vite build` |
| `npm run preview` | `vite preview` |
| `npm run lint` | `oxlint` |
| `npm run lint:fix` | `oxlint --fix` |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run gen:api` | `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts` |

::: warning gen:api needs a running backend
`gen:api` fetches `/openapi/v1.json` from a live backend instance, so start the backend first (`dotnet run --project backend/samples/MinimalHost`, or `dev.bat`). Never hand-edit the generated `src/api/schema.d.ts` — the next `gen:api` run overwrites it.
:::

From the repo root, two batch scripts manage the whole stack at once:

| Script | Effect |
|---|---|
| `dev.bat` | Opens two windows: backend (`dotnet run --project samples/MinimalHost`, `:5100`) and frontend (`npm install && npm run dev`, `:5173`) |
| `stop.bat` | Kills whatever is listening on ports `5100` and `5173` |

## Where to next

- How routes get built from the backend menu tree → [Routing](/frontend/routing)
- How API calls flow through the typed client → [Request Flow](/frontend/request)
- The fuller directory/convention checklist → [Frontend Standards](/standard/frontend)
