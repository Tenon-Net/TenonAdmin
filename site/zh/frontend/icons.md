# 图标

TenonAdmin 的图标是**离线渲染**的:几套 Iconify 图标集加上你自己的本地 SVG,在启动时统一注册一次,之后在任意组件里通过一个薄封装 `AppIcon` 使用,也可以在菜单管理里通过 `IconPicker` 组件交互式挑选。

## 注册图标集

`setupIcons()`(`src/lib/icons.ts`)在 `main.ts` 里只调用一次,通过 `tenon-naive-iconify-picker` 的 `setupIconPicker` 注册:

- **离线 Iconify 集**——`ph`(Phosphor,默认集,启动时预热)、`lucide`(Lucide)、`ep`(Element Plus)、`ant-design`(Ant Design)。每一套都是独立的懒加载 `@iconify-json/<prefix>` chunk,只在第一次用到时才加载。
- **本地 SVG**——`src/assets/svg/*.svg` 下的所有文件,以原始字符串 glob 导入:

```ts
import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })
```

  例如 `src/assets/svg/star.svg` 会成为可选的 `local:star`。

由于 Iconify 数据和 SVG 都打包进了应用本身,图标渲染从不请求外部 CDN(如 `api.iconify.design`)——在离线或网络受限的部署环境里,表现和联网时完全一致。

## 在组件里用图标

`AppIcon`(`src/components/AppIcon.vue`)封装了包里的 `OfflineIcon`,是全站渲染图标的标准方式:

```vue
<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
</script>

<template>
  <AppIcon icon="ph:house-duotone" />
  <AppIcon icon="local:star" :size="20" />
</template>
```

`icon` 的值是一个 `prefix:name` 字符串(本地 SVG 用 `local:name`)。默认大小为 `18`。若 `icon` 为空或找不到对应图标,`AppIcon` 会兜底为 `ph:dot-outline-duotone`——和侧栏菜单在 rail/折叠态下、条目未设置图标时使用的兜底一致。

## 在菜单管理里选图标

`IconPicker`(`src/components/IconPicker/index.vue`)是应用里的选择器组件,用在**系统管理 → 菜单管理**的菜单 `icon` 字段上。它封装了 npm 包的 `IconPicker`,注入 tenon 自己的 vue-i18n 文案,并复用 `setupIcons()` 已经全局注册好的图标集(因此 `ph` 会作为首个/默认 Tab)——调用处无需额外配置。

::: tip 完整选择器 API
本页只介绍图标在应用里是怎么接入的。选择器组件的完整 API——多图标库 Tab、注册本地 SVG、`labels`/i18n、`v-model` 约定——见 [IconPicker](/zh/components/icon-picker)。
:::

## 接下来看看

- [主题](/zh/frontend/theme)
- [IconPicker 组件](/zh/components/icon-picker)
