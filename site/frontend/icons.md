# Icons

TenonAdmin renders icons **offline**: a handful of Iconify icon sets plus your own local SVGs are registered once at startup, then consumed anywhere through a thin `AppIcon` wrapper, and picked interactively via the `IconPicker` component in menu administration.

## Registering icon sets

`setupIcons()` (`src/lib/icons.ts`) is called once from `main.ts` and registers, via `tenon-naive-iconify-picker`'s `setupIconPicker`:

- **Offline Iconify collections** — `ph` (Phosphor, the default set, preheated on startup), `lucide` (Lucide), `ep` (Element Plus), `ant-design` (Ant Design). Each is a separate lazy `@iconify-json/<prefix>` chunk, loaded only when that set is first needed.
- **Local SVGs** — everything under `src/assets/svg/*.svg`, glob-imported as raw strings:

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

  A file like `src/assets/svg/star.svg` becomes selectable as `local:star`.

Because both the Iconify data and the SVGs are bundled into the app itself, icon rendering never calls out to an external CDN (e.g. `api.iconify.design`) — it works the same in an air-gapped or otherwise network-restricted deployment as it does online.

## Using an icon in a component

`AppIcon` (`src/components/AppIcon.vue`) wraps the package's `OfflineIcon` and is the standard way to render an icon anywhere in the app:

```vue
<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
</script>

<template>
  <AppIcon icon="ph:house-duotone" />
  <AppIcon icon="local:star" :size="20" />
</template>
```

The `icon` value is a `prefix:name` string (or `local:name` for a registered local SVG). Size defaults to `18`. If `icon` is empty or the name can't be resolved, `AppIcon` falls back to `ph:dot-outline-duotone` — the same fallback the sidebar menu uses for rail/collapsed items without an assigned icon.

## Picking an icon in menu admin

`IconPicker` (`src/components/IconPicker/index.vue`) is the app's picker widget, used for the menu `icon` field in **System → Menu** administration. It wraps the npm package's `IconPicker`, supplies tenon's own vue-i18n labels, and reuses the collections already registered by `setupIcons()` (so `ph` shows up as the first/default tab) — no extra configuration needed at the call site.

::: tip Full picker API
This page only covers how icons are wired into the app. For the picker component's complete API — multi-library tabs, registering local SVGs, `labels`/i18n, `v-model` contract — see [IconPicker](/components/icon-picker).
:::

## Where to next

- [Theme](/frontend/theme)
- [IconPicker component](/components/icon-picker)
