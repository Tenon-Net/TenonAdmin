<script setup lang="ts">
import { h, ref } from 'vue'
import { NButton, NSpace, NTabs, NTabPane, NPopconfirm, useMessage } from 'naive-ui'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { recycleApi, type RecycleBinItem } from '@/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const types = ['user', 'role', 'org', 'position', 'module', 'config', 'dict', 'menu', 'job'] as const
type RecycleType = (typeof types)[number]
const activeTab = ref<RecycleType>('user')
const tableRefs = ref<Record<string, ProTableInst<RecycleBinItem>>>({})

function setTableRef(type: string) {
  return (el: any) => { if (el) tableRefs.value[type] = el }
}

function refreshTab(type: string) {
  tableRefs.value[type]?.refresh()
}

const columns: ProTableColumn<RecycleBinItem>[] = [
  { key: 'name', title: () => t('recycle.name') },
  { key: 'code', title: () => t('recycle.code') },
  { key: 'deletedAt', title: () => t('recycle.deletedAt'), format: 'datetime', width: 180 },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 180,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('POST:/api/v1/sys/recycle/{type}/{id}/restore')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => recycleApi.restore(activeTab.value, r.id), t('recycle.restored')).then((ok) => {
                    if (ok) refreshTab(activeTab.value)
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'primary' }, () => t('recycle.restore')),
                default: () => t('recycle.restoreConfirm', { name: r.name }),
              },
            )
          : null,
        authStore.hasPerm('DELETE:/api/v1/sys/recycle/{type}/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => recycleApi.purge(activeTab.value, r.id), t('recycle.purged')).then((ok) => {
                    if (ok) refreshTab(activeTab.value)
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('recycle.purge')),
                default: () => t('recycle.purgeConfirm', { name: r.name }),
              },
            )
          : null,
      ]),
  },
]
</script>

<template>
  <div class="view">
    <n-tabs v-model:value="activeTab" type="line" animated>
      <n-tab-pane v-for="type in types" :key="type" :name="type" :tab="t(`recycle.tabs.${type}`)">
        <ProTable
          :ref="setTableRef(type)"
          :columns="columns"
          :fetcher="recycleApi.page(type)"
          :storage-key="`sys-recycle-${type}`"
          @error="(e: unknown) => message.error(translateError(e))"
        />
      </n-tab-pane>
    </n-tabs>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
</style>
