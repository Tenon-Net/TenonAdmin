# 主题与图标

后台的外观没有一处写在组件 props 里。颜色经一层 CSS 变量下发，图标在启动时一次性离线注册好。换主色、补暗色、塞自己的 SVG，改的都只是这两处的输入。

## 四层 token，业务只碰角色令牌

TenonAdmin 的外观由 CSS 自定义属性驱动，不是组件 props。`web/src/styles/tokens.css` 把所有变量分成四层：

- **原语**：灰阶（`--color-gray-50…900`）、品牌主色、四语义色的基色。固定值，不随主题翻转。
- **角色令牌**：`--color-bg-*`、`--color-text-*`、`--color-border*`、`--color-fill*`、`--color-primary*`、`--color-mask`。语义化命名，亮色值放在 `:root`，暗色覆盖放在 `:root[data-theme="dark"]`。**业务代码只消费这一层。**
- **度量**：字号、间距、圆角。与主题无关，只在 `:root` 出现一次。
- **阴影**：亮色在 `:root`，暗色单独覆盖（深底需要更重的投影）。

"业务只碰角色令牌"不是规矩，是省事：假如某段业务 CSS 直接写了 `--color-gray-900` 当文字色，暗色主题翻不到它。灰阶是固定原语，不随 `data-theme` 变，于是深底上顶着一行黑字。只有角色令牌在亮暗两套里各存一份值，`--color-text-primary` 这一行 CSS 在两个主题下都对，翻的是 token 背后的值，不是你的样式。换肤同理：整套换配色也只改角色令牌那一层的覆盖块，原语和度量原封不动。

## 明暗：首访跟随系统，手动切换后记住

`app` store（`web/src/stores/app.ts`）的 `themeScheme` 有三态：`'light'`、`'dark'`、`'auto'`（默认）。`auto` 下，`isDark` getter 通过 VueUse 的 `usePreferredDark` 响应式读取系统偏好。首访因此自动匹配系统深浅色，系统主题变了也实时联动。点顶栏的切换按钮（`toggleDark()`）或调 `setThemeScheme()`，会落到明确的 `'light'`/`'dark'`，并随 store 持久化进 `localStorage`(key `app`)，此后不再跟随系统。

## 主色与密度

同一个 store 里还有两个用户可调的开关，和 `themeScheme` 一起持久化：

- `accent`：品牌主色，从 6 个候选里选（`web/src/theme/accents.ts`：靛蓝 `#646CFF` 默认，另有紫、青、粉、橙、绿）。换主色即重算 `--color-primary*`。
- `density`：`'comfortable'` / `'compact'`，落到 `<html>` 的 `data-density`，联动表格行高与卡片内边距。

## 从 token 到 Naive UI

手写 CSS 直接读 tokens，但 Naive UI 组件不认 CSS 变量。它要一个 JS 对象（`GlobalThemeOverrides`）。`buildThemeOverrides()`（`web/src/theme/naive-theme.ts`）用 `getComputedStyle` 把同一批 CSS 变量读出来，映到 Naive 的 `common.*`（`primaryColor`←`--color-primary`、`bodyColor`←`--color-bg-body`、`borderRadius`←`--radius-md`，依此类推）。两边读的是同一份值，所以手写样式和 Naive 组件永远不会各显各的色。

主色是唯一不直接读、而是算出来的一档。6 个候选主色不可能每个都在 `tokens.css` 里预写 hover/pressed/light 四态，所以只存一个 `accent`，其余态由 `mix(a, b, t)`（`web/src/theme/mix.ts`，两色按 `t∈[0,1]` 线性插值）派生：亮色 `hover = mix(primary, #FFF, .16)`、`pressed = mix(primary, #000, .18)`;暗色先把 accent 往白里提亮一档（`mix(accent, #FFF, .18)`）再派生，免得靛蓝压在深底上发闷。

落地在 `useTheme()`(`web/src/composables/useTheme.ts`)：它盯着 `app.isDark` / `accent` / `density`，一变就往 `<html>` 打 `data-theme` / `data-density`，把派生出的 `--color-primary*` 写进 `document.documentElement`（让消费 token 的手写 CSS 立即换色），再重建 Naive 的 `themeOverrides`。`App.vue` 把结果接到 `<n-config-provider :theme-overrides>`，包住整个应用。

::: tip 完整 token 表
上面够你换主色、加暗色、判断该改哪一层。完整的令牌清单、语义徽章派生、`token → Naive` 全映射表，见 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md)。
:::

## 图标离线注册

图标是**离线渲染**的：几套 Iconify 图标集加上你自己的本地 SVG，启动时统一注册一次，之后在任意组件里通过薄封装 `AppIcon` 使用，也可以在菜单管理里用 `IconPicker` 交互式挑选。

`setupIcons()`（`web/src/lib/icons.ts`）在 `main.ts` 里只调一次，通过 `tenon-naive-iconify-picker` 的 `setupIconPicker` 注册两类来源：

- **离线 Iconify 集**：`ph`（Phosphor，默认集，启动时预热）、`lucide`（Lucide）、`ep`（Element Plus）、`ant-design`（Ant Design）。每套是独立的懒加载 `@iconify-json/<prefix>` chunk，第一次用到才加载。
- **本地 SVG**：`src/assets/svg/*.svg` 下的所有文件，以原始字符串 glob 导入：

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

  例如 `src/assets/svg/star.svg` 成为可选的 `local:star`。

Iconify 数据和 SVG 都打进应用本身，图标渲染从不请求外部 CDN（如 `api.iconify.design`）。所以离线或网络受限的部署环境里，表现和联网时一致。

## 在组件里用图标

`AppIcon`（`web/src/components/AppIcon.vue`）封装了包里的 `OfflineIcon`，是全站渲染图标的标准方式：

```vue
<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
</script>

<template>
  <AppIcon icon="ph:house-duotone" />
  <AppIcon icon="local:star" :size="20" />
</template>
```

`icon` 是 `prefix:name` 字符串（本地 SVG 用 `local:name`），默认大小 `18`。`icon` 为空或找不到时，`AppIcon` 兜底为 `ph:dot-outline-duotone`，和侧栏菜单在 rail/折叠态下、条目未设图标时的兜底一致。

## 在菜单管理里选图标

`IconPicker`（`web/src/components/IconPicker/index.vue`）是应用里的选择器，用在**系统管理 → 菜单管理**的菜单 `icon` 字段上。它封装 npm 包的 `IconPicker`，注入 tenon 自己的 vue-i18n 文案，并复用 `setupIcons()` 已全局注册好的图标集。`ph` 于是成为首个、也是默认的 Tab，调用处不用再配置。

::: tip 选择器完整 API
这里只讲图标在应用里怎么接入。选择器组件本身由独立包提供，多图标库 Tab、注册本地 SVG、`labels`/i18n、`v-model` 约定的完整 API 见 [IconPicker](/zh/components/icon-picker)。
:::
