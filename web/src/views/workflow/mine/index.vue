<script setup lang="ts">
/**
 * 我发起的。菜单 component 填 `workflow/mine/index`。
 * 行点击或「查看」进实例详情(`/workflow/instance/:id/detail`)。
 */
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { wfInstanceApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfInstanceListItem, WfInstanceStatus } from '@/types/workflow'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const tableRef = ref<ProTableInst<WfInstanceListItem>>()

function openDetail(r: WfInstanceListItem) {
  if (r.id == null) return
  void router.push(`/workflow/instance/${r.id}/detail`)
}

/** 与 detail.vue 同一套实例状态数字表,不另起体系。 */
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

const columns: ProTableColumn<WfInstanceListItem>[] = [
  { key: 'definitionName', title: () => t('workflow.mine.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'version',
    title: () => t('workflow.mine.version'),
    width: 80,
    align: 'center',
    render: (r) => (r.version == null ? '—' : `v${r.version}`),
  },
  {
    key: 'businessKey',
    title: () => t('workflow.mine.businessKey'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.businessKey || '—',
  },
  {
    key: 'status',
    title: () => t('common.status'),
    width: 96,
    render: (r) =>
      h(NTag, { size: 'small', type: statusType(r.status), bordered: false }, () => statusLabel(r.status)),
  },
  { key: 'createTime', title: () => t('workflow.mine.createTime'), width: 170, format: 'datetime' },
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
          () => t('workflow.mine.view'),
        ),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-mine"
    row-key="id"
    :columns="columns"
    :fetcher="wfInstanceApi.page"
    @row-click="(row: WfInstanceListItem) => openDetail(row)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #empty>
      <n-empty :description="t('workflow.mine.empty')" size="small" />
    </template>
  </ProTable>
</template>
