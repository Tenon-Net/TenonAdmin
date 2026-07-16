# ProTable

Nearly every list page in the TenonAdmin frontend is the same table: `tenon-naive-pro-table` (a standalone npm package, `^0.3.1`). Its model has just two pieces — one `columns` array that drives the search form, the dict cells, and the column-settings panel all at once, and one `fetcher` function that wires in any backend. This page covers exactly one thing: how to use it correctly inside the TenonAdmin template. The full, prop-by-prop reference lives in the package README and isn't repeated here.

## The bare minimum for a list page

Take position management (`web/src/views/system/position/index.vue`) as the skeleton, strip it down to nothing but the table, and it looks like this:

```vue
<script setup lang="ts">
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { positionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysPosition } from '@/types/api'

const columns: ProTableColumn<SysPosition>[] = [
  { key: 'name', title: () => t('position.name'), search: true },
  { key: 'code', title: () => t('position.code') },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
]
</script>

<template>
  <ProTable
    :columns="columns"
    :fetcher="positionApi.page"
    storage-key="sys-position"
    @error="(e) => message.error(translateError(e))"
  />
</template>
```

The `name` column has `search: true`, so it automatically becomes an input in the search form; `createTime` has `format: 'datetime'`, so it's formatted in local time. Whatever you don't declare, you can ignore — the table won't invent behavior for you. Four conventions hide in that short snippet; the rest of this page unpacks them one by one.

## The fetcher is the only thing you have to adapt

ProTable makes exactly one assumption about the backend: `fetcher(params) => Promise<{ items, total }>`. TenonAdmin's backend doesn't return that shape — it returns a `PagedList<T>` (`{ current, size, total, items }`), and its paging params are `Current`/`Size`, not `page`/`pageSize`. That mismatch shouldn't be scattered across every page, so the template pins it down in one place, `web/src/api/index.ts`:

```ts
// frontend {page,pageSize} → backend record props {Current,Size} (PascalCase)
const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

// backend PagedList<T> → ProTable's {items,total} contract
function toPage<T>(res): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}

export const positionApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        // search key `name` → backend's PascalCase `Name`; the type wants Pascal, the binding itself is case-insensitive
        params: { query: { ...pageParams(params), Name: params.name } },
      })
      .then((r) => toPage<SysPosition>(r)),
}
```

So in the page, `:fetcher="positionApi.page"` just hands the API-layer method straight in — the mapping is already done there. When you add a new list page, copy an existing `userApi.page`/`positionApi.page` from `api/index.ts`, change the endpoint and the search field names — don't hand-assemble `Current`/`Size` inside the component.

A search column's `key` is the parameter name it searches by (the `name` column arrives in the `fetcher` as `name`); which backend field that lands on is decided by the `Name: params.name` step in the API layer.

## Three things the template already wires up for you

**Labels are injected globally — pages no longer pass them by hand.** ProTable's button copy — search / reset / refresh / column settings — needs to follow i18n, and the template hooks it up once in `web/src/main.ts` so every page inherits it afterward:

```ts
app.provide(
  PRO_TABLE_DEFAULTS,
  createProTableDefaults({
    labels: computed(() => {
      void i18n.global.locale.value // touch locale so it's tracked as a dependency — switching language recomputes instantly
      const t = i18n.global.t
      return { search: t('common.search'), reset: t('common.reset'), /* …column settings / density, etc. */ }
    }),
  }),
)
```

The page layer therefore never writes `:labels`; you only pass it to override an individual page's copy. Column titles are the exception — they must be written as a function, `title: () => t('...')`. Writing `title: t('...')` directly evaluates only at the moment the column is built and won't update on a language switch (a lesson learned in commit 308a361).

**Errors stay in the view layer.** The package shows no UI of its own; when the `fetcher` throws, it emits to `@error`, and the page decides how to surface it. Across the app this is written uniformly as `@error="(e) => message.error(translateError(e))"`: `translateError` maps the backend's numeric `ErrorCode` into copy in the current language.

**Action buttons show or hide by permission.** The authorization model is "permission code IS the route," so for each action the page checks `authStore.hasPerm('{METHOD}:/{route}')` once and simply doesn't render the button when the permission is missing:

```ts
// inside the actions-column render, gate each button individually
authStore.hasPerm('PUT:/api/v1/sys/position/{id}')
  ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
  : null
```

The toolbar's add / bulk-delete buttons work the same way via the `v-auth` directive: `v-auth="'POST:/api/v1/sys/position/add'"`. The permission-code string must match the backend route template character for character (including placeholder segments like `{id}`), or it will never match.

`storage-key` decides which localStorage key holds the column settings and density (prefixed `protable:`); name it uniformly as `{module}-{page}`, e.g. `sys-position`, `sys-user`.

## Tree tables: static-data mode, and its four traps

First ask yourself one question: is this a flat, paginated list, or a hierarchical tree like orgs or menus? Flat means the `fetcher` mode above — hand paging and search off to it. A tree is different — a tree has no pagination; you pull the whole thing back at once and lay it out yourself. The org page (`org/index.vue`) and the menu page (`menu/index.vue`) both run in **static-data mode**:

```vue
<ProTable
  :columns="columns"
  :data="visibleTree"
  row-key="id"
  :pagination="false"
  :expanded-row-keys="expandedKeys"
  @update:expanded-row-keys="(keys) => (expandedKeys = keys)"
/>
```

Give the tree column `minWidth: 220` + `fixed: 'left'` so you never lose track of "which row is this" while scrolling sideways; give every text column `ellipsis: { tooltip: true }`, or long paths wrap and leave rows at ragged heights. Keep at most two actions — Edit, plus a "More ▾" `n-dropdown`. Four actions laid flat across 260–300px are bound to wrap, and you can't reach them once you scroll horizontally; both org and menu tripped on exactly this. For a delete inside a dropdown item, use `useConfirm().confirm` (a dialog) — `n-popconfirm` is an inline trigger and won't fit inside a dropdown.

What will actually cost you an afternoon of debugging is the four silent failures below:

**Don't add a permanently-empty column.** Once the menu tree strips out button nodes, only directories and pages remain, and permission codes hang only off buttons — so a "permission code" column is 100% "—". By the same token, keyword filtering runs over the stripped tree, so matching on `n.permission` never hits; to search by permission code you have to look at a node's button children (see `buttonInfoById` in `menu/index.vue`). The menu page just deletes the permission-code column outright.

**You compute the search yourself.** In static `:data` mode ProTable does no client-side filtering — a column's `search` config only renders the search control and emits; it won't filter the data for you. So keyword filtering goes through a `computed` plus `filterTree` from `utils/tree.ts` (a matching node keeps its whole subtree; a non-matching node with a matching descendant is kept as part of the ancestor chain). Put the keyword in an `n-input` in `#toolbar`, not in a column's `search`: a tree table has no pagination, and that search card would waste a whole block of height for nothing.

**Controlled expansion means dropping `default-expand-all`.** Once you pass `:expanded-row-keys`, naive treats it as the source of truth, and an initial `[]` overrides `default-expand-all` straight into "all collapsed." You have to seed "expand all by default" yourself with `expandableIds(tree)`. One more thing: when `data` changes the controlled keys don't follow automatically — recompute them after a search or an app switch, or matching results stay hidden inside collapsed ancestors.

::: warning Reload after an inline status change — don't write onto the row object
When `filterTree` prunes, an ancestor kept "only because a descendant matched" is a shallow copy. In a search state, writing onto the row object (`r.enabled = v`) writes to that copy and never reaches the source tree — the toggle springs back after you click it. So after an inline change, call `load()` to refetch rather than writing back locally. `StatusSwitch` is a pessimistic update (it only emits once the request succeeds), so a single refetch gives you the final state.
:::

## Sorting, master-detail, narrow-column search

These are all ready to copy from the sample pages, one line each:

- **Column sorting** (the `user` page): put `sorter: true` on the column, and clicking the header merges `{ sortField, sortOrder }` into the `fetcher`; the API layer maps them to the backend's `SortField`/`SortOrder` (see `userApi.page`). The backend sorts safely against an entity-column allowlist, ignoring invalid fields and falling back to the default (`PagedListExtensions.OrderBySafe`); the field name is the entity property name, case-insensitive.
- **Master-detail selection** (the `dict` page): `:active-row-key` + `@row-click` for row highlighting; remember `stopPropagation` inside the render of an in-row switch/button, or clicking it bubbles up and fires `@row-click`.
- **Narrow-column search** (the `dict` page): `:search="{ layout: 'inline' }"` is the card-less single-row variant; paired with a column's `search: true`, it fits narrow spaces like a master-detail right pane.

Column-width dragging, virtual scroll, summary rows, cell merging and the like all pass through to the inner `n-data-table` via attrs or column properties — ProTable exposes no extra API for them; any prop the package doesn't intercept is forwarded down as-is.

## Version and local development

Sorting and collapsible search need `^0.3.1`; 0.3.0 has a known issue — row dragging never works in `fetcher` mode (Sortable binds only once in `onMounted`, but with an empty table naive hasn't rendered a tbody at all), so the template pins `^0.3.1`. After you change the backend's sort or paging contract, remember to run `npm run gen:api` to regenerate the schema (the backend must be running).

To develop against the package's source, `NPT_LOCAL=1 npm run dev` links directly to the sibling repo (see `web/vite.config.ts`); the loop is the same as the icon package's: edit source → publish a patch version → bump.

The package's full prop, event, and escape-hatch slot reference is authoritative in the [README](https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md); this page covers only how it's wired into the TenonAdmin template.
