<script setup lang="ts">
// tenon 图标渲染器:直接复用 npm 包的 OfflineIcon,仅钉死 tenon 全站的空值兜底图标。
// 离线集/本地 SVG 已由 main.ts 的 setupIcons() 全局注册。
import { OfflineIcon } from 'tenon-naive-iconify-picker'

defineProps<{ icon?: string; size?: number | string }>()
</script>

<template>
  <!-- 兜底与 useLayoutMenu.renderIcon 一致,保证 rail/折叠态也有图标 -->
  <OfflineIcon :icon="icon" :size="size ?? 18" fallback="ph:dot-outline-duotone" />
</template>
