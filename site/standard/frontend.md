# Frontend Standards (Vue 3 + Naive UI)

Check your work against this list before writing a page or wiring up an API. The stack is `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse, with path alias `@` → `src`; see [Core Concepts](/guide/concepts) for the overall architecture, [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md) for component usage, and [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md) for the design system.

## Where things go

- Pages are organized by module/entity: `views/<module>/<entity>/index.vue`; follow `views/system/menu/index.vue` for a full CRUD example (`NDataTable` + `NModal` form + `NPopconfirm`).
- `composables/` (`use*`) holds the single source of UI-library-agnostic logic; Naive message callbacks are injected by the view, not written into the composable.
- `api/` has three pieces: `client.ts` (openapi-fetch) + `index.ts` (grouped by domain) + the generated `schema.d.ts`. See [project structure](/frontend/structure) for the other directories' responsibilities.

## API contract

::: warning schema.d.ts is a generated artifact — don't hand-edit it
`src/api/schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, which needs **the backend running** to fetch `/openapi/v1.json`); hand-edits are overwritten the next time you generate — to change a type, change the backend endpoint/DTO and regenerate. This endpoint isn't mounted in production; see the [FAQ](/faq) for details.
:::

- API calls are centralized in `api/index.ts`, grouped by domain (`authApi`/`userApi`/`moduleApi`/`menuApi`…); each method is shaped like `client.X(...).then(r => unwrap<T>(r))` — never call `client` bare in a view.
- `unwrap` unwraps the envelope uniformly; failures (`code≠0` or non-2xx) all normalize to `ApiError` (carrying `code`/`msgKey`), and the view `catch`es it and produces copy with `translateError(e)`.
- Pagination is normalized at the API layer into `{ items, total }` to fit `useTable` (the backend returns `PagedList<T>{current,size,total,items}`).
- Query parameter names use PascalCase (required by ASP.NET model binding).
- Set `VITE_API_BASE` at build time only when the frontend and backend are genuinely cross-origin (CDN / separate domain), and the backend must then explicitly configure `TenonAdmin:Api:Cors:AllowedOrigins` (deny-all by default). See [the HTTP request layer](/frontend/request) for the auth / 401-refresh middleware and [consuming backend responses](/frontend/api-contract) for the envelope-unwrapping details.

## Routing

- `router/routes.ts` holds only static routes (login, error, shell/layout); the real menu tree is fetched from the backend after login and injected as dynamic routes (in-memory only, never persisted).
- A menu node's `component` string (e.g. `system/user/index`) maps to `/src/views/system/user/index.vue`; a route's `name = menu-${id}`, mounted under `layout`.
- Logout / app switching uses `registerDynamic` / `resetRouter` to add/remove dynamic routes precisely, not resetting the whole route tree.

::: danger Don't persist routesReady / menuTree
Persisting them skips the refresh-rebuild flow and sends you straight to a 404 after a refresh — these two pieces of state must live in memory only. See [routing & dynamic menus](/frontend/routing) for the rebuild mechanism.
:::

## State (Pinia)

- `defineStore` + `actions`; **persist selectively** with `persist: { pick: [...] }`, not the whole store (e.g. `auth` persists only `currentModuleId`).
- Existing stores: `auth` (module/menu/permission codes/`routesReady`), `user` (token/login state), `app` (theme/preferences), `tabs` (tab pages). Logout goes through `reset()` to clear the auth state and the tabs.

## Composables

- Named `use*`, returning reactive refs and methods, **Naive-agnostic** (error / message callbacks are injected by the view — see `useTable`'s `onError`).
- List pages use `composables/useTable.ts` uniformly: pass a `fetcher(({page,pageSize,...params})=>Promise<{items,total}>)` and get back `loading/rows/pagination/load/search/onPage/onPageSize`.

## Button-level permissions

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">Add</n-button>
```

- A single permission code takes a string; an array is OR by default, and the `.and` modifier does AND; a non-match removes the element from the DOM (not just hides it).
- Permission-code values are the backend's normalized routes (same source as `[RolePermission]`), not custom-invented permission strings. See [frontend permissions](/frontend/permission) for details.

## Shared components

- The admin backend has **no component-demo menu**; component usage is consolidated in [`web/COMPONENTS.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md) — read it before writing a page to avoid reinventing the wheel, and update it when you add a new general-purpose component.
- Existing ones include ProTable / FormContainer / `useConfirm` / StatusSwitch / the dict suite (DictSelect/DictRadio/DictTag/DictCheckbox) / OrgTreeSelect / FileUpload (chunked resumable) / ApiSelect (from which UserSelect/RoleSelect derive); see each component's `README.md` in its own directory for the detailed API.

## i18n

- All visible text in views goes through `t('...')` — hardcoded Chinese/English literals are forbidden.
- Error copy is translated by code: the backend sends only `code`/`msgKey`, and the frontend produces copy via `translateError` + `locales/zh-CN.ts`/`en-US.ts`. See [i18n & error codes](/frontend/i18n) for the mechanism.

## Design system

- Business code consumes only the role-token layer (e.g. `--color-text-primary`), never the primitive layer directly (e.g. `--color-gray-500`); the single source of tokens is `src/styles/tokens.css`.
- Component styles use `scoped` + CSS variables (`var(--gap-card)`, etc.), never hardcoded colors/spacing.
- Light/dark switches on `<html data-theme="dark">`, defaulting to light when unset; role tokens / primary color / semantic colors / shadows all flip as a group under it. See [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md) and [Theming & Design Tokens](/frontend/appearance) for the full spec.

## Before committing

```bash
npm run lint        # oxlint (lint:fix to autofix)
npm run typecheck   # vue-tsc --noEmit
npm run build       # vue-tsc --noEmit && vite build
```

Only when all three pass is it done — don't run just one and assume you're fine.
