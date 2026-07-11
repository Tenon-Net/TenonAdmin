# FileUpload

上传触发器。封 `n-upload` 的 `:custom-request`,内部 `await fileApi.upload(file.file)`(走 api 层,自动带 Bearer、自动补 multipart boundary),成功 `emit('uploaded', out)`。

`inheritAttrs:false` + `v-bind="$attrs"`:`accept`、`multiple`、`show-file-list`、`:max` 等一切透传给 `n-upload`,不重复声明。默认 slot 即触发器(通常放一颗按钮)。

## Props

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `chunked` | `boolean` | `false` | 走分片 / 断点续传 / 秒传(`utils/chunkUpload`):切片 + 算整文件 SHA-256 → `chunk/init`(秒传探测 / 已收分片)→ 并发传缺失片 → `chunk/complete`(服务端合并 + 哈希校验 + 三道关);进度回传 n-upload 进度条。默认 `false` 走单次 `fileApi.upload`。 |

## Emits

| 事件 | 载荷 | 说明 |
|---|---|---|
| `uploaded` | `FileUploadOutput` | 单个文件上传成功后触发(多选时每个文件各触发一次)。列表页据此 `refresh`。 |

## 用法

```vue
<!-- 文件管理页工具栏:纯触发器,不显示 n-upload 自带文件列表,上传成功刷新表格 -->
<FileUpload :show-file-list="false" @uploaded="() => tableRef?.refresh()">
  <n-button type="primary">
    <template #icon><AppIcon icon="ph:upload-simple" :size="16" /></template>{{ t('file.upload') }}
  </n-button>
</FileUpload>
```

失败在组件内 `message.error(translateError(e))` 并 `onError()`(n-upload 该项标红);成功 `onFinish()`。
