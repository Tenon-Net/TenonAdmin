<script setup lang="ts">
// Tenon 品牌徽标(透榫)。内联 SVG、固定品牌配色(不随 accent 变——品牌身份固定 #646CFF);
// 明暗两版切换,源自 design-mockups/brand/tenon-logo(-dark).svg。
import { computed } from 'vue'
import { useAppStore } from '@/stores/app'

const props = withDefaults(defineProps<{ size?: number }>(), { size: 28 })
const app = useAppStore()

const bg = computed(() => (app.isDark ? '#16181D' : '#646CFF'))
const mark = computed(() => (app.isDark ? '#7A81FF' : '#FFFFFF'))
const markOpacity = computed(() => (app.isDark ? 0.45 : 0.5))
</script>

<template>
  <svg :width="props.size" :height="props.size" viewBox="0 0 120 120" role="img" aria-label="Tenon">
    <rect width="120" height="120" rx="26" :fill="bg" />
    <g transform="translate(21,21) scale(0.65)">
      <rect x="16" y="27" width="88" height="26" rx="6" :fill="mark" :fill-opacity="markOpacity" />
      <rect x="47" y="27" width="26" height="76" rx="6" :fill="mark" />
    </g>
  </svg>
</template>
