<script setup lang="ts">
// Tenon 品牌徽标(滚筒刷 × T)。内联 SVG、固定品牌配色(不随 accent 变——品牌身份固定 #646CFF);
// 明暗两版切换,源自 design-mockups/brand/tenon-logo(-dark).svg。
import { computed } from 'vue'
import { useAppStore } from '@/stores/app'

const props = withDefaults(defineProps<{ size?: number }>(), { size: 28 })
const app = useAppStore()

const bg = computed(() => (app.isDark ? '#181A2A' : '#646CFF'))
const mark = computed(() => (app.isDark ? '#8B91FF' : '#FFFFFF'))
</script>

<template>
  <svg :width="props.size" :height="props.size" viewBox="0 0 64 64" role="img" aria-label="Tenon">
    <rect width="64" height="64" rx="6" :fill="bg" />
    <path d="M12 12H52V24H48V20H44V52H32V28H40V20H36V24H12Z" :fill="mark" />
  </svg>
</template>
