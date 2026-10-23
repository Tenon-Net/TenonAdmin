<script setup lang="ts">
// 常用子集直接渲染；完整图标集加载和本地 SVG 仍交给包的 OfflineIcon。
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { OfflineIcon } from 'tenon-naive-iconify-picker'
import { isCoreIcon } from '@/lib/icons'

const props = withDefaults(defineProps<{ icon?: string; size?: number | string; fallback?: string }>(), {
  size: 18,
  fallback: 'lucide:dot',
})
const name = computed(() => props.icon?.trim() || props.fallback)
</script>

<template>
  <!-- 兜底与 useLayoutMenu.renderIcon 一致,保证 rail/折叠态也有图标 -->
  <Icon v-if="isCoreIcon(name)" :icon="name" :width="size" :height="size" />
  <OfflineIcon v-else :icon="name" :size="size" />
</template>
