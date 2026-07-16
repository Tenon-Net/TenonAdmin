# IconPicker

> `tenon-naive-iconify-picker` —— 面向 **Vue 3 + Naive UI** 的离线优先图标选择器,基于 [Iconify](https://iconify.design)。

可注册任意多个图标库(每个库一个 Tab),**零网络请求**离线浏览,可注入你自己的 SVG,选中结果就是一个字符串——非常适合"给这个菜单选个图标"这类字段。

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/tenon-naive-iconify-picker"><img src="https://img.shields.io/npm/v/tenon-naive-iconify-picker?color=cb3837&logo=npm" alt="npm"></a>
  <a href="https://github.com/Tenon-Net/tenon-naive-iconify-picker"><img src="https://img.shields.io/github/stars/Tenon-Net/tenon-naive-iconify-picker?logo=github" alt="GitHub"></a>
</div>

## 特性

- 🧭 **多图标库** —— 可注册任意多个(Lucide、Ant Design、Element Plus、Phosphor…),每个库一个 Tab。
- 🔌 **离线** —— 注册的库从本地数据渲染,永远不请求 `api.iconify.design`。
- 📦 **零配置** —— 开箱自带 Lucide,导入即用。
- 🎨 **主题自适应** —— 边框、文字、悬停、主色、圆角全部通过 `useThemeVars()` 跟随宿主 Naive 主题,无需接任何 CSS 变量。
- 🖼️ **本地 SVG** —— 注册你项目里的 SVG,以 `local:<名字>` 选择。
- 🌐 **在线兜底** —— 输入任意 Iconify 名称(如 `mdi:home`),联网时在线加载。
- 🌍 **可国际化** —— 所有文案来自一个 `labels` prop。
- 🧩 **单字符串值** —— `v-model` 就是一个 `prefix:name`(或 `local:name`)字符串,直接存进数据库字段。

## 前置要求

这是**用在既有应用里的组件**,不打包 Vue 和 Naive UI,需以 peer 依赖提供:`vue ^3.3`、`naive-ui ^2.34`、`@iconify/vue ^4 || ^5`。仅限浏览器(用到 `navigator.onLine`、`v-html`)。

## 安装

```bash
npm i tenon-naive-iconify-picker
```

样式由组件**自动注入**,无需单独 import 任何 CSS。

## 快速开始

零配置:内置的 **Lucide** 库已自动注册。

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { IconPicker, OfflineIcon } from 'tenon-naive-iconify-picker'

const icon = ref('lucide:rocket')
</script>

<template>
  <!-- 必须在应用的 <n-config-provider> 内,主题才能解析 -->
  <IconPicker v-model="icon" />

  <!-- 任意位置渲染已存的值 —— 同样离线,也支持 local: -->
  <OfflineIcon :icon="icon" :size="18" />
</template>
```

`v-model` 保存的是单个字符串,如 `lucide:rocket` 或 `local:star`。整个契约就这一点。

## 注册更多图标库

你注册的每个库都会成为一个 **Tab**。图标数据**不打包**进本组件:每个库在**你的**构建里是一个懒加载 chunk,只在第一次点开该 Tab 时加载(Lucide ≈ 85 KB gz;Phosphor ≈ 946 KB gz)。

```bash
npm i @iconify-json/ant-design @iconify-json/ep @iconify-json/ph
```

```ts
// main.ts
import { setupIconPicker, lucideCollection } from 'tenon-naive-iconify-picker'

setupIconPicker({
  collections: [
    lucideCollection, // 本包内置
    { prefix: 'ant-design', name: 'Ant Design',  loader: () => import('@iconify-json/ant-design/icons.json').then(m => m.default) },
    { prefix: 'ep',         name: 'Element Plus', loader: () => import('@iconify-json/ep/icons.json').then(m => m.default) },
    { prefix: 'ph',         name: 'Phosphor',     loader: () => import('@iconify-json/ph/icons.json').then(m => m.default) },
  ],
})
```

保存的值带库前缀(`ant-design:home-outlined`),所以 `<OfflineIcon>` 总能正确渲染。到 [icon-sets.iconify.design](https://icon-sets.iconify.design) 浏览所有可用库。

## 本地 SVG

**Vite**——用 glob 把 SVG 目录读成原始字符串,文件名(去掉 `.svg`)就是图标名:

```ts
import { registerLocalIcons } from 'tenon-naive-iconify-picker'

registerLocalIcons(
  import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
)
// star.svg  ->  local:star
```

## 国际化

组件内部**不含任何 i18n 框架**。所有可见文案来自一个 `labels` 对象(默认英文),用 `labels` prop 覆盖任意子集;接 vue-i18n 时传 `computed(() => ({ ... }))` 即随语言切换。

```vue
<IconPicker v-model="icon" :labels="{ placeholder: '选择一个图标', title: '图标' }" />
```

文案键:`placeholder` / `title` / `search` / `local` / `online` / `onlinePlaceholder` / `use` / `offlineHint` / `loading` / `empty` / `more`(支持 `{n}` 占位)。

> 完整 props(`collections` / `localIcons` / `cap` / `clearable` 等)、`OfflineIcon` API、SSR / Nuxt 注意事项,见 [package README](https://github.com/Tenon-Net/tenon-naive-iconify-picker/blob/main/README.zh-CN.md)。
