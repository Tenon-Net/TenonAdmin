<script setup lang="ts">
// 统一图标渲染器:local: → 内联本地 SVG;其余 → iconify(内置集等离线注册完再渲,避免命中外部 CDN;非内置直接在线兜底)。
// 合并 XiHan(iconify offline)与 soybean(本地 SVG 双路)两种参考的渲染路径。
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { LOCAL_PREFIX, ensureCollection, isBundled, isRegistered, localSvgRaw } from '@/lib/icons'

const props = withDefaults(defineProps<{ icon?: string; size?: number | string }>(), {
  icon: '',
  size: 18,
})

// 与 useLayoutMenu.renderIcon 一致的兜底,保证 rail/折叠态也有图标。
const FALLBACK = 'ph:dot-outline-duotone'

const name = computed(() => (props.icon && props.icon.trim()) || FALLBACK)
const prefix = computed(() => {
  const i = name.value.indexOf(':')
  return i > 0 ? name.value.slice(0, i) : ''
})
const isLocal = computed(() => prefix.value === LOCAL_PREFIX)
const localRaw = computed(() => (isLocal.value ? localSvgRaw(name.value.slice(LOCAL_PREFIX.length + 1)) : undefined))

// 内置集须等注册完再渲染,否则 <Icon> 会回落到外部 iconify CDN;非内置直接渲染(联网兜底)。
const iconifyReady = ref(false)
watch(
  name,
  () => {
    if (isLocal.value) return
    const p = prefix.value
    if (!isBundled(p) || isRegistered(p)) {
      iconifyReady.value = true
      return
    }
    iconifyReady.value = false
    ensureCollection(p).then(() => {
      iconifyReady.value = true
    })
  },
  { immediate: true },
)

const px = computed(() => (typeof props.size === 'number' ? `${props.size}px` : props.size))
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html — 本地 SVG 为项目内可信资产,非用户输入 -->
  <span v-if="isLocal" class="app-icon" :style="{ width: px, height: px }" v-html="localRaw || ''" />
  <Icon v-else-if="iconifyReady" :icon="name" :width="px" :height="px" />
  <span v-else class="app-icon" :style="{ width: px, height: px }" />
</template>

<style scoped>
.app-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.app-icon :deep(svg) {
  width: 100%;
  height: 100%;
}
</style>
