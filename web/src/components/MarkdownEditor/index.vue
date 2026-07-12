<script setup lang="ts">
// 通知公告 Markdown 编辑器:封 md-editor-v3,跟随应用明暗主题。存 Markdown 纯文本
//(不存 HTML → 前台 MarkdownView 用库自带渲染器展示,无 v-html XSS 面)。
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

// ponytail: 图片走 fileApi.upload 回 storagePath 当 URL;能否显示取决于后端是否静态托管该路径
//(本地存储默认多可直连)。后端不托管则仅影响"上传图片"子功能,不阻塞文字/链接编辑。
async function onUploadImg(files: File[], callback: (urls: string[]) => void) {
  const outs = await Promise.all(files.map((f) => fileApi.upload(f)))
  callback(outs.map((o) => o.storagePath))
}
</script>

<template>
  <MdEditor v-model="text" :theme="theme" @on-upload-img="onUploadImg" />
</template>
