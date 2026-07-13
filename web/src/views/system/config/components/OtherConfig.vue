<script setup lang="ts">
// 其他配置 = 分类配置中心的通用兜底:任意/未预置 key 的扁平 key-value 全量 CRUD(从原 config/index.vue 平移)。
// 列驱动搜索/分页/竞态交 ProTable,新增/编辑弹窗状态手写(与 module 页一致,好评审)。
import { h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NPopconfirm, NForm, NFormItem,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ConfigInput, SysConfig } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysConfig>>()

const columns: ProTableColumn<SysConfig>[] = [
  { key: 'configKey', title: () => t('config.key'), search: true },
  { key: 'name', title: () => t('config.name'), search: true },
  { key: 'configValue', title: () => t('config.value'), ellipsis: { tooltip: true }, render: (r) => r.configValue || '—' },
  { key: 'groupCode', title: () => t('config.group'), search: true, render: (r) => r.groupCode || '—' },
  { key: 'sort', title: () => t('config.sort'), width: 80 },
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
            // popconfirm 当触发器,「执行→toast」交给 useConfirm().run(与 module 页一致)。
            onPositiveClick: () =>
              run(() => configApi.remove(r.id), t('config.deleted')).then((ok) => {
                if (ok) tableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('config.deleteConfirm', { name: r.name }),
          },
        ),
      ]),
  },
]

// ── 新增/编辑弹窗(FormContainer:loading/关闭时机按 onConfirm 协议接管)──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  configKey: { required: true, whitespace: true, message: () => t('config.keyRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('config.nameRequired'), trigger: ['input', 'blur'] },
}
const blank = (): ConfigInput => ({ configKey: '', configValue: '', name: '', groupCode: '', sort: 0, remark: '' })
const form = reactive<ConfigInput>(blank())

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
function openEdit(r: SysConfig) {
  editingId.value = r.id
  Object.assign(form, {
    configKey: r.configKey, configValue: r.configValue ?? '', name: r.name,
    groupCode: r.groupCode ?? '', sort: r.sort, remark: r.remark ?? '',
  })
  show.value = true
}

/** FormContainer onConfirm:校验失败 reject / API 失败 return false → 弹层不关;成功刷当前页(不回第 1 页)。 */
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await configApi.add({ ...form })
    else await configApi.update(editingId.value, { ...form })
    message.success(t('config.saved'))
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
    :fetcher="configApi.page"
    :labels="labels"
    storage-key="sys-config"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/config'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
    </template>
  </ProTable>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('config.addTitle') : t('config.editTitle')"
    :width="520"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="90">
      <n-form-item :label="t('config.key')" path="configKey">
        <n-input v-model:value="form.configKey" :placeholder="t('config.key')" :disabled="editingId !== null" />
      </n-form-item>
      <n-form-item :label="t('config.name')" path="name">
        <n-input v-model:value="form.name" :placeholder="t('config.name')" />
      </n-form-item>
      <n-form-item :label="t('config.value')">
        <n-input v-model:value="(form.configValue as string)" type="textarea" :autosize="{ minRows: 2 }" />
      </n-form-item>
      <n-form-item :label="t('config.group')">
        <n-input v-model:value="(form.groupCode as string)" :placeholder="t('config.group')" />
      </n-form-item>
      <n-form-item :label="t('config.sort')">
        <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('config.remark')">
        <n-input v-model:value="(form.remark as string)" type="textarea" :autosize="{ minRows: 2 }" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
