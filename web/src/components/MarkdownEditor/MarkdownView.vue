<script setup lang="ts">
// 只读渲染 Markdown(MdPreview,较 MdEditor 轻)。跟随应用明暗主题。通知详情/列表展示用。
import { computed } from 'vue'
import { MdPreview } from 'md-editor-v3'
import 'md-editor-v3/lib/style.css'
import { useAppStore } from '@/stores/app'

const props = defineProps<{ value?: string | null }>()
const app = useAppStore()
const theme = computed(() => (app.isDark ? 'dark' : 'light'))
</script>

<template>
  <MdPreview :model-value="props.value ?? ''" :theme="theme" />
</template>
