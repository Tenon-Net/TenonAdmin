# IconPicker

> `tenon-naive-iconify-picker` — an offline-first icon picker for **Vue 3 + Naive UI**, built on [Iconify](https://iconify.design).

Register as many icon libraries as you like (one tab per library), browse **with zero network requests**, inject your own SVGs, and the selected result is just a string — a great fit for fields like "pick an icon for this menu item."

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/tenon-naive-iconify-picker"><img src="https://img.shields.io/npm/v/tenon-naive-iconify-picker?color=cb3837&logo=npm" alt="npm"></a>
  <a href="https://github.com/Tenon-Net/tenon-naive-iconify-picker"><img src="https://img.shields.io/github/stars/Tenon-Net/tenon-naive-iconify-picker?logo=github" alt="GitHub"></a>
</div>

## Features

- 🧭 **Multiple icon libraries** — register as many as you like (Lucide, Ant Design, Element Plus, Phosphor…), one tab per library.
- 🔌 **Offline** — registered libraries render from local data and never call `api.iconify.design`.
- 📦 **Zero config** — ships with Lucide built in; import and go.
- 🎨 **Theme-aware** — borders, text, hover, primary color, and border-radius all follow the host's Naive theme via `useThemeVars()`, no CSS variables to wire up.
- 🖼️ **Local SVGs** — register your own project's SVGs, selectable as `local:<name>`.
- 🌐 **Online fallback** — type any Iconify name (e.g. `mdi:home`) to load it online when connected.
- 🌍 **i18n-ready** — all copy comes from a single `labels` prop.
- 🧩 **Single-string value** — `v-model` is just a `prefix:name` (or `local:name`) string, stored directly in a database field.

## Prerequisites

This is a **component for use inside an existing app**: it doesn't bundle Vue or Naive UI, so provide them as peer dependencies: `vue ^3.3`, `naive-ui ^2.34`, `@iconify/vue ^4 || ^5`. Browser-only (uses `navigator.onLine` and `v-html`).

## Installation

```bash
npm i tenon-naive-iconify-picker
```

Styles are **injected automatically** by the component — no separate CSS import needed.

## Quick Start

Zero config: the built-in **Lucide** library is registered automatically.

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { IconPicker, OfflineIcon } from 'tenon-naive-iconify-picker'

const icon = ref('lucide:rocket')
</script>

<template>
  <!-- Must be inside the app's <n-config-provider> so the theme can resolve -->
  <IconPicker v-model="icon" />

  <!-- Render a stored value anywhere — also offline, also supports local: -->
  <OfflineIcon :icon="icon" :size="18" />
</template>
```

`v-model` stores a single string, such as `lucide:rocket` or `local:star`. That's the entire contract.

## Registering More Icon Libraries

Each library you register becomes a **tab**. Icon data is **not bundled** into this component: each library is a lazy-loaded chunk in **your own** build, loaded only the first time that tab is opened (Lucide ≈ 85 KB gz; Phosphor ≈ 946 KB gz).

```bash
npm i @iconify-json/ant-design @iconify-json/ep @iconify-json/ph
```

```ts
// main.ts
import { setupIconPicker, lucideCollection } from 'tenon-naive-iconify-picker'

setupIconPicker({
  collections: [
    lucideCollection, // built into this package
    { prefix: 'ant-design', name: 'Ant Design',  loader: () => import('@iconify-json/ant-design/icons.json').then(m => m.default) },
    { prefix: 'ep',         name: 'Element Plus', loader: () => import('@iconify-json/ep/icons.json').then(m => m.default) },
    { prefix: 'ph',         name: 'Phosphor',     loader: () => import('@iconify-json/ph/icons.json').then(m => m.default) },
  ],
})
```

Stored values carry the library prefix (`ant-design:home-outlined`), so `<OfflineIcon>` can always render them correctly. Browse all available libraries at [icon-sets.iconify.design](https://icon-sets.iconify.design).

## Local SVGs

**Vite** — use a glob import to read an SVG directory as raw strings; the filename (minus `.svg`) becomes the icon name:

```ts
import { registerLocalIcons } from 'tenon-naive-iconify-picker'

registerLocalIcons(
  import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
)
// star.svg  ->  local:star
```

## Internationalization

The component contains **no i18n framework** of its own. All visible copy comes from a single `labels` object (English by default); override any subset via the `labels` prop. When integrating with vue-i18n, pass `computed(() => ({ ... }))` to react to language switches.

```vue
<IconPicker v-model="icon" :labels="{ placeholder: 'Choose an icon', title: 'Icon' }" />
```

Label keys: `placeholder` / `title` / `search` / `local` / `online` / `onlinePlaceholder` / `use` / `offlineHint` / `loading` / `empty` / `more` (supports the `{n}` placeholder).

> For the full props list (`collections` / `localIcons` / `cap` / `clearable`, etc.), the `OfflineIcon` API, and SSR/Nuxt notes, see the [package README](https://github.com/Tenon-Net/tenon-naive-iconify-picker/blob/main/README.zh-CN.md).
