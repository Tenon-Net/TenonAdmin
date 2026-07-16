# ProTable

> `tenon-naive-pro-table` — a column-driven Pro Table for **Vue 3 + Naive UI**.

A single `columns` array drives the **search form, dict-based cell rendering, and the column settings panel**; a single `fetcher` function adapts to any backend. Every capability is progressively ignorable — features you don't use stay out of your way. Zero runtime dependencies, peers are only `vue ^3.3` + `naive-ui ^2.34`, ESM only.

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/tenon-naive-pro-table"><img src="https://img.shields.io/npm/v/tenon-naive-pro-table?color=cb3837&logo=npm" alt="npm"></a>
  <a href="https://github.com/Tenon-Net/tenon-naive-pro-table"><img src="https://img.shields.io/github/stars/Tenon-Net/tenon-naive-pro-table?logo=github" alt="GitHub"></a>
</div>

## Features

- **Columns drive everything** — declare `search` on a column to turn it into a search field; declare `options` and the same dict powers both cell translation and the search dropdown.
- **Single-function backend contract** — `(params: {page, pageSize, ...searchFields}) => Promise<{items, total}>`. Any request/response shape is mapped inside the fetcher; no other configuration needed.
- **Column settings + persistence** — show/hide, drag-to-reorder, left/right pinning; persisted to localStorage under `storage-key`, merged safely as column definitions evolve.
- **Density toggle** — comfortable/compact, follows the host's Naive theme (zero CSS shipped in the package).
- **Race-condition guard** — stale out-of-order responses from rapid page-flipping are dropped automatically.
- **Locale-reactive copy** — column titles/labels accept `() => string`, updating instantly on language switch.
- **Declarative formatting** — `format: 'date' | 'datetime' | 'money'` or a custom function; **async dicts** via `options: () => Promise<...>` with loading state and de-duplication.
- **`useProTable` / `useTableCrud`** — a UI-agnostic data core and an optional CRUD-dialog state machine, exported independently.

## Installation

```bash
npm i tenon-naive-pro-table
```

## Quick Start

```vue
<script setup lang="ts">
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'

interface User { id: number; account: string; name: string; enabled: boolean; createTime: string }

const columns: ProTableColumn<User>[] = [
  { type: 'index' },
  { key: 'account', title: 'Account', search: true },
  { key: 'name', title: 'Name', search: true },
  {
    key: 'enabled', title: 'Status', tag: true, search: true,
    options: [
      { label: 'Enabled', value: true, tagType: 'success' },
      { label: 'Disabled', value: false },
    ],
  },
  { key: 'createTime', title: 'Created At', format: 'datetime' },
]

// The one and only backend contract — adapt any API here
async function fetcher({ page, pageSize, ...query }) {
  const res = await fetch(`/api/users?page=${page}&size=${pageSize}`).then(r => r.json())
  return { items: res.list, total: res.total }
}
</script>

<template>
  <ProTable :columns="columns" :fetcher="fetcher" storage-key="users" />
</template>
```

Must be rendered inside the host's `<n-config-provider>` — the table follows its theme, locale, and density.

## Column Definition

Data columns inherit Naive UI column properties (`width`, `fixed`, `align`, `ellipsis`, `sorter`, etc. are all passed through), plus:

| Field | Description |
|---|---|
| `key` | Row field name; also the default search-param key, the column-settings id, and the `#cell-{key}` slot name. Required. |
| `title` | `string \| () => VNodeChild`. The function form is evaluated at render time — language switches take effect instantly. |
| `render` | `(row, rowIndex) => VNodeChild`, custom cell content, highest priority. |
| `format` | `'date' \| 'datetime' \| 'money' \| (value, row) => string`, declarative formatting. |
| `options` | Dict: declared once, used both for cell translation and the search dropdown. |
| `tag` | Renders the translated value as an `NTag` (type taken from the matched item's `tagType`). |
| `search` | `boolean \| SearchConfig`. `true` = select if `options` is present, otherwise an input. |
| `hide` / `hideInTable` / `hideInSetting` | Initially hidden / search-only field / excluded from the column-settings panel (typical for the actions column). |

Special columns: `{ type: 'selection' | 'expand' | 'index' }`; an actions column = a normal column + `render` + `fixed: 'right'` + `hideInSetting: true`.

Cell rendering priority: `render` → `#cell-{key}` slot → `options` translation (+`tag`) → `format` → raw value.

## Core Props

| Prop | Description |
|---|---|
| `columns` | Required |
| `fetcher` | `(params) => Promise<{items, total}>`, remote mode |
| `data` | Static mode (client-side pagination) |
| `search` | `false` hides the search card; `layout: 'inline'` renders a single row with no card, for narrow columns/master-detail layouts |
| `storage-key` | Enables localStorage persistence for column settings + density |
| `labels` | Pass a `computed` to react to language switches |
| `active-row-key` | Pairs with `@row-click` for master-detail selection highlighting |
| `row-draggable` / `drag-handle` | Row drag-to-reorder (sortablejs lazy-loaded); persisting the order is the host's responsibility |

All other attributes (`striped`, `max-height`, `virtual-scroll`, `checked-row-keys`, etc.) are passed through to `n-data-table` as-is.

**Events**: `search`, `reset`, `loaded`, `error`, `row-click`, `row-drag-sort` — the component never shows its own toasts; the host handles that in `@error`.

## Global Defaults (provide/inject)

`provide` once at the host's root component and every ProTable inherits it; priority is always **instance prop / explicit column value > global default > built-in fallback**.

```ts
// main.ts
import { createProTableDefaults, PRO_TABLE_DEFAULTS } from 'tenon-naive-pro-table'

app.provide(PRO_TABLE_DEFAULTS, createProTableDefaults({
  align: 'left',
  pageSizes: [10, 20, 50, 100],
  labels: computed(() => ({ search: t('common.search'), reset: t('common.reset') })),
}))
```

## More Capabilities

Tree/expandable rows, server-side sorting (`sorter: true`), collapsible search, virtual scroll, summary rows, cell merging, cross-page selection retention, and more — all available directly via attrs / column pass-through.

> For the full API, behavioral conventions, and integration details within TenonAdmin, see the [package README](https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md) and [tenon COMPONENTS.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md).
