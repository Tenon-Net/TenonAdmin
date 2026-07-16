# Frontend Standards (Vue 3 + Naive UI)

> This page is an actionable checklist distilled from the existing code. See [Core Concepts](/guide/concepts) for the overall frontend architecture, [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md) for component usage, and [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md) for the design system.

## Stack and directory layout

`<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse. Path alias `@` → `src`.

| Directory | Responsibility |
|---|---|
| `views/` | Pages (organized by module/entity into subdirectories, `views/<module>/<entity>/index.vue`) |
| `composables/` | UI-library-agnostic logic, single source of truth (`use*`); Naive messages stay in the view layer |
| `stores/` | Pinia state |
| `layouts/` | Layout shell (header/sidebar/tabs/settings) |
| `components/` | Reusable components |
| `api/` | `client.ts` (openapi-fetch) + `index.ts` (grouped by domain) + generated `schema.d.ts` |
| `router/` | Static routes + dynamic route rebuilding |
| `theme/`, `styles/` | Theme tokens |
| `locales/` | i18n |
| `directives/` | `v-auth`, etc. |
| `types/` | Hand-written types (`menu.ts`) and re-exports |

## API contract flow

::: warning schema.d.ts is a generated artifact
`src/api/schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, which **requires the backend to be running** to fetch `/openapi/v1.json`). **Never hand-edit it** — the next generation run will overwrite your changes. To change types, modify the backend endpoint/DTO and regenerate. This endpoint isn't mounted in production — see the [FAQ](/faq) for details.
:::

`src/api/client.ts` is a typed `openapi-fetch` wrapper around the schema, handling three things:

```ts
// Default empty baseUrl = same-origin: the schema's path keys already include /api/v1
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
```

- **Auth middleware**: reads the latest token from `useUserStore()` before each request and injects `Authorization: Bearer`.
- **401 refresh middleware**: concurrent 401s are coalesced into a single refresh (`refreshOnce`); after a successful refresh, the original request is replayed (write requests `clone()` the body upfront to keep a replayable copy); a failed refresh clears the session and redirects to login.
- Setting `VITE_API_BASE` at build time is only needed when the frontend and API are genuinely cross-origin (CDN / separate domain), in which case the backend must also explicitly configure `TenonAdmin:Api:Cors:AllowedOrigins` (default deny-all).

`api/index.ts` exports grouped by domain (`authApi`/`userApi`/`moduleApi`/`menuApi`, etc.), each method shaped like `client.X(...).then(r => unwrap<T>(r))`:

- **`unwrap`** uniformly unwraps the envelope: a 2xx `Result<T>` (throws `ApiError` if `code≠0`), and non-2xx envelopes/ProblemDetails, are both normalized into `ApiError` (carrying `code`/`msgKey`). The view layer catches it and displays text via `translateError(e)`.
- Pagination responses are normalized at the API layer into `{ items, total }` to fit `useTable` (the backend returns `PagedList<T>{current,size,total,items}`).
- Query parameter names use PascalCase (required by ASP.NET model binding).

## Routing (static + dynamic menu injection)

- `router/routes.ts` holds only static routes (login, error, shell/layout). The real menu tree is fetched from the backend after login and injected as **dynamic routes** (living only in memory, never persisted).
- Component resolution (`composables/useAuthMenu.ts`): `import.meta.glob('/src/views/**/*.vue')` collects all pages; a menu node's `component` string (e.g. `system/user/index`) maps to `/src/views/system/user/index.vue`. A route's `path` comes from the menu's `path`, `name = menu-${id}`, mounted under `layout`.
- **F5 / deep links**: dynamic routes are lost on refresh. The router guard calls `useModule().enterInitial()` to rebuild them when `routesReady=false`, then re-resolves the current URL.

::: danger Don't persist routesReady / menuTree
Persisting them would skip the rebuild flow, sending you straight to a 404 after refresh. These two pieces of state must stay in-memory only.
:::

- Logout / app switching uses `registerDynamic` / `resetRouter` to precisely add/remove dynamic routes, rather than resetting the entire route tree.

## State (Pinia)

`defineStore` + `actions`; persist **selectively** with `persist: { pick: [...] }`, not the entire store (e.g. `auth` only persists `currentModuleId`). Existing stores:

| Store | Responsibility |
|---|---|
| `auth` | Module/menu/permission codes/`routesReady` |
| `user` | Token/login state |
| `app` | Theme/preferences |
| `tabs` | Tab pages |

Logout calls `reset()` to clear the auth state and tabs.

## Composables

- Named `use*`, returning reactive refs and methods; **UI-library-agnostic** (error/message callbacks are injected by the view — see `useTable`'s `onError`).
- List pages consistently use `composables/useTable.ts`: pass a `fetcher(({page,pageSize,...params})=>Promise<{items,total}>)`, get back `loading/rows/pagination/load/search/onPage/onPageSize`.

## Button-level permissions (`v-auth`)

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">Add</n-button>
```

- A single permission code takes a string; an array defaults to OR; the `.and` modifier does AND. A non-match removes the element from the DOM (not just hides it).
- Permission code values are the backend's normalized routes (the same source as `[RolePermission]`), not custom-invented permission strings.

## Shared components

::: tip Check this index before writing a page
The admin backend deliberately has **no component-demo menu** — component usage is consolidated in [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md). Check it before adding a new page to avoid reinventing the wheel. Update it too when you add a new shared component.
:::

Existing components cover list pages (ProTable), form containers (FormContainer), confirmation dialogs (`useConfirm`), inline enable/disable toggles (StatusSwitch), the dict suite (DictSelect/DictRadio/DictTag/DictCheckbox), org tree selection (OrgTreeSelect), file upload (FileUpload, with chunked resumable upload), and the remote paginated dropdown base (ApiSelect, from which UserSelect/RoleSelect are derived). See each component's `README.md` in its directory for detailed API docs.

## i18n

- Text is translated by code: the backend only sends `code`/`msgKey`; the frontend produces display text via `translateError` + `locales/zh-CN.ts`/`en-US.ts`.
- All visible text in views goes through `t('...')` — hardcoded Chinese/English literals are forbidden.

## Design system

- The single source of truth for tokens is [`src/styles/tokens.css`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/src/styles/tokens.css) — colors/font sizes/spacing/radii/shadows all come from CSS variables defined there. Business code **only consumes the role-token layer** (e.g. `--color-text-primary`), never the primitive layer directly (e.g. `--color-gray-500`).
- Light/dark switching relies on `<html data-theme="dark">`, defaulting to light when unset; role tokens/primary colors/semantic colors/shadows are all flipped as a group under `[data-theme="dark"]`.
- Component styles use `scoped` + CSS variables (`var(--gap-card)`, etc.) — never hardcode colors/spacing.
- The full spec (design tone, layout dimensions, component hierarchy, token → Naive `GlobalThemeOverrides` mapping) is in [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md).

## Components / views

- `<script setup lang="ts">`; table columns use `h()` render functions. `views/system/menu/index.vue` is a complete CRUD example (`NDataTable` + `NModal` form + `NPopconfirm`).

## Pre-commit checks

```bash
npm run lint        # oxlint (lint:fix to autofix)
npm run typecheck   # vue-tsc --noEmit
npm run build        # vue-tsc --noEmit && vite build
```

All three must pass — don't consider it done after running just one of them.

---

> See section 2 of [`docs/coding-standards.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/docs/coding-standards.md) for the fuller write-up.
