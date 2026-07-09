<script setup lang="ts">
import { h } from 'vue'
import { NCard, NFormItemGi, NGrid, NInput, NButton, NSpace, NDataTable, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { userApi } from '@/api'
import { useTable } from '@/composables/useTable'
import { translateError } from '@/utils/error'
import type { UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()

const { loading, rows, params, pagination, search, onPage, onPageSize } = useTable<UserItem>(
  (p) => userApi.page({ page: p.page, pageSize: p.pageSize, account: p.account as string, name: p.name as string }),
  { initParams: { account: '', name: '' }, onError: (e) => message.error(translateError(e)) },
)

// DataTableColumns 是 Naive 专有类型,只出现在视图层(设计 §7.2 边界)。
const columns: DataTableColumns<UserItem> = [
  { title: () => t('user.account'), key: 'account' },
  { title: () => t('user.name'), key: 'name' },
  {
    title: () => t('user.status'),
    key: 'enabled',
    render: (r) =>
      h(NTag, { type: r.enabled ? 'success' : 'default', size: 'small', bordered: false }, () =>
        r.enabled ? t('user.enabled') : t('user.disabled'),
      ),
  },
  {
    title: () => t('user.superAdmin'),
    key: 'isSuperAdmin',
    render: (r) => (r.isSuperAdmin ? h(NTag, { type: 'warning', size: 'small', bordered: false }, () => t('user.superAdmin')) : '—'),
  },
  { title: () => t('user.createTime'), key: 'createTime', render: (r) => (r.createTime ? r.createTime.replace('T', ' ').slice(0, 19) : '') },
]

function reset() {
  params.account = ''
  params.name = ''
  search()
}
</script>

<template>
  <div class="page">
    <n-card :bordered="true" class="filter">
      <n-grid :cols="'1 s:2 m:3'" responsive="screen" :x-gap="16" :y-gap="12">
        <n-form-item-gi :label="t('user.account')">
          <n-input v-model:value="(params.account as string)" clearable :placeholder="t('user.account')" @keyup.enter="search" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.name')">
          <n-input v-model:value="(params.name as string)" clearable :placeholder="t('user.name')" @keyup.enter="search" />
        </n-form-item-gi>
        <n-form-item-gi>
          <n-space>
            <n-button type="primary" @click="search"><template #icon><Icon icon="ph:magnifying-glass" /></template>{{ t('common.search') }}</n-button>
            <n-button @click="reset">{{ t('common.reset') }}</n-button>
          </n-space>
        </n-form-item-gi>
      </n-grid>
    </n-card>

    <n-card :bordered="true">
      <div class="bar">
        <h3>{{ t('user.title') }}</h3>
      </div>
      <n-data-table
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="(r: UserItem) => r.id"
        remote
        :pagination="{
          page: pagination.page,
          pageSize: pagination.pageSize,
          itemCount: pagination.itemCount,
          showSizePicker: true,
          pageSizes: [10, 20, 50],
          onUpdatePage: onPage,
          onUpdatePageSize: onPageSize,
        }"
      />
    </n-card>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.bar {
  margin-bottom: 12px;
}
.bar h3 {
  font-size: var(--font-size-md);
  font-weight: 600;
  color: var(--color-text-primary);
}
</style>
