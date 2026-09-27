<script setup lang="ts">
import { h, reactive, ref } from 'vue'
import { NButton, NDatePicker, NEmpty, NForm, NFormItem, NModal, NSpace, NSwitch, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import UserSelect from '@/components/UserSelect/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { wfDelegationApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import type { WfDelegationRule, WfDelegationRuleInput } from '@/types/workflow'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<WfDelegationRule>>()
const modalOpen = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)
const requestKey = useRequestKey()
const deleteRequestKeys = new Map<number, string>()
const form = reactive({
  originalUserId: null as number | null,
  delegateUserId: null as number | null,
  enabled: true,
  startsAt: Date.now(),
  endsAt: Date.now() + 30 * 24 * 60 * 60 * 1000,
})

function resetForm(row?: WfDelegationRule) {
  editingId.value = row?.id == null ? null : Number(row.id)
  form.originalUserId = row?.originalUserId == null ? null : Number(row.originalUserId)
  form.delegateUserId = row?.delegateUserId == null ? null : Number(row.delegateUserId)
  form.enabled = row?.enabled ?? true
  form.startsAt = row?.startsAt ? new Date(row.startsAt).getTime() : Date.now()
  form.endsAt = row?.endsAt ? new Date(row.endsAt).getTime() : Date.now() + 30 * 24 * 60 * 60 * 1000
  requestKey.reset()
  modalOpen.value = true
}

function toInput(): WfDelegationRuleInput | null {
  if (!form.originalUserId || !form.delegateUserId || !form.startsAt || !form.endsAt || form.startsAt >= form.endsAt) {
    message.warning(t('workflow.delegation.required'))
    return null
  }
  return {
    originalUserId: form.originalUserId,
    delegateUserId: form.delegateUserId,
    enabled: form.enabled,
    startsAt: new Date(form.startsAt).toISOString(),
    endsAt: new Date(form.endsAt).toISOString(),
    requestId: requestKey.value(),
  } as WfDelegationRuleInput
}

async function save() {
  const input = toInput()
  if (!input) return
  submitting.value = true
  try {
    if (editingId.value == null) await wfDelegationApi.add(input)
    else await wfDelegationApi.update(editingId.value, input)
    requestKey.settle('success')
    modalOpen.value = false
    await tableRef.value?.refresh()
  } catch (e) {
    requestKey.settle(classifyOutcome(e))
    message.error(translateError(e))
  } finally {
    submitting.value = false
  }
}

async function remove(row: WfDelegationRule) {
  const id = Number(row.id)
  const requestId = deleteRequestKeys.get(id) ?? `delete-${id}-${Date.now()}`
  deleteRequestKeys.set(id, requestId)
  const ok = await confirm({
    content: t('workflow.delegation.deleteConfirm'),
    type: 'warning',
    action: () => wfDelegationApi.remove(id, requestId),
    successMsg: t('workflow.delegation.deleted'),
  })
  if (ok) {
    deleteRequestKeys.delete(id)
    await tableRef.value?.refresh()
  }
}

const columns: ProTableColumn<WfDelegationRule>[] = [
  {
    key: 'originalUserId',
    title: () => t('workflow.delegation.originalUser'),
    minWidth: 170,
    render: (row) => row.originalUserName || `#${row.originalUserId}`,
  },
  {
    key: 'delegateUserId',
    title: () => t('workflow.delegation.delegateUser'),
    minWidth: 170,
    render: (row) => row.delegateUserName || `#${row.delegateUserId}`,
  },
  { key: 'startsAt', title: () => t('workflow.delegation.startsAt'), width: 190, format: 'datetime' },
  { key: 'endsAt', title: () => t('workflow.delegation.endsAt'), width: 190, format: 'datetime' },
  {
    key: 'enabled',
    title: () => t('workflow.delegation.enabled'),
    width: 110,
    render: (row) => h(NTag, { size: 'small', type: row.enabled ? 'success' : 'default', bordered: false }, () =>
      row.enabled ? t('workflow.delegation.active') : t('workflow.delegation.disabled')),
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 150,
    fixed: 'right',
    hideInSetting: true,
    render: (row) => h(NSpace, { size: 2, wrapItem: false }, () => [
      authStore.hasPerm('PUT:/api/v1/workflow/delegation/{id}')
        ? h(NButton, { size: 'small', quaternary: true, onClick: () => resetForm(row) }, () => t('common.edit'))
        : null,
      authStore.hasPerm('DELETE:/api/v1/workflow/delegation/{id}')
        ? h(NButton, { size: 'small', quaternary: true, type: 'error', onClick: () => void remove(row) }, () => t('common.delete'))
        : null,
    ]),
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    storage-key="workflow-delegation"
    row-key="id"
    :columns="columns"
    :fetcher="wfDelegationApi.page"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/workflow/delegation/add'" type="primary" @click="resetForm()">
        {{ t('workflow.delegation.add') }}
      </n-button>
    </template>
    <template #empty>
      <n-empty :description="t('workflow.delegation.empty')" size="small" />
    </template>
  </ProTable>

  <n-modal v-model:show="modalOpen" preset="card" style="width: 520px" :title="editingId == null ? t('workflow.delegation.add') : t('workflow.delegation.edit')">
    <n-form label-placement="left" label-width="110">
      <n-form-item :label="t('workflow.delegation.originalUser')">
        <UserSelect v-model:value="form.originalUserId" :disabled="editingId != null" />
      </n-form-item>
      <n-form-item :label="t('workflow.delegation.delegateUser')">
        <UserSelect v-model:value="form.delegateUserId" />
      </n-form-item>
      <n-form-item :label="t('workflow.delegation.startsAt')">
        <n-date-picker v-model:value="form.startsAt" type="datetime" clearable />
      </n-form-item>
      <n-form-item :label="t('workflow.delegation.endsAt')">
        <n-date-picker v-model:value="form.endsAt" type="datetime" clearable />
      </n-form-item>
      <n-form-item :label="t('workflow.delegation.enabled')">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
    <template #footer>
      <n-space justify="end">
        <n-button @click="modalOpen = false">{{ t('common.cancel') }}</n-button>
        <n-button type="primary" :loading="submitting" @click="void save()">{{ t('workflow.delegation.save') }}</n-button>
      </n-space>
    </template>
  </n-modal>
</template>
