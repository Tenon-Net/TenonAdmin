# Theme & Design Tokens

TenonAdmin's look is driven by CSS custom properties, not component props. `web/src/styles/tokens.css` defines a four-layer token system — primitives (gray scale, brand color, semantic base colors), **role tokens** (`--color-bg-*`, `--color-text-*`, `--color-border*`, `--color-fill*`, `--color-primary*`, business code consumes only this layer), theme-independent metrics (font size, spacing, radius), and shadows. Light values live on `:root`; dark overrides live on `:root[data-theme="dark"]`.

At runtime, `useTheme()` (`web/src/composables/useTheme.ts`) watches the app store's appearance prefs and applies them to `<html>`: it sets `data-theme`/`data-density`/`data-gray` attributes, derives `--color-primary*` from the chosen accent color, and rebuilds Naive UI's theme object. That rebuild happens in `buildThemeOverrides()` (`web/src/theme/naive-theme.ts`), which reads the very same CSS variables via `getComputedStyle` and maps them onto Naive's `GlobalThemeOverrides`. Because both hand-written CSS and Naive UI components read from the same token values, they never drift apart. `App.vue` wires the result into `<n-config-provider :theme-overrides="overrides">`, which wraps the whole app.

## Dark, light, auto

The `app` store's `themeScheme` has three states: `'light'`, `'dark'`, `'auto'` (the default). In `auto` mode, the `isDark` getter follows the OS preference via VueUse's `usePreferredDark`, reactively — so the app matches the system theme on first visit and live-updates if the OS theme changes. Calling `toggleDark()` (the header's theme switch) or `setThemeScheme()` sets an explicit `'light'`/`'dark'` value, which persists to `localStorage` and from then on overrides the OS preference.

## Accent & density

Two more user-facing knobs live in the same `app` store: `accent` (the brand color, used to derive `--color-primary*`) and `density` (`'comfortable'` / `'compact'`, reflected as `data-density` on `<html>`). Both are persisted alongside `themeScheme`.

::: tip Full token reference
This page is an orientation, not a spec. For the complete token tables and the Naive UI mapping spec, see [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md) on GitHub.
:::

## Where to next

- [Icons](/frontend/appearance)
- [Frontend Structure](/frontend/structure)


---

<!-- TODO(rewrite): merged from icons.md -->

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

- [Theme](/frontend/appearance)
- [IconPicker component](/components/icon-picker)
