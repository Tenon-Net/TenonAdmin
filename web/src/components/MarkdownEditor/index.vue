<script setup lang="ts">
// 通知公告 Markdown 编辑器:封 md-editor-v3,跟随应用明暗主题。存 Markdown 源文本 —— 注意 Markdown
// **天然放行内联 HTML**,不等于无 XSS 面;渲染安全由 main.ts 的 setupMarkdown() 全局挂的 XSSPlugin 兜底
//(见 @/lib/markdown)。no-* 关掉会从 unpkg 懒加载的扩展(见 lib/markdown),气隙下零触网。
import { computed } from 'vue'
import { MdEditor } from 'md-editor-v3'
import 'md-editor-v3/lib/style.css'
import { useAppStore } from '@/stores/app'
import { fileApi } from '@/api'

const props = defineProps<{ value?: string | null }>()
const emit = defineEmits<{ 'update:value': [v: string] }>()
const app = useAppStore()

const text = computed({ get: () => props.value ?? '', set: (v) => emit('update:value', v) })
const theme = computed(() => (app.isDark ? 'dark' : 'light'))

// 图片插的是后端签发的**签名直链**(viewUrl),不是 storagePath —— 后端默认不静态托管上传目录
//(那等于鉴权绕过),storagePath 当 src 必然是坏链。viewUrl 匿名可取但不可伪造,<img> 直接能加载。
async function onUploadImg(files: File[], callback: (urls: string[]) => void) {
  const outs = await Promise.all(files.map((f) => fileApi.upload(f)))
  callback(outs.map((o) => o.viewUrl ?? ''))
}
</script>

<template>
  <MdEditor v-model="text" :theme="theme" no-katex no-mermaid no-highlight no-prettier @on-upload-img="onUploadImg" />
</template>
