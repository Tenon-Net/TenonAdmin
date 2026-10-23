<script setup lang="ts">
/**
 * 我已办的。菜单 component 填 `workflow/done/index`。
 * 行点击或「查看」进实例详情(`/workflow/instance/:id/detail`)。
 */
import { h, onActivated, shallowRef, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { wfTaskApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfDoneItem, WfInstanceStatus, WfTaskAction } from '@/types/workflow'
import { instanceStatusKey, instanceStatusTagType, taskActionKey, taskActionTagType } from '@/workflow/statusLabels'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const tableRef = useTemplateRef<ProTableInst<WfDoneItem>>('tableRef')

function openDetail(r: WfDoneItem) {
  if (r.instanceId == null) return
  void router.push({
    path: `/workflow/instance/${r.instanceId}/detail`,
    query: { from: '/workflow/done' },
  })
}

// 列表被 keep-alive 缓存。首次激活交给 ProTable 自己拉数,回到本页时重拉。
const skipRefresh = shallowRef(true)
onActivated(() => {
  if (skipRefresh.value) {
    skipRefresh.value = false
    return
  }
  void tableRef.value?.refresh()
})

function statusLabel(s: WfInstanceStatus | undefined): string {
  return t(`workflow.status.${instanceStatusKey(s)}`, String(s ?? ''))
}

const statusType = instanceStatusTagType

function actionLabel(a: WfTaskAction | undefined): string {
  return t(`workflow.action.${taskActionKey(a)}`, String(a ?? ''))
}

const actionTagType = taskActionTagType

const columns: ProTableColumn<WfDoneItem>[] = [
  { key: 'definitionName', title: () => t('workflow.done.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'starterName',
    title: () => t('workflow.done.starter'),
    width: 120,
    ellipsis: { tooltip: true },
    render: (r) => r.starterName?.trim() || '—',
  },
  {
    key: 'summary',
    title: () => t('workflow.done.summary'),
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (r) => r.summary?.trim() || '—',
  },
  {
    key: 'nodeName',
    title: () => t('workflow.done.node'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.nodeName || '—',
  },
  {
    key: 'action',
    title: () => t('workflow.done.action'),
    width: 88,
    render: (r) =>
      h(NTag, { size: 'small', type: actionTagType(r.action), bordered: false }, () => actionLabel(r.action)),
  },
  {
    key: 'businessKey',
    title: () => t('workflow.done.businessKey'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.businessKey || '—',
  },
  {
    key: 'instanceStatus',
    title: () => t('common.status'),
    width: 96,
    render: (r) =>
      h(
        NTag,
        { size: 'small', type: statusType(r.instanceStatus), bordered: false },
        () => statusLabel(r.instanceStatus),
      ),
  },
  { key: 'createTime', title: () => t('workflow.done.createTime'), width: 170, format: 'datetime' },
  {
    key: 'comment',
    title: () => t('workflow.done.comment'),
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (r) => r.comment?.trim() || '—',
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 100,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(
          NButton,
          {
            size: 'small',
            quaternary: true,
            type: 'primary',
            onClick: (e: MouseEvent) => {
              e.stopPropagation()
              openDetail(r)
            },
          },
          () => t('workflow.done.view'),
        ),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-done"
    row-key="hisTaskId"
    :columns="columns"
    :fetcher="wfTaskApi.done"
    @row-click="(row: WfDoneItem) => openDetail(row)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #empty>
      <n-empty :description="t('workflow.done.empty')" size="small" />
    </template>
  </ProTable>
</template>
