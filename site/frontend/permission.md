# Frontend Permissions

An action button stays hidden when the user lacks its permission, so the UI never starts a flow that must end in 403. Template buttons, row actions, and status switches all need to apply the same decision, even though they use different Vue APIs.

> Prerequisite: complete the [regular-role grant in the tutorial](/frontend/getting-started#_5-grant-access-to-a-regular-role).

## Gate the Refresh button first

Change the tutorial's Refresh button to:

```vue
<n-button v-auth="'GET:/api/v1/sys/position/page'" :loading="loading" @click="load">
  Refresh
</n-button>
```

The button appears for an account with the position query permission and disappears after that permission is removed and the account signs in again. A direct request must still receive 403. `v-auth` avoids a guaranteed failure in the UI, while the server rejects unauthorized access.

[![Check both the menu and endpoint permission in the role tree; the UI is Chinese](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

## The big picture: two stores, split by what persists

- **`user` store** (`src/stores/user.ts`) — holds tokens, session mode, and profile data. Body sessions persist access/refresh tokens and profile data. Cookie sessions persist only the `cookieSession` marker: access stays in memory, refresh stays in an HttpOnly cookie, and neither token enters Web Storage.
- **`auth` store** (`src/stores/auth.ts`) — `modules`, `currentModuleId`, `defaultModuleId`, `menuTree`, `permissionCodes`, `permissionsLoaded`, `isSuperAdmin`, `routesReady`, plus the `homePath` and `hasPerm` getters. Declared `persist: { pick: ['currentModuleId'] }` — **only `currentModuleId` is persisted**.

The two stores have different lifetimes. A body session survives reload through persisted tokens; a cookie session keeps only its marker and silently obtains a new access token from the HttpOnly refresh cookie. Permission codes, the menu tree, and `routesReady` reload on every boot because dynamic routes exist only in router memory. Persisting `routesReady: true` would skip reconstruction and send dynamic pages to 404. `currentModuleId` alone survives so the app can restore the previous module before `enterInitial()` fetches the remaining authorization state.

## `v-auth`: the button-level directive in templates

Registered globally in `main.ts`:

```ts
app.directive('auth', vAuth)
```

The toolbar buttons on a page are where it earns its keep. The real user-management page (`web/src/views/system/user/index.vue`) writes its toolbar like this:

```vue
<template #toolbar>
  <n-button v-auth="'POST:/api/v1/sys/user'" type="primary" @click="openAdd">
    {{ t('common.add') }}
  </n-button>
  <n-button
    v-auth="'POST:/api/v1/sys/user/batch-delete'"
    type="error"
    :disabled="!hasSelection"
    @click="batchDelete"
  >
    {{ t('common.batchDelete') }}
  </n-button>
</template>
```

The directive value is the permission code for the endpoint behind that button. Besides a single string, it also accepts an array of codes (OR by default: show if any one matches) and an array with the `.and` modifier (AND: show only if all match):

```vue
<n-button v-auth="['a', 'b']">shown if a OR b matches</n-button>
<n-button v-auth.and="['a', 'b']">shown only if a AND b match</n-button>
```

On mount, the directive creates a `watchEffect` that observes `authStore.hasPerm`. It reevaluates when permissions finish loading or change, and disposes the subscription on unmount:

```ts
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const stop = watchEffect(() => {
      const ok = Array.isArray(need) ? need[mode]((c) => auth.hasPerm(c)) : auth.hasPerm(need)
      el.style.display = ok ? '' : 'none'
    })
    stopHandles.set(el, stop)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
```

The directive uses `display: none` so the node can become visible when permission state changes. The node remains in the DOM, so it cannot be a security boundary. Real authorization always runs on the server in the `[RolePermission]` filter; the directive controls presentation only.

## `hasPerm`: super admin passes / not-loaded hides / exact match

The `v-auth` directive and the buttons in render functions go through the same getter, so the show/hide rule is collapsed into a single place:

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

Three states:

1. **Super admin (`isSuperAdmin`) → fail-open.** Everything is shown, echoing the `sadm`-claim bypass in the backend's `[RolePermission]`.
2. **Permission codes not loaded yet (`permissionsLoaded === false`) → fail-closed.** Every gated button is hidden. This isn't a corner case to wave off. The guard `await`s `enterInitial`, so a normal login never actually passes through a flicker window where permissions aren't in yet — what fail-closed really guards against is `/personal/permissions` failing to fetch at all. When you genuinely don't know whether a user has a permission, wrongly saying "yes" is far worse than wrongly saying "no." If "not loaded" were treated as "has permission," every gated button (including the ones the user has no rights to) would flash into view and then vanish. Fail-closed guarantees the user only ever sees buttons they can use — no flash of forbidden UI.
3. **Loaded regular user → exact match against `permissionCodes`.** An empty set means no grants and cannot match a permission code. Super-admin status lives in the separate `isSuperAdmin` field and must never be inferred from whether the permission set is empty.

## Spreading the gate across every action button

`v-auth` is template syntax and works only inside `<template>`. Inline row actions such as edit, delete, copy, reset password, force logout, and restore are assembled with `h()` in a column `render` function, outside the directive's reach. Those render functions call `authStore.hasPerm(code)` directly: a match creates the button and a miss returns `null`. The API differs, but both paths use the same store decision.

The user-management page's operations column is the canonical form:

```ts
// web/src/views/system/user/index.vue — operations column
render: (r) =>
  h(NSpace, { size: 4 }, () => [
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}')
      ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
      : null,
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}/password')
      ? h(NButton, { onClick: () => openReset(r) }, () => t('user.resetPassword'))
      : null,
    // No delete button on a super-admin row: no permission for it, and it guards against accidents; regular users toggle by the DELETE code
    r.isSuperAdmin || !authStore.hasPerm('DELETE:/api/v1/sys/user/{id}')
      ? null
      : h(NPopconfirm, { onPositiveClick: () => remove(r) }, {
          trigger: () => h(NButton, { type: 'error' }, () => t('common.delete')),
          default: () => t('user.deleteConfirm', { name: r.name }),
        }),
  ]),
```

When there are too many actions for one row (the org page collapses four into "Edit + More ▾"), each option in the dropdown is filtered by code too, and if none survive the dropdown simply isn't rendered:

```ts
// web/src/views/system/org/index.vue — operations column
const dropdownOptions = [
  authStore.hasPerm('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
  authStore.hasPerm('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
  authStore.hasPerm('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete') } : null,
].filter((o) => o !== null)
// ...
dropdownOptions.length ? h(NDropdown, { options: dropdownOptions }) : null
```

Not every gate is a hide. A button like an enable/disable switch is better disabled than hidden — hide it and the user assumes the feature doesn't exist; disable it and you're telling them "there's a switch here, you just can't work it." So the status column's `StatusSwitch` wires the permission into `disabled`:

```ts
// web/src/views/system/user/index.vue — status column
h(StatusSwitch, {
  value: r.enabled,
  // Super admin can't be disabled (prevents self-lockout — disable it and there's no way back from the UI, and the backend protects it too); no enable/disable permission is likewise greyed out
  disabled: r.isSuperAdmin || !authStore.hasPerm('PUT:/api/v1/sys/user/{id}/enabled'),
  request: (next: boolean) => userApi.setEnabled(r.id, next),
})
```

## The permission-code convention

A permission code is the normalized route itself — `{METHOD}:/{route template}` (for example, `GET:/api/v1/ping`) — so there is no second vocabulary to synchronize. On portal entry, `useModule().enterInitial()` runs two requests in parallel. `GET /personal/permissions` fetches the code set and sets `permissionsLoaded` only on success; a failure leaves it false, so ordinary permission gates fail closed. `GET /personal/profile` supplies the super-admin flag. If that request fails, the store retains the user snapshot from login, or treats the account as ordinary when no snapshot exists.

Since the permission code *is* the route, the frontend has no reason to invent its own permission vocabulary. This also draws the line between the two ends cleanly: the frontend only decides button show/hide and disable by code, while the backend computes and enforces that same code — how it normalizes a route into a permission code, and how `[RolePermission]` validates the session and the grant, is in the [Request Pipeline](/backend/request-pipeline); the design around swapping the authorization step (a different permission computation, a different session check) is in the [Replaceability Model](/backend/replaceability).

## Verify with two accounts

Use one role that has the target permission and one that does not. The first should see the action and complete it; the second should not see ordinary action buttons, while protected status switches should remain disabled. Then call the same backend endpoint directly and confirm that the unprivileged account still receives 403. The UI check finds missing gates, while the direct request proves that enforcement remains on the server.
