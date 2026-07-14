<script setup lang="ts">
// 岗位管理 = 普通 ProTable CRUD + 行内启停。岗位与机构无关联(PositionInput 无 orgId)。
// 后端无独立启停端点 → StatusSwitch 走全量 update(同 module 页);code 编辑禁用。
import { h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NPopconfirm, NForm, NFormItem, NSwitch,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { positionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { PositionInput, SysPosition } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const tableRef = ref<ProTableInst<SysPosition>>()

// 行数据 → 完整入参:openEdit 回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。
const toInput = (r: SysPosition): PositionInput => ({ name: r.name, code: r.code, sort: r.sort, enabled: r.enabled })

const columns: ProTableColumn<SysPosition>[] = [
  { key: 'name', title: () => t('position.name'), search: true },
  { key: 'code', title: () => t('position.code') },
  { key: 'sort', title: () => t('position.sort'), width: 80 },
  {
    key: 'enabled',
    title: () => t('common.status'),
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => positionApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => positionApi.remove(r.id), t('position.deleted')).then((ok) => {
                if (ok) tableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('position.deleteConfirm', { name: r.name }),
          },
        ),
      ]),
  },
]

// ── 新增/编辑弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  name: { required: true, whitespace: true, message: () => t('position.nameRequired'), trigger: ['input', 'blur'] },
}
const blank = (): PositionInput => ({ name: '', code: '', sort: 0, enabled: true })
const form = reactive<PositionInput>(blank())

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
function openEdit(r: SysPosition) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await positionApi.add({ ...form })
    else await positionApi.update(editingId.value, { ...form })
    message.success(t('position.saved'))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="positionApi.page"
    storage-key="sys-position"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/position/add'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
    </template>
  </ProTable>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('position.addTitle') : t('position.editTitle')"
    :width="480"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-form-item :label="t('position.name')" path="name">
        <n-input v-model:value="form.name" :placeholder="t('position.name')" />
      </n-form-item>
      <n-form-item :label="t('position.code')" path="code">
        <n-input v-model:value="form.code" :placeholder="t('position.codePlaceholder')" :disabled="editingId !== null" />
      </n-form-item>
      <n-form-item :label="t('position.sort')">
        <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
