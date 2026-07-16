# 主题与 Design Tokens

TenonAdmin 的外观由 CSS 自定义属性驱动,而不是组件 props。`web/src/styles/tokens.css` 定义了四层 token 体系:原语(灰阶、品牌主色、语义色基色)、**角色令牌**(`--color-bg-*`、`--color-text-*`、`--color-border*`、`--color-fill*`、`--color-primary*`,业务代码只消费这一层)、与主题无关的度量(字号、间距、圆角),以及阴影。亮色值放在 `:root`,暗色覆盖放在 `:root[data-theme="dark"]`。

运行时,`useTheme()`(`web/src/composables/useTheme.ts`)监听 app store 里的外观偏好,并落地到 `<html>` 上:设置 `data-theme`/`data-density`/`data-gray` 属性,从选定的主色派生出 `--color-primary*`,并重建 Naive UI 的主题对象。重建这一步发生在 `buildThemeOverrides()`(`web/src/theme/naive-theme.ts`)里 —— 它通过 `getComputedStyle` 读取同一套 CSS 变量,映射到 Naive 的 `GlobalThemeOverrides` 上。由于手写 CSS 和 Naive UI 组件读的是同一份 token 值,两者永远不会脱节。`App.vue` 把结果接入 `<n-config-provider :theme-overrides="overrides">`,包裹整个应用。

## 明暗与跟随系统

`app` store 的 `themeScheme` 有三种状态:`'light'`、`'dark'`、`'auto'`(默认)。`auto` 模式下,`isDark` getter 通过 VueUse 的 `usePreferredDark` 响应式地跟随系统偏好 —— 首次访问自动匹配系统主题,系统主题变化时也会实时联动。调用 `toggleDark()`(顶栏的主题切换按钮)或 `setThemeScheme()` 会设为明确的 `'light'`/`'dark'`,并持久化到 `localStorage`,此后就不再跟随系统。

## 主色与密度

同一个 `app` store 里还有另外两个用户可调的开关:`accent`(品牌主色,用来派生 `--color-primary*`)和 `density`(`'comfortable'` / `'compact'`,反映为 `<html>` 上的 `data-density`)。两者都和 `themeScheme` 一起持久化。

::: tip 完整 token 参考
本页只是导览,不是完整规范。完整的 token 表和 Naive UI 映射规范见 GitHub 上的 [`web/DESIGN.md`](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/DESIGN.md)。
:::

## 下一步

- [图标](/zh/frontend/icons)
- [前端目录结构](/zh/frontend/structure)
