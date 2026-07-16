# Theme & Icons

Reskinning the admin with a new brand color, adding a dark palette, or slotting in a few of your own icons touches fewer places than you'd think. The whole look is driven by one layer of CSS variables, and icons are registered offline once at startup — this page explains those two mechanisms so you know where to change things, and why only there.

## Four token layers; business code touches only the role tokens

TenonAdmin's look is driven by CSS custom properties, not component props. `web/src/styles/tokens.css` splits every variable into four layers:

- **Primitives** — the gray scale (`--color-gray-50…900`), the brand color, and the base colors of the four semantic colors. Fixed values, they don't flip with the theme.
- **Role tokens** — `--color-bg-*`, `--color-text-*`, `--color-border*`, `--color-fill*`, `--color-primary*`, `--color-mask`. Semantically named, with light values on `:root` and dark overrides on `:root[data-theme="dark"]`. **Business code consumes only this layer.**
- **Metrics** — font sizes, spacing, radii. Theme-independent, present once on `:root`.
- **Shadows** — light on `:root`, dark overridden separately (a dark background needs a heavier shadow).

"Business code touches only the role tokens" isn't a rule, it's the lazy path: if some business CSS wrote `--color-gray-900` directly as its text color, the dark theme can't reach it — the gray scale is a fixed primitive, it doesn't move with `data-theme`, so you'd get a line of black text sitting on a dark background. Only the role tokens store a value for each of light and dark, so the one line of CSS `--color-text-primary` is correct under both themes — what flips is the value behind the token, not your styles. Reskinning works the same way: swapping the whole palette only touches the override block in the role-token layer, leaving primitives and metrics untouched.

## Dark, light, auto: follow the system on first visit, remembered after a manual toggle

The `app` store's (`web/src/stores/app.ts`) `themeScheme` has three states: `'light'`, `'dark'`, `'auto'` (the default). In `auto`, the `isDark` getter reads the system preference reactively via VueUse's `usePreferredDark` — matching the system light/dark on first visit, and live-updating when the OS theme changes. Clicking the header's toggle (`toggleDark()`) or calling `setThemeScheme()` lands on an explicit `'light'`/`'dark'`, persisted with the store to `localStorage` (key `app`), and from then on it no longer follows the system.

## Accent and density

Two more user-facing knobs live in the same store, persisted alongside `themeScheme`:

- `accent` — the brand color, chosen from 6 candidates (`web/src/theme/accents.ts`: indigo `#646CFF` by default, plus purple, cyan, pink, orange, green). Changing the accent recomputes `--color-primary*`.
- `density` — `'comfortable'` / `'compact'`, applied as `data-density` on `<html>`, driving table row height and card padding.

## From tokens to Naive UI

Hand-written CSS reads the tokens directly, but Naive UI components don't understand CSS variables — they want a JS object (`GlobalThemeOverrides`). `buildThemeOverrides()` (`web/src/theme/naive-theme.ts`) reads that same batch of CSS variables out with `getComputedStyle` and maps them onto Naive's `common.*` (`primaryColor` ← `--color-primary`, `bodyColor` ← `--color-bg-body`, `borderRadius` ← `--radius-md`, and so on). Both sides read the same values, so hand-written styles and Naive components never drift into different colors.

The accent is the one value that isn't read directly but computed. There's no way to pre-write hover/pressed/light states for all 6 accent candidates in `tokens.css`, so only the one `accent` is stored and the other states are derived by `mix(a, b, t)` (`web/src/theme/mix.ts`, a linear interpolation of two colors by `t∈[0,1]`): in light, `hover = mix(primary, #FFF, .16)` and `pressed = mix(primary, #000, .18)`; in dark, the accent is first lightened one step toward white (`mix(accent, #FFF, .18)`) before deriving the rest, so indigo doesn't come out muddy against a dark background.

It all comes together in `useTheme()` (`web/src/composables/useTheme.ts`): it watches `app.isDark` / `accent` / `density`, and on any change stamps `data-theme` / `data-density` onto `<html>`, writes the derived `--color-primary*` into `document.documentElement` (so token-consuming hand-written CSS reskins instantly), and rebuilds Naive's `themeOverrides`. `App.vue` wires the result into `<n-config-provider :theme-overrides>`, wrapping the whole app.

::: tip The full token tables
The above is enough to change the accent, add a dark palette, and figure out which layer to touch. For the complete token listing, the semantic-badge derivations, and the full `token → Naive` mapping table, see [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md).
:::

## Offline icon registration

Icons are rendered **offline**: a handful of Iconify collections plus your own local SVGs, registered once at startup, then used in any component through the thin `AppIcon` wrapper, and pickable interactively in menu administration via `IconPicker`.

`setupIcons()` (`web/src/lib/icons.ts`) is called exactly once from `main.ts`, and registers two kinds of source through `tenon-naive-iconify-picker`'s `setupIconPicker`:

- **Offline Iconify collections** — `ph` (Phosphor, the default set, warmed on startup), `lucide` (Lucide), `ep` (Element Plus), `ant-design` (Ant Design). Each is a separate lazy `@iconify-json/<prefix>` chunk, loaded only when first used.
- **Local SVGs** — everything under `src/assets/svg/*.svg`, glob-imported as raw strings:

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

  A file like `src/assets/svg/star.svg` becomes selectable as `local:star`.

Both the Iconify data and the SVGs are bundled into the app itself, so icon rendering never reaches out to an external CDN (such as `api.iconify.design`) — in an offline or network-restricted deployment it behaves exactly as it does online.

## Using an icon in a component

`AppIcon` (`web/src/components/AppIcon.vue`) wraps the package's `OfflineIcon` and is the standard way to render an icon anywhere in the app:

```vue
<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
</script>

<template>
  <AppIcon icon="ph:house-duotone" />
  <AppIcon icon="local:star" :size="20" />
</template>
```

`icon` is a `prefix:name` string (`local:name` for a local SVG), default size `18`. When `icon` is empty or can't be resolved, `AppIcon` falls back to `ph:dot-outline-duotone` — the same fallback the sidebar menu uses for an item with no icon in the rail/collapsed state.

## Picking an icon in menu admin

`IconPicker` (`web/src/components/IconPicker/index.vue`) is the app's picker, used on the menu `icon` field in **System → Menu administration**. It wraps the npm package's `IconPicker`, injects tenon's own vue-i18n labels, and reuses the collections already registered globally by `setupIcons()` (so `ph` shows up as the first/default tab) — no configuration needed at the call site.

::: tip The picker's full API
This only covers how icons are wired into the app. The picker component itself — multi-library tabs, registering local SVGs, `labels`/i18n, the `v-model` contract — is provided by a standalone package; for its API, see [IconPicker](/components/icon-picker).
:::
