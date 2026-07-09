<script setup lang="ts">
// tenon 图标选择器:复用 npm 包的 IconPicker,仅把 tenon 的 i18n 文案注入 labels(包本身不带 i18n 框架)。
// 图标集/本地 SVG 由 main.ts 的 setupIcons() 全局注册;这里不传 collections,复用全局注册顺序(ph 为首/默认页)。
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { IconPicker } from 'tenon-naive-iconify-picker'

const model = defineModel<string>({ default: '' })
const props = withDefaults(defineProps<{ placeholder?: string; clearable?: boolean }>(), {
  placeholder: '',
  clearable: true,
})

const { t } = useI18n()

const labels = computed(() => ({
  placeholder: props.placeholder || t('iconPicker.placeholder'),
  title: t('iconPicker.title'),
  search: t('iconPicker.search'),
  local: t('iconPicker.local'),
  online: t('iconPicker.online'),
  onlinePlaceholder: t('iconPicker.onlinePlaceholder'),
  use: t('iconPicker.use'),
  offlineHint: t('iconPicker.offlineHint'),
  loading: t('common.loading'), // 包用 labels.loading;tenon 复用 common.loading
  empty: t('iconPicker.empty'),
  more: t('iconPicker.more', { n: '{n}' }), // 透传字面量 {n} 给包(否则 vue-i18n 会吃掉占位,计数丢失)
}))
</script>

<template>
  <!-- 选择器 chrome 用 ph(已预热,离线优先);tenon 语言包驱动全部文案 -->
  <IconPicker
    v-model="model"
    :labels="labels"
    :clearable="clearable"
    search-icon="ph:magnifying-glass"
    clear-icon="ph:x"
  />
</template>
