<script setup lang="ts">
/**
 * 待我审批列表。菜单 component 填 `workflow/todo/index`。
 * 行点「办理」进实例详情(`/workflow/instance/:id/detail`)。
 */
import { h, onActivated, onDeactivated, onScopeDispose, shallowRef, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { wfTaskApi } from '@/api/workflow'
import { workflowTodoBus } from '@/composables/useRealtime'
import { translateError } from '@/utils/error'
import type { WfTodoItem } from '@/types/workflow'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const tableRef = useTemplateRef<ProTableInst<WfTodoItem>>('tableRef')

function openDetail(r: WfTodoItem) {
  void router.push({
    path: `/workflow/instance/${r.instanceId}/detail`,
    query: { from: '/workflow/todo' },
  })
}

// 列表被 keep-alive 缓存。首次激活交给 ProTable 自己拉数,从详情办理完再回来时重拉行和总数。
const skipRefresh = shallowRef(true)
// #53:实时推送新待办/撤回/加减签时自动重拉;keep-alive 失活期间不拉,重新激活时由 onActivated 刷新。
const active = shallowRef(true)
onDeactivated(() => { active.value = false })
const offTodo = workflowTodoBus.on(() => { if (active.value) void tableRef.value?.refresh() })
onScopeDispose(offTodo)
onActivated(() => {
  active.value = true
  if (skipRefresh.value) {
    skipRefresh.value = false
    return
  }
  void tableRef.value?.refresh()
})

function isOverdue(r: WfTodoItem) {
  if (!r.dueTime) return false
  const ts = Date.parse(r.dueTime)
  return Number.isFinite(ts) && ts < Date.now()
}

const columns: ProTableColumn<WfTodoItem>[] = [
  { key: 'definitionName', title: () => t('workflow.todo.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'starterName',
    title: () => t('workflow.todo.starter'),
    width: 120,
    ellipsis: { tooltip: true },
    render: (r) => r.starterName?.trim() || '—',
  },
  {
    key: 'summary',
    title: () => t('workflow.todo.summary'),
    minWidth: 200,
    ellipsis: { tooltip: true },
    render: (r) => r.summary?.trim() || '—',
  },
  {
    key: 'nodeName',
    title: () => t('workflow.todo.node'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.nodeName || '—',
  },
  {
    key: 'status',
    title: () => t('common.status'),
    width: 96,
    render: () =>
      h(NTag, { size: 'small', type: 'info', bordered: false }, () => t('workflow.todo.pending')),
  },
  {
    key: 'businessKey',
    title: () => t('workflow.todo.businessKey'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.businessKey || '—',
  },
  { key: 'createTime', title: () => t('workflow.todo.createTime'), width: 170, format: 'datetime' },
  {
    key: 'dueTime',
    title: () => t('workflow.todo.dueTime'),
    width: 170,
    render: (r) => {
      if (!r.dueTime) return '—'
      const text = r.dueTime.replace('T', ' ').slice(0, 16)
      if (!isOverdue(r)) return text
      return h(NTag, { size: 'small', type: 'warning', bordered: false }, () => `${t('workflow.todo.overdue')} ${text}`)
    },
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 100,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) }, () => t('workflow.todo.handle')),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-todo"
    row-key="taskId"
    :columns="columns"
    :fetcher="wfTaskApi.todo"
    @error="(e) => message.error(translateError(e))"
  >
    <template #empty>
      <n-empty :description="t('workflow.todo.empty')" size="small" />
    </template>
  </ProTable>
</template>
