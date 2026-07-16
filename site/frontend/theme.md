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

- [Icons](/frontend/icons)
- [Frontend Structure](/frontend/structure)
