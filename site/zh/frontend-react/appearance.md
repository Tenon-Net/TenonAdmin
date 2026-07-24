# 主题与图标

后台的外观没有一处写在组件 props 里。颜色由一层 CSS 变量下发，再桥接给 antd；图标在构建期扫描源码，只把用到的那些打进包。换主色、补暗色、加图标，改的都是这两处的输入。

## 四层 token，业务只碰角色令牌

`web-react` 与 `web` 共享同一份设计令牌规范，只是消费方式不同。`web-react/src/styles/tokens.css` 把所有 CSS 自定义属性分成四层：

- **原语**：灰阶（`--color-gray-50…900`）、品牌主色、四个语义色的基色。固定值，不随主题翻转。
- **角色令牌**：`--color-bg-*`、`--color-text-*`、`--color-border*`、`--color-fill*`、`--color-primary*`、`--color-mask`。语义化命名，亮色值在 `:root`，暗色覆盖在 `:root[data-theme="dark"]`。**业务代码只消费这一层。**
- **度量**：字号、间距、圆角。与主题无关，只在 `:root` 出现一次。
- **阴影**：亮色在 `:root`，暗色单独覆盖（深底需要更重的投影）。

「业务只碰角色令牌」不是规矩，是省事。假如某段业务 CSS 直接拿 `--color-gray-900` 当文字色，暗色主题就翻不到它：灰阶是固定原语，不随 `data-theme` 变，于是深底上顶着一行黑字。只有角色令牌在亮暗两套里各存一份值，写 `--color-text-primary` 这一行两个主题下都对，翻的是 token 背后的值，不是你的样式。整套换配色也一样，只改角色令牌那层的覆盖块，原语和度量原封不动。

## 明暗：首访跟随系统，手动切换后记住

`themeScheme` 有三态：`'light'`、`'dark'`、`'auto'`（默认），在 `app` store 里（`web-react/src/stores/app.ts`）。`auto` 下由 `isDark` 选择器解析当前深浅：它读 `systemDark` 字段，而 `systemDark` 由模块级的一个 `matchMedia('(prefers-color-scheme: dark)')` 监听推入，等价于 Vue 侧的 `usePreferredDark`。所以首访自动匹配系统深浅色，系统主题一变页面也实时跟着翻。点顶栏切换按钮 `toggleDark()`，或调 `setThemeScheme()`，就落到明确的 `'light'` 或 `'dark'`，随 store 持久化进 `localStorage`（键 `app`），此后不再跟随系统。

`systemDark` 是设备当下的状态，不是用户偏好，所以它不持久化：存下来会让换了系统主题的用户在首帧看到上次那一侧。`isDark` 写成纯选择器，组件内 `useAppStore(isDark)`、组件外 `isDark(useAppStore.getState())` 都能同步读到，路由守卫和主题桥要的正是这个组件外的读法。

## 主色与密度

同一个 store 里还有两个用户可调、和 `themeScheme` 一起持久化的开关：

- `accent`：品牌主色，从 6 个候选里选（`web-react/src/theme/accents.ts`：靛蓝 `#646CFF` 默认，另有紫、青、粉、橙、绿）。换主色即重算 `--color-primary*`。
- `density`：`'comfortable'` / `'compact'`，走两条路。一条打到 `<html>` 的 `data-density`，联动手写壳的页内边距与卡片间距（`web-react/src/styles/chrome.css`）；另一条把 antd 的 `compactAlgorithm` 叠进主题，收紧组件自身的尺寸。表格行高 `compactAlgorithm` 不管，另给了 `cellPaddingBlock` 补上。

## 全站灰阶（哀悼模式）

灰阶是一个独立开关，和上面几项不同：它根本不进 antd 主题。`useDocumentGrayscale()`（`web-react/src/theme/useDocumentGrayscale.ts`）把 `app.grayscale` 映射成 `<html>` 上的 `data-gray` 属性，`web-react/src/styles/chrome.css` 里的 `html[data-gray] { filter: grayscale(1) }` 据此给整页去色，用在哀悼日这类场景。

它单独成一条 effect，故意不进主题桥的依赖。灰阶只是一层 CSS filter，不改任何 antd token，混进去只会让「切灰阶」白白重建整棵 `ConfigProvider`。抽成一个 hook 而不是内联在 `App` 里，是为了能单测这个 DOM 副作用。

## 从 token 到 antd

手写 CSS 直接读 tokens，但 antd 组件不认 CSS 变量，它要一个 JS 对象，也就是 `ConfigProvider` 的 `theme`。桥在 `buildAntdTheme()`（`web-react/src/theme/antd-theme.ts`）：用 `getComputedStyle` 把同一批 CSS 变量读出来，映到 antd 的 token 上，`colorPrimary` 接主色、`colorBgContainer` 接 `--color-bg-container`、`colorText` 接 `--color-text-primary`、`borderRadius` 接 `--radius-md`，依此类推。两边读的是同一份值，手写样式和 antd 组件不会各显各的色。

antd 和 Naive 这里有两处硬差别。一是数值 token 收的是**数字**不是字符串：`--radius-md` 是 `"10px"`，得先 `num()` 解析成 `10` 再下发，解析不出的键必须整个丢掉，这归 `defined()` 干。antd 内部是朴素展开 `{...种子, ...你的配置}`，而展开会连值为 `undefined` 的键一起复制，把种子覆盖成 `undefined`，圆角、字号会成片失效。二是 antd 会从 `colorPrimary`、`colorError` 这些种子色自己派生出 hover 与 active 的整条色阶，所以这座桥比 Naive 版**明显短**：Naive 每个语义色都要手工给四态，那套派生在这里整个不需要。

填充阶要留神。设计系统在 hover 之外还给了一档**按下**态（`--color-fill-active`），这是两个模板有意分叉的一处：antd 的 filled 按钮和搜索框把静息、hover、按下三态分别接到 `colorFillTertiary`、`colorFillSecondary`、`colorFill`，只有两档的话按下去毫无反馈；Naive 侧不需要这一档。每个填充 token 还得连同它的 antd alias 伙伴一起给，否则同一页会冒出两种灰。

主色是唯一不直接读、而是算出来的一档。6 个候选不可能每个都在 `tokens.css` 预写四态，所以只存一个 `accent`，其余由 `mix(a, b, t)` 派生（`web-react/src/theme/mix.ts`，两色按 `t∈[0,1]` 线性插值，和 Vue 版同一套魔数）。亮色下 `hover = mix(primary, #FFF, .16)`、`pressed = mix(primary, #000, .18)`；暗色先把 accent 往白里提亮一档再往下派生，免得靛蓝压在深底上发闷。

这些落地在 `useAntdTheme()`（`web-react/src/theme/useAntdTheme.ts`），用 `useLayoutEffect` 盯着 `dark`、`accent`、`density`，顺序是死的：先把 `data-theme` / `data-density` 打到 `<html>`，因为 `getComputedStyle` 读的是当前主题下的值，先读后翻就会拿到上一套配色、永远慢一拍；再重建 antd 配置；最后把 antd **最终解析出来的**主色写回 `--color-primary*`，供不经 antd 的手写样式（布局壳、登录页）消费。

最后这步为什么用 antd 解析后的值、而不是种子？因为 antd 的 `darkAlgorithm` 会拿种子再生成一整条暗色调色板，`colorPrimary` 是派生结果而非种子。要是把种子写回 CSS 变量，同一页上「antd 按钮」和「用 `var(--color-primary)` 的元素」会是两种紫，亮色下恰好相等，只有暗色露馅。以 antd 为准，模板内部才自洽。两个模板的暗色主色因此不同，这是有意的：各家 UI 库有各自的调色体系。`App.tsx` 把结果接到 `<ConfigProvider theme={themeConfig}>`，包住整个应用。

::: tip 完整令牌与映射
上面够你换主色、加暗色、判断该改哪一层。完整的令牌清单在 `web-react/src/styles/tokens.css`，`token → antd` 的逐条映射连同每个键为什么这么给的理由，都写在 `web-react/src/theme/antd-theme.ts` 的注释里。
:::

## 图标：构建期生成子集，离线渲染

两个模板的图标机制在这里彻底分叉。图标一律**离线渲染**，不请求 `api.iconify.design`；但打进包的是哪些图标，两边算法相反。Vue 版启动时把整套离线集注册进去（Phosphor 一套就近 1 MB），React 版反过来：构建期扫一遍源码，只把真正写到的图标名打进首屏。

构建脚本是 `web-react/scripts/generate-icon-subset.mjs`，挂在 `package.json` 的 `predev` / `prebuild` 上，每次起 dev 或打包前先跑一次。它扫 `src/` 下所有 `.ts` / `.tsx`（跳过 `.spec.`），用正则抓 `ph`、`lucide`、`ep`、`ant-design` 四个前缀后面的图标名字面量，连同 `scripts/icon-manifest.json` 里手动列的名字，从完整的 `@iconify-json/<prefix>` 集合里切出这些图标，写成 `src/assets/icons.generated.json`。首屏 bundle 里只有这份子集，源码没写到的图标不进包。

手动清单 `icon-manifest.json` 补的是扫描器看不见的图标：名字不是静态字面量、或只存在后端菜单配置里的那些。扫描只认写死在源码里的 `prefix:name`，动态拼出来的抓不到。

运行时分两条路。`setupIcons()`（`web-react/src/lib/icons.ts`）在 `main.tsx` 里调一次，把生成的子集同步注册进 `@iconify/react/offline`，首屏图标立即可渲染。完整的四套集合仍在，作为按需懒加载的 chunk（每套一个 `import('@iconify-json/<prefix>/icons.json')`），两种情形才拉：一是后端某个菜单配了子集之外的图标，`ensureIconLoaded` 按前缀补载那一整套；二是选择器打开某个 Tab，要枚举该集全部图标名。

于是首屏只背真正用到的图标，气隙部署下渲染任何已注册图标都不触网；后端临时配一个子集外的图标也不会开天窗，那一套会在用到时懒加载补上。

## 在组件里用图标

`AppIcon`（`web-react/src/components/AppIcon.tsx`）是全站渲染图标的标准方式，薄封装 `@iconify/react` 的 `<Icon>`（走 `/offline` 入口，不触网），不依赖 Vue 版那个 `tenon-naive-iconify-picker` 包。

```tsx
import { AppIcon } from '@/components/AppIcon'

<AppIcon icon="ph:house-duotone" />
<AppIcon icon="local:star" size={20} />
```

`icon` 是 `prefix:name` 字符串，本地 SVG 用 `local:name`，默认大小 `18`。`icon` 为空或找不到时兜底成 `ph:dot-outline-duotone`（用 `||` 判空，因为后端可能存了空串，不是只有 `null`）。侧栏目录节点另传 `ph:folder-duotone` 作兜底。渲染前 `AppIcon` 会 `ensureIconLoaded`：万一这个图标不在首屏子集里，就把它所属的那套离线集补载进来。

本地 SVG 走 `src/assets/svg/*.svg`，`web-react/src/lib/icons.ts` 用 Vite 的 raw glob 在模块级导入并注册成 `local:<名字>`：

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

例如 `src/assets/svg/star.svg` 成为可选的 `local:star`。

## 在菜单管理里选图标

`IconPicker`（`web-react/src/components/IconPicker.tsx`）用在**系统管理 → 菜单管理**的菜单 `icon` 字段上。和 Vue 版最大的不同：它是模板内联实现，不用 `tenon-naive-iconify-picker`（那是个 Vue 加 Naive 专属包）。选择器就是 antd 的 `Modal` 加 `Tabs` 加一张图标网格，触发器上显示当前值。

值契约是一个字符串：`prefix:name`（如 `ph:folder`）或 `local:name`，空串表示未选，受控（`value` / `onChange`），可直接放进 antd 的 `Form.Item`。Tab 顺序即 `COLLECTIONS`：`ph`（Phosphor，首个也是默认）、`lucide`、`ep`、`ant-design`，外加本地 SVG 一个 Tab。打开或切 Tab 时按前缀加载该集图标名，内置集懒加载、本地集同步取，单页最多渲染 300 个，超出提示继续输入缩小范围。

选择器文案走 react-i18next（`iconPicker.*` 键），跟着语言切换。加键约定见 [国际化](/zh/frontend-react/i18n)。
