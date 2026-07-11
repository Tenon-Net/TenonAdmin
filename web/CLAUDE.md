## Frontend architecture (`web/`)

Vue 3 `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse. Path alias `@` → `src`.

- **API is contract-generated**: `src/api/schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, backend must be running). `src/api/client.ts` wraps `openapi-fetch` typed against it. Don't hand-edit `schema.d.ts`; regenerate it.
- **Dynamic routing**: `router/routes.ts` holds static routes (login, error, shell); the real menu tree is fetched from the backend after login and injected as dynamic routes (multi-app portal — user picks/switches an app). `useModule().enterInitial()` in the router guard rebuilds them on hard refresh/deep-link, since dynamic routes live only in memory. `v-auth` directive (`directives/auth.ts`) gates buttons by permission.
- **Stores**: `auth` (token/session, routesReady), `user` (profile/login state), `app` (theme/prefs). First visit follows system dark/light (VueUse `usePreferredDark`); after a manual toggle, persistence takes over.
- Login page ships three swappable skins (`views/login/skins/`); theming via `styles/tokens.css` + `theme/`. Design system spec is `web/DESIGN.md`.
- **Shared components live in `web/COMPONENTS.md`** — read it before writing a page (FormContainer, useConfirm, StatusSwitch, dict suite, ProTable, icons); no component-demo menu by design. Update it when adding a shared component.
