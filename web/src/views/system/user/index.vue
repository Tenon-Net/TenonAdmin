<script setup lang="ts">
// 用户列表 = tenon-naive-pro-table 的列驱动用法:search 标记生成搜索表单,
// options+tag 做字典渲染,分页/竞态/工具栏/列设置全在包内。消息提示留在视图层(设计 §7.2)。
import { h } from 'vue'
import { NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { userApi } from '@/api'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { translateError } from '@/utils/error'
import type { UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const labels = useProTableLabels()

const columns: ProTableColumn<UserItem>[] = [
  { key: 'account', title: () => t('user.account'), search: true },
  { key: 'name', title: () => t('user.name'), search: true },
  {
    key: 'enabled',
    title: () => t('user.status'),
    tag: true,
    options: [
      { label: () => t('user.enabled'), value: true, tagType: 'success' },
      { label: () => t('user.disabled'), value: false },
    ],
  },
  {
    key: 'isSuperAdmin',
    title: () => t('user.superAdmin'),
    render: (r) =>
      r.isSuperAdmin ? h(NTag, { type: 'warning', size: 'small', bordered: false }, () => t('user.superAdmin')) : '—',
  },
  { key: 'createTime', title: () => t('user.createTime'), format: 'datetime' },
]
</script>

<template>
  <ProTable
    :columns="columns"
    :fetcher="userApi.page"
    :title="t('user.title')"
    :labels="labels"
    storage-key="sys-user"
    @error="(e) => message.error(translateError(e))"
  />
</template>
