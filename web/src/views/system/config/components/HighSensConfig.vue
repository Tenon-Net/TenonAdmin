<script setup lang="ts">
// 高敏权限自定义追加:默认集只读展示;追加/删除走 highSensApi(须 reauth 的写路径由后端强制)。
import { h, onMounted, ref } from 'vue'
import { NButton, NDataTable, NInput, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { highSensApi } from '@/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const loading = ref(false)
const defaults = ref<string[]>([])
const customs = ref<{ id: number; permissionCode: string; remark?: string | null }[]>([])
const newCode = ref('')
const newRemark = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    const data = await highSensApi.list()
    defaults.value = data.defaults ?? []
    customs.value = (data.customs ?? []).flatMap((item) =>
      item.id == null
        ? []
        : [{ id: Number(item.id), permissionCode: item.permissionCode ?? '', remark: item.remark }],
    )
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

async function add() {
  if (!newCode.value.trim()) {
    message.warning(t('config.security.highSens.codeRequired'))
    return
  }
  saving.value = true
  try {
    await highSensApi.add({ permissionCode: newCode.value.trim(), remark: newRemark.value.trim() || undefined })
    message.success(t('common.success'))
    newCode.value = ''
    newRemark.value = ''
    await load()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}

async function remove(id: number) {
  saving.value = true
  try {
    await highSensApi.remove(id)
    message.success(t('common.success'))
    await load()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}

const customCols: DataTableColumns<(typeof customs.value)[0]> = [
  { title: () => t('config.security.highSens.code'), key: 'permissionCode', ellipsis: { tooltip: true } },
  { title: () => t('config.security.highSens.remark'), key: 'remark', width: 160 },
  {
    title: () => t('common.action'),
    key: 'a',
    width: 100,
    render: (row) =>
      h(
        NButton,
        { size: 'small', type: 'error', quaternary: true, loading: saving.value, onClick: () => remove(row.id) },
        { default: () => t('common.delete') },
      ),
  },
]

onMounted(() => void load())
</script>

<template>
  <div>
    <p style="margin: 0 0 12px; color: var(--n-text-color-3); font-size: 13px">
      {{ t('config.security.highSens.hint') }}
    </p>
    <div style="margin-bottom: 12px">
      <n-tag v-for="d in defaults" :key="d" size="small" style="margin: 0 6px 6px 0">{{ d }}</n-tag>
      <span v-if="!defaults.length && !loading" style="color: var(--n-text-color-3)">—</span>
    </div>
    <n-space style="margin-bottom: 12px" align="center">
      <n-input v-model:value="newCode" :placeholder="t('config.security.highSens.codePh')" style="width: 320px" />
      <n-input v-model:value="newRemark" :placeholder="t('config.security.highSens.remarkPh')" style="width: 180px" />
      <n-button type="primary" :loading="saving" @click="add">{{ t('common.add') }}</n-button>
    </n-space>
    <n-data-table :columns="customCols" :data="customs" :loading="loading" size="small" :bordered="false" />
  </div>
</template>
