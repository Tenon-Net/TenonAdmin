# Frontend Permissions

The frontend splits authorization concerns across two Pinia stores: `user` holds identity and tokens, `auth` holds permissions, menu tree, and module state. This page covers how button-level permission checks work end to end — the `v-auth` directive, the `hasPerm` getter it delegates to, and the render-function escape hatch for cases the directive syntax can't reach.

## Overview: two stores, split by what persists

- **`user` store** (`src/stores/user.ts`) — `accessToken`, `refreshToken`, `userInfo` (`userId`, `account`, `name`, `mustChangePassword`), plus the `isLoggedIn` getter and `setSession`/`clear` actions. Declared with `persist: true` — **the entire store** is written to `localStorage`, which is why a page refresh keeps you logged in.
- **`auth` store** (`src/stores/auth.ts`) — `modules`, `currentModuleId`, `defaultModuleId`, `menuTree`, `permissionCodes`, `permissionsLoaded`, `isSuperAdmin`, `routesReady`, plus the `homePath` and `hasPerm` getters. Declared with `persist: { pick: ['currentModuleId'] }` — **only `currentModuleId` is persisted**.

The split exists because the two halves have different lifetimes. Tokens and profile need to survive a refresh outright (that's the whole point of staying logged in). Permissions, the menu tree, and `routesReady`, on the other hand, are rebuilt every time the app boots: dynamic routes only live in the router's in-memory state, so `routesReady` being persisted as `true` would make a refresh skip route rebuilding and every dynamic route would 404. `currentModuleId` is the one exception — it's persisted so a hard refresh or deep link can restore "which app you were in" before the guard re-fetches everything else via `useModule().enterInitial()`.

## `v-auth`: button-level permission directive

Registered globally in `main.ts`:

```ts
app.directive('auth', vAuth)
```

It accepts a single permission code, an array of codes (OR by default), or an array with the `.and` modifier (AND):

```vue
<!-- single code -->
<n-button v-auth="'POST:/api/v1/sample/doc'" @click="openAdd">{{ t('common.add') }}</n-button>

<!-- array, OR semantics: shows if the user has ANY of these codes -->
<n-button v-auth="['POST:/api/v1/sample/doc', 'PUT:/api/v1/sample/doc/{id}']">...</n-button>

<!-- array with .and modifier, AND semantics: shows only if the user has ALL of these codes -->
<n-button v-auth.and="['GET:/api/v1/sample/doc', 'DELETE:/api/v1/sample/doc/{id}']">...</n-button>
```

The directive only implements a `mounted` hook — it does not react to permission changes after mount. On mount, it evaluates the codes against `authStore.hasPerm` and, if the check fails, calls `el.remove()`:

```ts
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const ok = Array.isArray(need) ? need[mode]((c) => auth.hasPerm(c)) : auth.hasPerm(need)
    if (!ok) el.remove()
  },
}
```

**It physically removes the DOM node** — not `display: none`, not `v-if` re-evaluation. A denied button never exists in the DOM, so there's nothing to un-hide by tampering with client-side state or dev tools; the actual authorization decision still always happens server-side (`[RolePermission]`, see [Request Pipeline](/backend/request-pipeline)) — this directive is UX only, keeping buttons the user can't use out of the way.

## `hasPerm`: fail-open, fail-closed, or exact match

Both `v-auth` and any render-function button call into the same getter, so the display rule lives in exactly one place:

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

Three states:

1. **Super admin (`isSuperAdmin`) → fail-open.** Everything is shown, mirroring the backend's `sadm` claim bypass in `[RolePermission]`.
2. **Permissions not yet loaded (`permissionsLoaded === false`) → fail-closed.** Every gated button is hidden. This isn't a corner case to shrug off — it's the state every page is briefly in right after login, before `GET /personal/permissions` resolves. If the code instead treated "not loaded" as "allowed," every button (including ones the user has no rights to) would flash visible for a moment and then vanish once permissions arrived. Fail-closed means the user only ever sees buttons they can use — no flash of forbidden UI.
3. **Loaded, regular user → exact match against `permissionCodes`.** An empty `permissionCodes` set (a user with no granted permissions) can never match anything, so everything gated stays hidden. This also closes an older bug class where an empty set was ever conflated with "superadmin" — the two are tracked by separate fields (`isSuperAdmin` vs `permissionCodes`) precisely so an empty permission set can't accidentally unlock everything.

## Buttons built with render functions

`v-auth` is template syntax — it doesn't apply inside `h()` calls, e.g. a table column rendered programmatically. For those cases, call `authStore.hasPerm(code)` directly and branch on the result; it's the same underlying rule, just invoked imperatively:

```ts
const columns: DataTableColumns<SampleDoc> = [
  // ...
  {
    title: () => t('common.operation'),
    key: 'op',
    render: (r) =>
      h(NSpace, { size: 4 }, () => [
        authStore.hasPerm('PUT:/api/v1/sample/doc/{id}')
          ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sample/doc/{id}')
          ? h(NButton, { type: 'error' }, () => t('common.delete'))
          : null,
      ]),
  },
]
```

## Permission-code convention

A permission code is just a normalized route — `{METHOD}:/{route template}` (e.g. `GET:/api/v1/ping`) — with no separate string vocabulary to keep in sync. The frontend fetches the current user's code set once, after login, via `personalApi.permissions` (`GET /personal/permissions`), and stores it in `authStore.permissionCodes`; the superadmin flag comes from the profile endpoint into `authStore.isSuperAdmin`. Since the code *is* the route, the frontend never invents its own permission vocabulary — see [Request Pipeline](/backend/request-pipeline) for how the backend computes and enforces the same code server-side, and [Replaceability Model](/backend/replaceability) for how the authorization pieces around it can be swapped.

## Where to next

- [Frontend Routing](/frontend/routing)
- [Request Pipeline](/backend/request-pipeline)
- [Multi-Org Data Scope](/backend/data-scope)
