# Frontend Permissions

A button the user has no right to never renders. React has no directive system, so Vue's `v-auth` becomes a component here, `<Can>`: it renders its children on a matching permission code and returns `null` otherwise, so the button never enters the virtual DOM. How this gate is built comes down to two things — where the authorization state lives, and how the decision is computed.

## The big picture: two stores, split by what persists

- **`user` store** (`src/stores/user.ts`) — `accessToken`, `refreshToken`, `userInfo` (`userId`, `account`, `name`, `avatar`, `mustChangePassword`), plus the `isLoggedIn` selector and the `setSession`/`clear` actions. Persisted to `localStorage` through zustand's persist, with a `partialize` allowlist holding just those three keys — equivalent to Vue's `persist: true`. That's what keeps you logged in across a refresh.
- **`auth` store** (`src/stores/auth.ts`) — `modules`, `currentModuleId`, `defaultModuleId`, `menuTree`, `permissionCodes`, `permissionsLoaded`, `isSuperAdmin`, `routesReady`. The decision runs through the `hasPerm` pure function and the `useHasPerm` hook, not a Vue-style store getter. Its persist `partialize` keeps only `currentModuleId`.

The split exists because the two sides have different lifetimes. Tokens and profile have to survive a refresh, or "stay logged in" means nothing. Permission codes, the menu tree, and `routesReady` are the opposite: re-fetched on every app boot. `routesReady` especially must not be persisted — dynamic routes live only in the router's memory, and once it's stored as `true`, a refresh skips the route rebuild and every dynamic route 404s. `currentModuleId` is the one exception; persisting it lets an F5 or a deep link first restore "which app you were last in," with the guard re-fetching everything else via `useModule().enterInitial()` (see [Portal and guards](/frontend-react/portal-guards)).

## `<Can>`: no directive, gate with a component

Gating in React is a component, not a directive. There's no equivalent to `v-auth`, the "attach to an element, run once on mount" mechanism; instead you pass the content to protect as `children` into `<Can>`:

```tsx
// web-react/src/components/Can.tsx
export function Can({ code, every = false, children }: { code: string | string[]; every?: boolean; children: ReactNode }) {
  const has = useHasPerm()
  const codes = Array.isArray(code) ? code : [code]
  const ok = every ? codes.every(has) : codes.some(has)
  return ok ? <>{children}</> : null
}
```

A single string is the common form; it also takes an array of codes: OR by default (`some`, show if any matches), and AND with the `every` prop (show only if all match). The real user-management page (`web-react/src/views/system/user/index.tsx`) writes its toolbar like this:

```tsx
<Can code="POST:/api/v1/sys/user">
  <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
</Can>
<Can code="POST:/api/v1/sys/user/batch-delete">
  <Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button>
</Can>
```

On a miss `<Can>` returns `null`, React never renders the subtree, and the button never enters the DOM. The effect matches Vue's `v-auth` physical DOM removal: a button without permission isn't on the page, and can't be "conjured up" by tampering with client state or dev tools. The mechanism differs — Vue mounts the node and then calls `el.remove()`, while React simply never creates it.

One behavioral difference is worth naming. `v-auth` implements only a `mounted` hook, so a permission change after mount is never re-evaluated; `<Can>` subscribes to the store through `useHasPerm()`, so a change in permission codes re-renders it and flips visibility accordingly. A normal login fetches permissions once up front, so this rarely comes up in practice, but it's a real distinction.

Don't mistake `<Can>` for a security boundary. The real authorization decision always happens server-side; the backend's `[RolePermission]` filter is the authority. This component is UX only, keeping buttons the user can't use out of sight.

## `hasPerm`: super admin passes / not-loaded hides / exact match

`<Can>` and the buttons that check permissions imperatively in operation columns go through the same decision, so the show/hide rule collapses into one place:

```ts
export function hasPerm(
  s: Pick<AuthState, 'isSuperAdmin' | 'permissionsLoaded' | 'permissionCodes'>,
  code: string,
): boolean {
  return s.isSuperAdmin ? true : s.permissionsLoaded && s.permissionCodes.includes(code)
}
```

It's a pure function rather than a selector returning a closure, and zustand forces that. A zustand selector runs on every render, and its result is compared with the previous one via `Object.is`. A selector returning a freshly built function reads as "changed" every time, which loops into infinite re-rendering. So the decision lives in this pure function; reactive use inside components goes through `useHasPerm()`, and outside components you call `hasPerm(useAuthStore.getState(), code)` directly.

`useHasPerm()` subscribes to three fine-grained fields (`isSuperAdmin`, `permissionsLoaded`, `permissionCodes`), not the whole store. Otherwise any unrelated field like `menuTree` or `routesReady` changing would re-render every permission-gated button on the page.

Three states:

1. **Super admin (`isSuperAdmin`) → fail-open.** Everything is shown, echoing the `sadm`-claim bypass in the backend's `[RolePermission]`.
2. **Permission codes not loaded yet (`permissionsLoaded === false`) → fail-closed.** Every gated button is hidden first. This isn't a corner case to wave off. The guard `await`s `enterInitial`, so a normal login never hits a flicker window with permissions missing. What fail-closed really guards against is `/personal/permissions` failing to fetch. When you don't actually know whether a user has a permission, wrongly saying "yes" is far worse than wrongly saying "no." Treating "not loaded" as "has permission" would flash every gated button into view and then vanish it, including the ones the user has no right to.
3. **Loaded regular user → exact match against `permissionCodes`.** An empty `permissionCodes` matches no code, so gated buttons all stay hidden. Super admin and an empty permission set are carried by two independent fields (`isSuperAdmin` and `permissionCodes`), so an empty set can never be mistaken for super admin and accidentally unlock everything.

## Spreading the gate across every action button

Wrapping toolbar buttons in `<Can>` is enough, but a row's inline action buttons mostly don't go through `<Can>`; they call the predicate from `useHasPerm()` directly. The reason is that operation columns often stack a check on top of the permission code — a super-admin row gets no delete, no disable (self-lockout protection) — and folding that judgment together with the code into one predicate reads cleaner than nesting two conditions in JSX.

Grab the predicate once at the top of the component:

```ts
const has = useHasPerm()
```

The user page extracts these predicates into `userForm.ts` so they can be pinned by unit tests:

```ts
// web-react/src/views/system/user/userForm.ts
export const canEdit = (_r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  has('PUT:/api/v1/sys/user/{id}')

// Delete: never on a super-admin row (self-lockout protection), on top of the delete code.
export const canDelete = (r: { isSuperAdmin: boolean }, has: (c: string) => boolean) =>
  !r.isSuperAdmin && has('DELETE:/api/v1/sys/user/{id}')
```

The column's render only asks "can I," emitting the button on a hit:

```tsx
// web-react/src/views/system/user/index.tsx — operations column
render: (_, r) => (
  <Space size={4}>
    {canEdit(r, has) && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
    {canReset(r, has) && <Button type="link" size="small" onClick={() => openReset(r)}>{t('user.resetPassword')}</Button>}
    {canDelete(r, has) && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
  </Space>
),
```

When there are too many actions for one row (the org page collapses everything but edit into "More ▾"), each dropdown option is filtered by code too, and if none survive the dropdown isn't rendered:

```tsx
// web-react/src/views/system/org/index.tsx — operations column
const moreItems = ([
  has('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
  has('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
  has('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete'), danger: true } : null,
] as MenuProps['items'])!.filter(Boolean)
// ...
{moreItems!.length > 0 && (
  <Dropdown menu={{ items: moreItems, onClick: onMore }} trigger={['click']}>
    <Button type="link" size="small">{t('common.more')}</Button>
  </Dropdown>
)}
```

Not every gate is a hide. A button like an enable/disable switch is better disabled than hidden: hide it and the user assumes the feature doesn't exist, disable it and you're telling them "there's a switch here, you just can't work it." So the status column's `StatusSwitch` wires the permission into `disabled`, with the same combined predicate behind it (no disable on a super-admin row, on top of the enable/disable code):

```tsx
// web-react/src/views/system/user/index.tsx — status column
<StatusSwitch
  value={r.enabled}
  disabled={!canToggleEnabled(r, has)}
  request={(next) => userApi.setEnabled(r.id, next)}
  onChange={reload}
/>
```

## The permission-code convention

A permission code is the normalized route itself — `{METHOD}:/{route template}` (e.g. `GET:/api/v1/ping`) — with no separate string vocabulary to keep in sync. When the frontend logs into the portal, `useModule().enterInitial()` fires two requests in parallel: `GET /personal/permissions` fetches the current user's code set (stored in `authStore.permissionCodes`), and `GET /personal/profile` fetches the super-admin flag (stored in `authStore.isSuperAdmin`). Only when both succeed does `permissionsLoaded` go true; if either fails, the user is treated as ordinary and fail-closed, never erring toward over-permission.

Since the permission code *is* the route, the frontend has no reason to invent its own permission vocabulary, and the division between the two ends is clean: the frontend only decides button show/hide and disable by code, while the backend computes and enforces that same code. How it normalizes a route into a permission code, and how `[RolePermission]` validates the session and the grant, is in the [Request Pipeline](/backend/request-pipeline). The design around swapping the authorization step — a different permission computation, a different session check — is in the [Replaceability Model](/backend/replaceability).
