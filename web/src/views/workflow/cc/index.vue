<script setup lang="ts">
/**
 * 抄送我的。菜单 component 填 `workflow/cc/index`。
 * 行点「查看」进实例详情;详情 GET 会把该用户本实例未读行标已读。
 */
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { wfCcApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfCcItem } from '@/types/workflow'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const tableRef = ref<ProTableInst<WfCcItem>>()

function openDetail(r: WfCcItem) {
  void router.push(`/workflow/instance/${r.instanceId}/detail`)
}

const columns: ProTableColumn<WfCcItem>[] = [
  { key: 'definitionName', title: () => t('workflow.cc.definition'), minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'nodeName',
    title: () => t('workflow.cc.node'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.nodeName || r.nodeId,
  },
  {
    key: 'isRead',
    title: () => t('workflow.cc.readState'),
    width: 88,
    render: (r) =>
      h(
        NTag,
        { size: 'small', type: r.isRead ? 'default' : 'info', bordered: false },
        () => (r.isRead ? t('workflow.cc.read') : t('workflow.cc.unread')),
      ),
  },
  {
    key: 'businessKey',
    title: () => t('workflow.cc.businessKey'),
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.businessKey || '—',
  },
  { key: 'createTime', title: () => t('workflow.cc.createTime'), width: 170, format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 100,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(
          NButton,
          { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) },
          () => t('workflow.cc.view'),
        ),
      ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-cc"
    row-key="id"
    :columns="columns"
    :fetcher="wfCcApi.page"
    @error="(e) => message.error(translateError(e))"
  >
    <template #empty>
      <n-empty :description="t('workflow.cc.empty')" size="small" />
    </template>
  </ProTable>
</template>
