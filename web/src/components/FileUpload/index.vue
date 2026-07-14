<script setup lang="ts">
// FileUpload:封 n-upload 的 custom-request,内部走 api 层上传(自动带 Bearer),成功 emit('uploaded', out)。
// props 最小:accept/multiple/show-file-list 等一切经 $attrs 透传;默认 slot 作触发器。
// chunked=true 走分片/断点续传/秒传编排(uploadChunked),并把进度回给 n-upload 进度条。
import { NUpload, useMessage, type UploadCustomRequestOptions } from 'naive-ui'
import { fileApi } from '@/api'
import { uploadChunked } from '@/utils/chunkUpload'
import { translateError } from '@/utils/error'
import type { FileUploadOutput } from '@/types/api'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ chunked?: boolean }>()
// loadingChange:上传真正开始/结束时各发一次(try/finally 兜住),给触发器加"上传中"反馈用。
const emit = defineEmits<{ uploaded: [out: FileUploadOutput]; loadingChange: [loading: boolean] }>()
const message = useMessage()

// n-upload 每个文件回调一次;file.file 是原始 File。成/败调 onFinish/onError 让 n-upload 更新该项状态。
async function customRequest({ file, onFinish, onError, onProgress }: UploadCustomRequestOptions) {
  if (!file.file) {
    onError()
    return
  }
  emit('loadingChange', true)
  try {
    const out = props.chunked
      ? await uploadChunked(file.file, (percent) => onProgress({ percent }))
      : await fileApi.upload(file.file)
    emit('uploaded', out)
    onFinish()
  } catch (e) {
    message.error(translateError(e))
    onError()
  } finally {
    emit('loadingChange', false)
  }
}
</script>

<template>
  <n-upload v-bind="$attrs" :custom-request="customRequest">
    <slot />
  </n-upload>
</template>
