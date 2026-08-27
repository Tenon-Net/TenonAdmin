<script setup lang="ts">
/**
 * 我已办的。菜单 component 填 `workflow/done/index`。
 * 行点击或「查看」进实例详情(`/workflow/instance/:id/detail`)。
 */
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { wfTaskApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfDoneItem, WfInstanceStatus, WfTaskAction } from '@/types/workflow'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const tableRef = ref<ProTableInst<WfDoneItem>>()

function openDetail(r: WfDoneItem) {
  if (r.instanceId == null) return
  void router.push(`/workflow/instance/${r.instanceId}/detail`)
}

/** 与 detail.vue 同一套实例状态 / 动作数字表,不另起体系。 */
function normalizeStatus(s: WfInstanceStatus | undefined): string {
  if (s == null) return 'unknown'
  const map: Record<number, string> = {
    1: 'running',
    2: 'approved',
    3: 'rejected',
    4: 'cancelled',
    5: 'terminated',
  }
  return map[s] ?? 'unknown'
}

function statusLabel(s: WfInstanceStatus | undefined): string {
  return t(`workflow.status.${normalizeStatus(s)}`, String(s ?? ''))
}

function statusType(s: WfInstanceStatus | undefined): 'default' | 'info' | 'success' | 'error' | 'warning' {
  const key = normalizeStatus(s)
  if (key === 'approved') return 'success'
  if (key === 'rejected' || key === 'terminated') return 'error'
  if (key === 'cancelled') return 'warning'
  if (key === 'running') return 'info'
  return 'default'
}

function actionLabel(a: WfTaskAction | undefined): string {
  if (a == null) return t('workflow.action.unknown')
  const map: Record<number, string> = {
    1: 'approve',
    2: 'reject',
    3: 'transfer',
    4: 'return',
    5: 'withdraw',
    6: 'delegate',
  }
  const key = map[a] ?? 'unknown'
  return t(`workflow.action.${key}`, String(a))
}

function actionTagType(a: WfTaskAction | undefined): 'default' | 'success' | 'error' | 'warning' | 'info' {
  if (a === 1) return 'success'
  if (a === 2) return 'error'
  if (a === 3) return 'warning'
  return 'info'
}

const columns: ProTableColumn<WfDoneItem>[] = [
  { key: 'definitionName', title: () => t('workflow.done.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'nodeName',
    title: () => t('workflow.done.node'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.nodeName || r.nodeId,
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
