<script setup lang="ts">
/**
 * 流程定义列表。菜单 component 填 `workflow/definition/index`。
 * 「新建」与「设计」都跳设计器(`/workflow/definition/designer`,可带 `?id=`)——
 * 建草稿的逻辑留在设计器的空态里,本页不重复一份。
 */
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NDropdown, NEmpty, NSpace, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { wfDefinitionApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfDefinitionRow } from '@/types/workflow'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const { confirm } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<WfDefinitionRow>>()

// 后端 WfDefinitionStatus:0 草稿 / 1 已发布 / 2 停用(与 detail.vue 的实例状态同用就地数字表)。
const DRAFT = 0
const PUBLISHED = 1
const DISABLED = 2

const statusOptions = [
  { label: () => t('workflow.definition.status.draft'), value: DRAFT, tagType: 'default' as const },
  { label: () => t('workflow.definition.status.published'), value: PUBLISHED, tagType: 'success' as const },
  { label: () => t('workflow.definition.status.disabled'), value: DISABLED, tagType: 'warning' as const },
]

function openDesigner(r?: WfDefinitionRow) {
  const id = r?.id
  void router.push(id ? `/workflow/definition/designer?id=${id}` : '/workflow/definition/designer')
}

async function onPublish(r: WfDefinitionRow) {
  const ok = await confirm({
    content: t('workflow.definition.publishConfirm', { name: r.name ?? '' }),
    action: () => wfDefinitionApi.publish(Number(r.id)),
    successMsg: t('workflow.definition.publishOk'),
  })
  if (ok) await tableRef.value?.refresh()
}

async function onDisable(r: WfDefinitionRow) {
  const ok = await confirm({
    content: t('workflow.definition.disableConfirm', { name: r.name ?? '' }),
    type: 'warning',
    action: () => wfDefinitionApi.disable(Number(r.id)),
    successMsg: t('workflow.definition.disableOk'),
  })
  if (ok) await tableRef.value?.refresh()
}

async function onDelete(r: WfDefinitionRow) {
  const ok = await confirm({
    content: t('workflow.definition.deleteConfirm', { name: r.name ?? '' }),
    type: 'warning',
    action: () => wfDefinitionApi.remove(Number(r.id)),
    successMsg: t('common.deleted'),
  })
  if (ok) await tableRef.value?.refresh()
}

const columns: ProTableColumn<WfDefinitionRow>[] = [
  {
    key: 'name',
    title: () => t('workflow.definition.name'),
    search: true,
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (r) =>
      h(NSpace, { align: 'center', size: 6, wrapItem: false }, () => [
        r.icon ? h(AppIcon, { icon: r.icon, size: 18 }) : null,
        r.name || '—',
      ]),
  },
  {
    key: 'groupName',
    title: () => t('workflow.definition.group'),
    search: true,
    width: 140,
    ellipsis: { tooltip: true },
    render: (r) => r.groupName || '—',
  },
  { key: 'status', title: () => t('common.status'), width: 110, tag: true, search: true, options: statusOptions },
  {
    key: 'currentVersion',
    title: () => t('workflow.definition.version'),
    width: 100,
    align: 'center',
    // 草稿的 currentVersion 是 0:显示 v0 会被当成「有个 0 版」,直接给「未发布」更实。
    render: (r) =>
      Number(r.currentVersion) >= 1
        ? `v${r.currentVersion}`
        : h('span', { style: 'color:var(--color-text-tertiary);' }, t('workflow.definition.unpublished')),
  },
  { key: 'createTime', title: () => t('common.createTime'), width: 170, format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 150,
    fixed: 'right',
    hideInSetting: true,
    render: (r) => {
      // 发布/停用按各自权限码显隐,且按当前状态取舍:已发布不再重复给「发布」,已停用不再给「停用」。
      const dropdownOptions = [
        authStore.hasPerm('POST:/api/v1/workflow/definition/publish')
          ? { key: 'publish', label: t('workflow.definition.publish') }
          : null,
        authStore.hasPerm('POST:/api/v1/workflow/definition/disable') && Number(r.status) !== DISABLED
          ? { key: 'disable', label: t('workflow.definition.disable') }
          : null,
        authStore.hasPerm('DELETE:/api/v1/workflow/definition/{id}')
          ? { key: 'delete', label: t('common.delete') }
          : null,
      ].filter((o): o is { key: string; label: string } => o !== null)

      return h(NSpace, { size: 2, wrapItem: false }, () => [
        authStore.hasPerm('GET:/api/v1/workflow/definition/{id}')
          ? h(
              NButton,
              { size: 'small', quaternary: true, type: 'primary', onClick: () => openDesigner(r) },
              () => t('workflow.definition.design'),
            )
          : null,
        dropdownOptions.length
          ? h(
              NDropdown,
              {
                trigger: 'click',
                options: dropdownOptions,
                onSelect: (key: string) => {
                  if (key === 'publish') void onPublish(r)
                  else if (key === 'disable') void onDisable(r)
                  else void onDelete(r)
                },
              },
              () => h(NButton, { size: 'small', quaternary: true }, () => t('common.more')),
            )
          : null,
      ])
    },
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-definition"
    row-key="id"
    :columns="columns"
    :fetcher="wfDefinitionApi.page"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button
        v-auth="'POST:/api/v1/workflow/definition/add'"
        type="primary"
        @click="openDesigner()"
      >
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>
        {{ t('workflow.definition.create') }}
      </n-button>
    </template>
    <template #empty>
      <n-empty :description="t('workflow.definition.empty')" size="small" />
    </template>
  </ProTable>
</template>
