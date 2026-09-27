<script setup lang="ts">
/**
 * 流程监控。菜单 component 填 `workflow/monitor/index`。
 * 参与筛选是业务过滤,不是数据权限;行点击进同一详情。
 */
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import UserSelect from '@/components/UserSelect/index.vue'
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
  { key: 'definitionName', title: () => t('workflow.monitor.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'version',
    title: () => t('workflow.monitor.version'),
    width: 80,
    align: 'center',
    render: (r) => (r.version == null ? '—' : `v${r.version}`),
  },
  {
    key: 'starterUserId',
    title: () => t('workflow.monitor.starter'),
    width: 140,
    search: {
      key: 'starterUserId',
      label: () => t('workflow.monitor.starter'),
      render: ({ value, setValue, search }) =>
        h(UserSelect, {
          value: value as number | null,
          clearable: true,
          placeholder: t('workflow.monitor.starter'),
          'onUpdate:value': (v: number | null) => {
            setValue(v)
            search()
          },
        }),
    },
    render: (r) => (r.starterUserId == null ? '—' : t('workflow.detail.userFallback', { id: r.starterUserId })),
  },
  {
    key: 'actorUserId',
    title: () => t('workflow.monitor.actor'),
    width: 1,
    hideInTable: true,
    hideInSetting: true,
    search: {
      key: 'actorUserId',
      label: () => t('workflow.monitor.actor'),
      render: ({ value, setValue, search }) =>
        h(UserSelect, {
          value: value as number | null,
          clearable: true,
          placeholder: t('workflow.monitor.actor'),
          'onUpdate:value': (v: number | null) => {
            setValue(v)
            search()
          },
        }),
    },
  },
  {
    key: 'ccUserId',
    title: () => t('workflow.monitor.cc'),
    width: 1,
    hideInTable: true,
    hideInSetting: true,
    search: {
      key: 'ccUserId',
      label: () => t('workflow.monitor.cc'),
      render: ({ value, setValue, search }) =>
        h(UserSelect, {
          value: value as number | null,
          clearable: true,
          placeholder: t('workflow.monitor.cc'),
          'onUpdate:value': (v: number | null) => {
            setValue(v)
            search()
          },
        }),
    },
  },
  {
    key: 'businessKey',
    title: () => t('workflow.monitor.businessKey'),
    width: 140,
    ellipsis: { tooltip: true },
    search: { props: { clearable: true } },
    render: (r) => r.businessKey || '—',
  },
  {
    key: 'status',
    title: () => t('common.status'),
    width: 96,
    search: true,
    options: [
      { label: () => t('workflow.status.running'), value: 1 },
      { label: () => t('workflow.status.approved'), value: 2 },
      { label: () => t('workflow.status.rejected'), value: 3 },
      { label: () => t('workflow.status.cancelled'), value: 4 },
      { label: () => t('workflow.status.terminated'), value: 5 },
    ],
    render: (r) =>
      h(NTag, { size: 'small', type: statusType(r.status), bordered: false }, () => statusLabel(r.status)),
  },
  { key: 'createTime', title: () => t('workflow.monitor.createTime'), width: 170, format: 'datetime' },
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
          () => t('workflow.monitor.view'),
        ),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-monitor"
    row-key="id"
    :columns="columns"
    :fetcher="wfInstanceApi.monitor"
    @row-click="(row: WfInstanceListItem) => openDetail(row)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #empty>
      <n-empty :description="t('workflow.monitor.empty')" size="small" />
    </template>
  </ProTable>
</template>
