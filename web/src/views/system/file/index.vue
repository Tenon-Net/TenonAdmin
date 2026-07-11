<script setup lang="ts">
// 文件管理 = 只读 ProTable(搜索 originalName→FileName)+ 工具栏 FileUpload 触发器 + 行内下载/删除。
// 下载走 blob(Bearer 自动带)→ createObjectURL + <a download> + revoke;删除走 useConfirm。
import { h, ref } from 'vue'
import { NButton, NSpace, NPopconfirm, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FileUpload from '@/components/FileUpload/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { fileApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysFile } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysFile>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: fileApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('file.deleted'),
})

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`
}

async function download(r: SysFile) {
  try {
    const blob = await fileApi.download(r.id)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = r.originalName
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    message.error(translateError(e))
  }
}

const columns: ProTableColumn<SysFile>[] = [
  { type: 'selection' },
  { key: 'originalName', title: () => t('file.name'), search: true, ellipsis: { tooltip: true } },
  { key: 'extension', title: () => t('file.extension'), width: 90, render: (r) => r.extension || '—' },
  { key: 'sizeBytes', title: () => t('file.size'), width: 110, render: (r) => formatSize(r.sizeBytes) },
  { key: 'contentType', title: () => t('file.contentType'), ellipsis: { tooltip: true }, render: (r) => r.contentType || '—' },
  { key: 'createTime', title: () => t('file.uploadTime'), format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(
          NButton,
          { size: 'small', quaternary: true, type: 'primary', onClick: () => download(r) },
          { default: () => t('file.download'), icon: () => h(AppIcon, { icon: 'ph:download-simple', size: 15 }) },
        ),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => fileApi.remove(r.id), t('file.deleted')).then((ok) => {
                if (ok) tableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('file.deleteConfirm', { name: r.originalName }),
          },
        ),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="fileApi.page"
    :title="t('file.title')"
    :labels="labels"
    storage-key="sys-file"
    :checked-row-keys="checkedKeys"
    @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <FileUpload v-auth="'POST:/api/v1/sys/file/upload'" :show-file-list="false" @uploaded="() => tableRef?.refresh()">
        <n-button type="primary">
          <template #icon><AppIcon icon="ph:upload-simple" :size="16" /></template>{{ t('file.upload') }}
        </n-button>
      </FileUpload>
      <n-button
        v-auth="'POST:/api/v1/sys/file/batch-delete'"
        type="error"
        :disabled="!hasSelection"
        @click="batchDelete"
      >
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('common.batchDelete') }}
      </n-button>
    </template>
  </ProTable>
</template>
