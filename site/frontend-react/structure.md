# Project Structure & Startup

Move `@/styles/tokens.css` after `import App`, and the theme bridge can no longer read the color variables: `getComputedStyle` comes back empty, antd's colors quietly fall back to their defaults, and nothing warns you. A lot of the rules on the `web-react/` side are import-order rules like this one — they're not written down anywhere, so opening the file is the only way to find them.

Which template to pick, and what each ships, is in [Frontend templates](/guide/frontend-templates); the per-page conventions are spread across [Request flow](/frontend-react/request), [Permissions](/frontend-react/permission), and [i18n](/frontend-react/i18n). This page covers only the foundation.

## Directory layout

All paths below are relative to `web-react/src/`.

| Directory | Responsibility |
|---|---|
| `api/` | `client.ts` (typed `openapi-fetch` wrapper with auth and refresh middleware) + `index.ts` (endpoint calls grouped by domain) + generated `schema.d.ts` |
| `assets/` | Static assets: `svg/` (local SVGs) and `icons.generated.json` (the offline icon subset produced by `gen:icons` at build time, not hand-written) |
| `components/` | Reusable components (DataTable, FormContainer, Can, the Dict* family, and more — see `web-react/COMPONENTS.md`) |
| `composables/` | Module-level logic that is UI-library-agnostic and not bound to a component lifecycle: `useModule` (portal decisions, a router-free async function), `useRealtime` (SignalR connection + unread pub/sub) |
| `hooks/` | React hooks that need component context: `useConfirm` (confirm dialog + result toast, backed by antd's `App.useApp`), `useBatchDelete` (selection state + batch delete) |
| `layouts/` | The layout shell: header, sidebar, tabs, settings drawer, menu search, notice bell |
| `lib/` | One-time setup and runtime bases: `icons` (offline icon registration), `markdown` (md-editor-rt XSS wiring), `echarts` (on-demand chart registration) |
| `locales/` | i18n resources (`zh-CN.ts`, `en-US.ts`) and the i18next instance (`index.ts`); `ext/` is the consumer extension slot that upstream never writes to, so it never conflicts on sync |
| `router/` | `buildRoutes` (menu tree → `RouteObject`), `menuRoutes` (the route-building decisions), `Protected` (the guard component), `MissingRoute` |
| `stores/` | zustand stores (`app`, `auth`, `user`, `dict`, `site`, `tabs`), all persisted |
| `styles/` | `tokens.css` (design tokens), `chrome.css` (layout-shell bare CSS + reset), `code.css` (code-block highlight colors) |
| `theme/` | The antd theme bridge: `antd-theme` (builds the `ThemeConfig`), `useAntdTheme` (the hook that stamps `data-*` and rebuilds the theme), `accents`/`mix` (accent candidates and color mixing), `useDocumentGrayscale` (mourning grayscale, a standalone CSS filter) |
| `types/` | Hand-written domain types (`menu.ts`, `api.ts`) that the UI consumes instead of the verbose openapi-generated types |
| `utils/` | Utility functions (`error.ts`, `chunkUpload.ts`, `tree.ts`, `ua.ts`, `url.ts`) |
| `views/` | Pages, organized by module |

Above `src/` sit two files: the entry `main.tsx` and the root component `App.tsx`. Assembly splits in two: `main.tsx` owns global singletons and side effects, `App.tsx` owns the provider tree and routing.

Vue has a `router/index.ts` that mounts dynamic routes one by one, imperatively, with `addRoute`. There's no equivalent file here. Routes are computed by `buildRoutes(menuTree)` and handed to `useRoutes` to render. When `menuTree` changes, the routes recompute on their own — nothing gets mounted by hand. Details are in [Routing](/frontend-react/routing).

## Startup flow

`main.tsx` does global initialization only, then mounts `<App />`:

```tsx
import '@/styles/tokens.css'   // must precede import App: the theme bridge reads these back via getComputedStyle
import '@/styles/chrome.css'
import '@/styles/code.css'
import '@/locales'             // side effect: builds the i18next instance and wires the store subscription
import { setupIcons } from '@/lib/icons'
import { setupMarkdown } from '@/lib/markdown'
import App from './App'

setupIcons()      // register 4 offline icon sets (non-blocking; menus/AppIcon re-render once sets are ready)
setupMarkdown()   // attach md-editor-rt's XSS filter plugin before the first render

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

The three stylesheets come first because ES modules evaluate in write order. Put `tokens.css` after `import App`, and `App`'s whole dependency graph evaluates first. Any `getComputedStyle` call during that evaluation reads empty values. Feed those into antd's `ConfigProvider`, and the colors quietly fall back to their defaults. It's the same class of ordering problem as Vue's `app.use(pinia)` having to precede `app.use(router)`.

`App.tsx` supplies what `main.tsx` left out: the provider tree, the antd context, and routing.

```tsx
export default function App() {
  // subscribe per field, not to the whole store: unrelated changes shouldn't rebuild the ConfigProvider
  const dark = useAppStore(isDark)
  const accent = useAppStore((s) => s.accent)
  const density = useAppStore((s) => s.density)
  const locale = useAppStore((s) => s.locale)

  const themeConfig = useAntdTheme({ dark, accent, density })
  useDocumentGrayscale() // grayscale is one CSS filter on <html>, kept out of the antd theme deps

  useEffect(() => { /* loadSite() fetches site branding; set document.title when present */ }, [])

  return (
    <ConfigProvider theme={themeConfig} locale={locale === 'en-US' ? antdEnUS : antdZhCN}>
      <AntdApp>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/oauth/callback" element={<CallbackPage />} />
            <Route path="/*" element={<Protected />} />
          </Routes>
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  )
}
```

- **`ConfigProvider`** is the theme bridge. `theme` comes from `useAntdTheme()`. `locale` is antd's own string set (`zh_CN`/`en_US` from `antd/locale`) — a separate layer from the app's i18n. The two have to switch together, or you get a Chinese UI with tables that read "No data".
- **`AntdApp`** (antd's `App` component) supplies the context instances for `message`/`modal`/`notification`. Hooks like `useConfirm` need it, so it nests inside `ConfigProvider`.
- **Routing** has two static entries: `/login` and `/oauth/callback`. Everything else goes through `/*` to `Protected`. The login guard, forced password change, F5 deep-link rebuild, layout shell, and the menu-derived dynamic routes all live inside `Protected` — see [Routing](/frontend-react/routing) and [Portal & guards](/frontend-react/portal-guards).
- `useEffect` calls `loadSite()` once for anonymous site info, and writes `document.title` when a title comes back — the same thing Vue's `App.vue` does in `onMounted`.

## Dev proxy

`vite.config.ts` proxies so the browser only talks to `:5174`:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5174,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR Hub; ws proxies the WebSocket upgrade
  },
},
```

The port is 5174, not Vue's 5173, so both templates can run side by side. `strictPort: true` is deliberate. By default Vite slips silently to the next free port when one is taken. 5173 and 5174 sit right next to each other, so a slip means anything hard-coded to 5174 connects to the other app instead — no error, just someone else's page. Refusing to start is the safer failure.

The backend dev port defaults to 5100. To point at a different backend instance, set `TENON_API_TARGET` before starting Vite. The backend's CORS is deny-all by default, so same-origin access in local dev rides entirely on this proxy. The `/hub` entry is the one extra over Vue's config: it proxies SignalR's WebSocket, which is how realtime notifications arrive.

`vite.config.ts` also `define`s `__APP_VERSION__` from `package.json`'s `version` at build time, shown in the login footer. That value is frozen at bundle time and never goes through backend config. `resolve.alias` has a single entry, `@` → `./src`: the template is self-contained, importing neither `web/` nor any shared layer, so nothing else needs a path.

## Commands

Run these from the `web-react/` directory:

| Script | Command |
|---|---|
| `npm run dev` | `vite`: dev server, `:5174` (`predev` runs `gen:icons` first) |
| `npm run build` | `tsc --noEmit && vite build` (`prebuild` runs `gen:icons` first) |
| `npm run preview` | `vite preview` |
| `npm run test` | `vitest run` |
| `npm run test:watch` | `vitest` |
| `npm run test:e2e` | `playwright test` |
| `npm run lint` | `oxlint` |
| `npm run lint:fix` | `oxlint --fix` |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run gen:api` | `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts` |
| `npm run gen:icons` | `node scripts/generate-icon-subset.mjs` |

Type-checking uses `tsc --noEmit`, not Vue's `vue-tsc`.

::: tip gen:icons runs on its own
`gen:icons` scans the icon names that appear across `src/**`, combines them with the seeds in `scripts/icon-manifest.json`, and writes the offline icon subset to `assets/icons.generated.json`. `predev`/`prebuild` already hang it in front of `dev` and `build`, so normal work never runs it by hand.
:::

::: warning gen:api needs a running backend
`gen:api` pulls `/openapi/v1.json` from a live backend, so start the backend first (`dotnet run --project backend/samples/MinimalHost`, or just run `dev.bat`). The generated `src/api/schema.d.ts` must not be hand-edited; the next `gen:api` overwrites it.
:::

The repo root also holds two batch scripts that manage the whole set at once:

| Script | Effect |
|---|---|
| `dev.bat` | Opens three windows: backend (`:5100`), `web` (Vue, `:5173`), and `web-react` (`:5174`), each running `npm install && npm run dev` |
| `stop.bat` | Stops the frontend and backend processes started by `dev.bat` |

Once this structure runs, the next page is [Routing](/frontend-react/routing): how the backend menu tree derives the route table. After that comes [Request flow](/frontend-react/request), how a single API call travels through the typed client.
