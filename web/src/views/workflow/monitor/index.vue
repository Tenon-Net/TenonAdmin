<script setup lang="ts">
/**
 * 流程监控。菜单 component 填 `workflow/monitor/index`。
 * 参与筛选是业务过滤,不是数据权限;行点击进同一详情。
 */
import { computed, h, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NForm, NFormItem, NInput, NModal, NSelect, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import UserSelect from '@/components/UserSelect/index.vue'
import { wfInstanceApi } from '@/api/workflow'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { defaultMonitorNodeId, monitorNodeOptions } from './nodes'
import type { WfInstanceListItem, WfInstanceStatus } from '@/types/workflow'
import { normalizeWfId, wfInstanceDetailPath, type WfId } from '@/workflow/id'
import { runtimeStatusKey } from '@/workflow/runtimeStatus'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<WfInstanceListItem>>()

type AdminKind = 'transfer' | 'terminate' | 'skip'
const adminPerm: Record<AdminKind, string> = {
  transfer: 'POST:/api/v1/workflow/instance/force-transfer',
  terminate: 'POST:/api/v1/workflow/instance/terminate',
  skip: 'POST:/api/v1/workflow/instance/skip',
}
const showAdmin = ref(false)
const adminKind = ref<AdminKind>('terminate')
const adminRow = ref<WfInstanceListItem | null>(null)
const reason = ref('')
const toUserId = ref<WfId | null>(null)
const nodeId = ref<string | null>(null)
const activeNodes = ref<{ id: string; name: string }[]>([])
const nodesLoading = ref(false)
const submitting = ref(false)
let nodeSeq = 0
const modalTitle = computed(() => t(`workflow.monitor.${adminKind.value === 'transfer' ? 'forceTransfer' : adminKind.value}`))

function isRunning(r: WfInstanceListItem) {
  return Number(r.status) === 1
}

function openAdmin(kind: AdminKind, row: WfInstanceListItem, event: MouseEvent) {
  event.stopPropagation()
  adminKind.value = kind
  adminRow.value = row
  reason.value = ''
  toUserId.value = null
  nodeId.value = null
  activeNodes.value = []
  showAdmin.value = true
  const instanceId = normalizeWfId(row.id)
  if (kind === 'terminate' || instanceId == null) return
  const seq = ++nodeSeq
  nodesLoading.value = true
  void wfInstanceApi.get(instanceId).then((detail) => {
    if (seq !== nodeSeq) return
    activeNodes.value = monitorNodeOptions(detail)
    nodeId.value = defaultMonitorNodeId(activeNodes.value)
  }).catch((e) => {
    if (seq === nodeSeq) message.error(translateError(e))
  }).finally(() => {
    if (seq === nodeSeq) nodesLoading.value = false
  })
}

async function submitAdmin() {
  const row = adminRow.value
  if (row?.id == null) return
  const text = reason.value.trim()
  if (!text) {
    message.warning(t('workflow.monitor.reasonRequired'))
    return
  }
  const instanceId = normalizeWfId(row.id)
  if (instanceId == null) {
    message.warning(t('workflow.monitor.unsafeId'))
    return
  }
  const userId = normalizeWfId(toUserId.value)
  if (adminKind.value === 'transfer' && userId == null) {
    message.warning(t(toUserId.value == null
      ? 'workflow.monitor.admin.issue.targetRequired'
      : 'workflow.monitor.unsafeId'))
    return
  }
  if (adminKind.value !== 'terminate' && activeNodes.value.length > 0 && !nodeId.value) {
    message.warning(t('workflow.monitor.admin.issue.ambiguousNode'))
    return
  }
  submitting.value = true
  try {
    const node = nodeId.value?.trim() || null
    if (adminKind.value === 'transfer') {
      await wfInstanceApi.forceTransfer({
        instanceId,
        toUserId: userId!,
        reason: text,
        nodeId: node,
      })
    } else if (adminKind.value === 'terminate') {
      await wfInstanceApi.terminate({ instanceId, reason: text })
    } else {
      await wfInstanceApi.skip({ instanceId, reason: text, nodeId: node })
    }
    message.success(t('common.success'))
    showAdmin.value = false
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    submitting.value = false
  }
}

function openDetail(r: WfInstanceListItem) {
  if (r.id == null) return
  const path = wfInstanceDetailPath(r.id)
  if (path == null) {
    message.warning(t('workflow.monitor.unsafeId'))
    return
  }
  void router.push(path)
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

function statusLabel(row: WfInstanceListItem | undefined): string {
  const key = row ? runtimeStatusKey(row) ?? normalizeStatus(row.status) : 'unknown'
  return t(`workflow.status.${key}`)
}

function statusType(row: WfInstanceListItem | undefined): 'default' | 'info' | 'success' | 'error' | 'warning' {
  if (row && runtimeStatusKey(row)) return 'warning'
  const key = normalizeStatus(row?.status)
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
          source: 'workflow',
          value: value as number | null,
          clearable: true,
          placeholder: t('workflow.monitor.starter'),
          'onUpdate:value': (v: number | null) => {
            setValue(v)
            search()
          },
        }),
    },
    render: (r) => r.starterName?.trim() || '—',
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
          source: 'workflow',
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
          source: 'workflow',
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
      h(NTag, { size: 'small', type: statusType(r), bordered: false }, () => statusLabel(r)),
  },
  { key: 'createTime', title: () => t('workflow.monitor.createTime'), width: 170, format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 280,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => {
        const buttons = [
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
        ]
        if (isRunning(r)) {
          const kinds: AdminKind[] = ['transfer', 'terminate', 'skip']
          for (const kind of kinds) {
            if (!authStore.hasPerm(adminPerm[kind])) continue
            buttons.push(
              h(
                NButton,
                {
                  size: 'small',
                  quaternary: true,
                  type: kind === 'terminate' ? 'error' : 'warning',
                  onClick: (e: MouseEvent) => openAdmin(kind, r, e),
                },
                () => t(`workflow.monitor.${kind === 'transfer' ? 'forceTransfer' : kind}`),
              ),
            )
          }
        }
        return buttons
      }),
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

  <n-modal
    v-model:show="showAdmin"
    preset="card"
    :title="modalTitle"
    style="width: min(480px, calc(100vw - 32px))"
  >
    <n-form>
      <n-form-item v-if="adminKind === 'transfer'" :label="t('workflow.monitor.targetUser')" required>
        <UserSelect v-model:value="toUserId" source="workflow" :placeholder="t('workflow.monitor.targetUser')" />
      </n-form-item>
      <n-form-item
        v-if="adminKind !== 'terminate'"
        :label="t('workflow.monitor.nodeId')"
        required
      >
        <n-select
          v-model:value="nodeId"
          :loading="nodesLoading"
          :options="activeNodes.map((node) => ({ label: node.name, value: node.id }))"
          :placeholder="t('workflow.monitor.nodeId')"
        />
      </n-form-item>
      <n-form-item :label="t('workflow.monitor.reason')" required>
        <n-input v-model:value="reason" type="textarea" :maxlength="512" show-count />
      </n-form-item>
    </n-form>
    <template #footer>
      <n-space justify="end">
        <n-button @click="showAdmin = false">{{ t('common.cancel') }}</n-button>
        <n-button type="primary" :loading="submitting" :disabled="nodesLoading" @click="submitAdmin">{{ t('common.confirm') }}</n-button>
      </n-space>
    </template>
  </n-modal>
</template>
