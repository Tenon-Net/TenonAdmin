<script setup lang="ts">
// 只读渲染 Markdown(MdPreview,较 MdEditor 轻)。跟随应用明暗主题。通知详情/列表展示用。
// XSS 过滤由 main.ts 的 setupMarkdown() 全局兜底(@/lib/markdown)—— MdPreview 默认会渲染正文里的内联 HTML,
// 必须过滤。no-* 关掉会从 unpkg 懒加载的扩展,气隙下零触网。
import { computed } from 'vue'
import { MdPreview } from 'md-editor-v3'
import 'md-editor-v3/lib/style.css'
import { useAppStore } from '@/stores/app'

const props = defineProps<{ value?: string | null }>()
const app = useAppStore()
const theme = computed(() => (app.isDark ? 'dark' : 'light'))
</script>

<template>
  <MdPreview :model-value="props.value ?? ''" :theme="theme" no-katex no-mermaid no-highlight />
</template>
