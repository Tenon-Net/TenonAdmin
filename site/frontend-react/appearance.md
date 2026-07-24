# Theme & Icons

None of the admin's appearance lives in component props. Colors flow down through one layer of CSS variables and are bridged into antd; icons are picked out by scanning the source at build time, so only the ones you actually use get bundled. Reskinning, adding a dark palette, or slotting in an icon all change the inputs to these two mechanisms.

## Four token layers; business code touches only the role tokens

`web-react` and `web` share the same design-token spec — they only consume it differently. `web-react/src/styles/tokens.css` splits every CSS custom property into four layers:

- **Primitives** — the gray scale (`--color-gray-50…900`), the brand color, and the base colors of the four semantic colors. Fixed values, they don't flip with the theme.
- **Role tokens** — `--color-bg-*`, `--color-text-*`, `--color-border*`, `--color-fill*`, `--color-primary*`, `--color-mask`. Semantically named, with light values on `:root` and dark overrides on `:root[data-theme="dark"]`. **Business code consumes only this layer.**
- **Metrics** — font sizes, spacing, radii. Theme-independent, present once on `:root`.
- **Shadows** — light on `:root`, dark overridden separately (a dark background needs a heavier shadow).

"Business code touches only the role tokens" isn't a rule, it's the lazy path. If some business CSS wrote `--color-gray-900` directly as its text color, the dark theme couldn't reach it: the gray scale is a fixed primitive, it doesn't move with `data-theme`, so you'd get a line of black text on a dark background. Only the role tokens store a value for each of light and dark, so the single line `--color-text-primary` is correct under both themes — what flips is the value behind the token, not your styles. Reskinning works the same way: swapping the whole palette only touches the override block in the role-token layer, leaving primitives and metrics untouched.

## Dark, light, auto: follow the system on first visit, remembered after a manual toggle

`themeScheme` has three states — `'light'`, `'dark'`, `'auto'` (the default) — in the `app` store (`web-react/src/stores/app.ts`). In `auto`, the `isDark` selector resolves the current shade: it reads the `systemDark` field, which a module-level `matchMedia('(prefers-color-scheme: dark)')` listener feeds (the equivalent of Vue's `usePreferredDark`). So the first visit matches the system light/dark, and the page live-updates when the OS theme changes. Clicking the header toggle (`toggleDark()`) or calling `setThemeScheme()` lands on an explicit `'light'`/`'dark'`, persisted with the store to `localStorage` (key `app`), and from then on it no longer follows the system.

`systemDark` is the device's current state, not a user preference, so it isn't persisted: storing it would show a user who changed their OS theme the previous side on the first frame. `isDark` is written as a pure selector, readable both inside a component (`useAppStore(isDark)`) and outside one (`isDark(useAppStore.getState())`) — the router guard and the theme bridge need exactly that out-of-component read.

## Accent and density

Two more user-facing knobs live in the same store, persisted alongside `themeScheme`:

- `accent` — the brand color, chosen from 6 candidates (`web-react/src/theme/accents.ts`: indigo `#646CFF` by default, plus purple, cyan, pink, orange, green). Changing the accent recomputes `--color-primary*`.
- `density` — `'comfortable'` / `'compact'`, applied along two paths. One stamps `data-density` onto `<html>`, driving the hand-written shell's page padding and card spacing (`web-react/src/styles/chrome.css`); the other folds antd's `compactAlgorithm` into the theme, tightening the components' own metrics. `compactAlgorithm` doesn't touch table row height, so `cellPaddingBlock` is given separately to cover it.

## Site-wide grayscale (mourning mode)

Grayscale is a standalone switch, unlike the ones above: it never enters the antd theme at all. `useDocumentGrayscale()` (`web-react/src/theme/useDocumentGrayscale.ts`) maps `app.grayscale` to a `data-gray` attribute on `<html>`, and `html[data-gray] { filter: grayscale(1) }` in `web-react/src/styles/chrome.css` desaturates the whole page — used on days of mourning and the like.

It's a separate effect, deliberately kept out of the theme bridge's dependencies. Grayscale is just a CSS filter that changes no antd token; folding it in would rebuild the entire `ConfigProvider` on every toggle for nothing. Extracting it into its own hook, rather than inlining it in `App`, is so the DOM side-effect can be unit-tested.

## From tokens to antd

Hand-written CSS reads the tokens directly, but antd components don't understand CSS variables — they want a JS object, namely `ConfigProvider`'s `theme`. The bridge is `buildAntdTheme()` (`web-react/src/theme/antd-theme.ts`): it reads that same batch of CSS variables out with `getComputedStyle` and maps them onto antd's tokens — `colorPrimary` to the accent, `colorBgContainer` to `--color-bg-container`, `colorText` to `--color-text-primary`, `borderRadius` to `--radius-md`, and so on. Both sides read the same values, so hand-written styles and antd components never drift into different colors.

antd and Naive differ hard in two places here. First, numeric tokens want a **number**, not a string: `--radius-md` is `"10px"`, so it must be parsed to `10` by `num()` before it's handed over, and a key that won't parse must be dropped entirely — that's what `defined()` does. Internally antd does a plain spread `{...seed, ...yourConfig}`, and the spread copies keys whose value is `undefined`, overwriting the seed with `undefined`, so radii and font sizes fail in bulk. Second, antd derives the whole hover/active ramp itself from seed colors like `colorPrimary` and `colorError`, so this bridge is **markedly shorter** than the Naive one: Naive hand-writes four states for every semantic color, and that derivation isn't needed here at all.

The fill scale needs care. Beyond hover, the design system also defines a **pressed** state (`--color-fill-active`), one place where the two templates diverge on purpose: antd's filled buttons and search boxes wire rest / hover / pressed to `colorFillTertiary` / `colorFillSecondary` / `colorFill`, and with only two steps a press gives no feedback; the Naive side doesn't need this step. Each fill token also has to be given together with its antd alias partner, or the same page sprouts two grays.

The accent is the one value that isn't read directly but computed. There's no way to pre-write four states for all 6 candidates in `tokens.css`, so only the one `accent` is stored and the rest are derived by `mix(a, b, t)` (`web-react/src/theme/mix.ts`, a linear interpolation of two colors by `t∈[0,1]`, the same magic numbers as Vue). In light, `hover = mix(primary, #FFF, .16)` and `pressed = mix(primary, #000, .18)`; in dark, the accent is first lightened one step toward white before deriving the rest, so indigo doesn't come out muddy against a dark background.

This all lands in `useAntdTheme()` (`web-react/src/theme/useAntdTheme.ts`), where a `useLayoutEffect` watches `dark`, `accent`, `density` in a fixed order: first stamp `data-theme` / `data-density` onto `<html>`, because `getComputedStyle` reads the values under the current theme, and reading before flipping picks up the previous palette, always one step behind; then rebuild the antd config; finally write antd's **resolved** accent back into `--color-primary*`, for the hand-written styles (layout shell, login page) that bypass antd.

Why that last step uses antd's resolved value rather than the seed: antd's `darkAlgorithm` takes the seed and generates a whole dark palette, so `colorPrimary` is a derived result, not the seed. Writing the seed back into the CSS variables would make an "antd button" and an element using `var(--color-primary)` two different purples on the same page — equal in light, exposed only in dark. Taking antd as the source of truth keeps the template internally consistent. The two templates' dark accents differ as a result, and that's intentional: each UI library has its own color algorithm. `App.tsx` wires the result into `<ConfigProvider theme={themeConfig}>`, wrapping the whole app.

::: tip The full tokens and mapping
The above is enough to change the accent, add a dark palette, and figure out which layer to touch. The complete token listing lives in `web-react/src/styles/tokens.css`, and the line-by-line `token → antd` mapping — with the reason each key is given the way it is — is in the comments of `web-react/src/theme/antd-theme.ts`.
:::

## Icons: a build-time subset, rendered offline

This is where the two templates' icon mechanisms fully diverge. Icons always render **offline**, never requesting `api.iconify.design`; but which icons get bundled is decided by opposite algorithms. The Vue template registers whole offline sets at startup (Phosphor alone is nearly 1 MB); the React template does the reverse — a build step scans the source and bundles only the icon names actually written into it.

The build script is `web-react/scripts/generate-icon-subset.mjs`, hooked to `package.json`'s `predev` / `prebuild`, so it runs once before every dev start or build. It scans every `.ts` / `.tsx` under `src/` (skipping `.spec.`), catches with a regex the icon-name literals following the four prefixes `ph`, `lucide`, `ep`, `ant-design`, and — together with the names hand-listed in `scripts/icon-manifest.json` — slices those icons out of the full `@iconify-json/<prefix>` collections into `src/assets/icons.generated.json`. Only that subset sits in the first-paint bundle; an icon not written into the source isn't bundled.

The hand-list `icon-manifest.json` covers icons the scanner can't see: names that aren't static literals, or that only exist in the backend's menu configuration. The scan only recognizes a `prefix:name` written literally in source; anything composed dynamically escapes it.

At runtime there are two paths. `setupIcons()` (`web-react/src/lib/icons.ts`), called once from `main.tsx`, registers the generated subset synchronously into `@iconify/react/offline`, so first-paint icons render immediately. The full four collections still exist, as lazy-loaded chunks (one `import('@iconify-json/<prefix>/icons.json')` per set), pulled only in two cases: when a backend menu is configured with an icon outside the subset, `ensureIconLoaded` fetches that whole set by prefix; and when the picker opens a tab and needs to enumerate all of that set's icon names.

So the first paint carries only the icons actually in use, an air-gapped deployment renders any registered icon without touching the network, and a backend icon outside the subset still doesn't leave a hole — its set lazy-loads the moment it's needed.

## Using an icon in a component

`AppIcon` (`web-react/src/components/AppIcon.tsx`) is the standard way to render an icon anywhere in the app — a thin wrapper over `@iconify/react`'s `<Icon>` (through the `/offline` entry, so it never touches the network), with no dependency on Vue's `tenon-naive-iconify-picker` package.

```tsx
import { AppIcon } from '@/components/AppIcon'

<AppIcon icon="ph:house-duotone" />
<AppIcon icon="local:star" size={20} />
```

`icon` is a `prefix:name` string (`local:name` for a local SVG), default size `18`. When `icon` is empty or can't be resolved, it falls back to `ph:dot-outline-duotone` (emptiness checked with `||`, since the backend may store an empty string, not only `null`). Sidebar directory nodes pass their own fallback, `ph:folder-duotone`. Before rendering, `AppIcon` calls `ensureIconLoaded`: if the icon isn't in the first-paint subset, its owning offline set is fetched in.

Local SVGs come from `src/assets/svg/*.svg`; `web-react/src/lib/icons.ts` imports and registers them as `local:<name>` at module load via Vite's raw glob:

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

A file like `src/assets/svg/star.svg` becomes selectable as `local:star`.

## Picking an icon in menu management

`IconPicker` (`web-react/src/components/IconPicker.tsx`) is used on the menu `icon` field under **System → Menu Management**. The biggest difference from the Vue version: it's implemented inline in the template, without `tenon-naive-iconify-picker` (a Vue + Naive-only package). The picker is just an antd `Modal` plus `Tabs` plus a grid of icons, with the trigger showing the current value.

The value contract is a single string: `prefix:name` (e.g. `ph:folder`) or `local:name`, empty string meaning unset, controlled (`value` / `onChange`), droppable straight into an antd `Form.Item`. Tab order follows `COLLECTIONS`: `ph` (Phosphor, first and default), `lucide`, `ep`, `ant-design`, plus one tab for local SVGs. Opening or switching a tab loads that set's icon names — built-in sets lazily, the local set synchronously — rendering at most 300 per page and prompting you to keep typing to narrow it down beyond that.

The picker's copy goes through react-i18next (`iconPicker.*` keys) and switches with the language. For how to add those keys, see [Internationalization](/frontend-react/i18n).
