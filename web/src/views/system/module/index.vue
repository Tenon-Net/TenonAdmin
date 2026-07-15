<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue'
import {
  NCard, NButton, NSpace, NDataTable, NTag, NForm, NFormItem, NInput, NInputNumber, NSwitch,
  NPopconfirm, useMessage, type DataTableColumns, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import IconPicker from '@/components/IconPicker/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ModuleInput, ModuleRow } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()

const loading = ref(false)
const rows = ref<ModuleRow[]>([])

/** 内置 system 模块受保护(后端 42013),前端据 code 禁删,避免明知不可为的请求。 */
const isBuiltin = (r: ModuleRow) => r.code === 'system'

async function load() {
  loading.value = true
  try {
    rows.value = await moduleApi.list()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

// ── 新增/编辑弹窗(FormContainer:loading/底栏/关闭时机由容器按 onConfirm 协议接管)──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const rules: FormRules = {
  // whitespace: 保留原「.trim() 禁用保存」的语义,纯空白不算填写
  code: { required: true, whitespace: true, message: () => t('module.codeRequired'), trigger: ['input', 'blur'] },
  title: { required: true, whitespace: true, message: () => t('module.nameRequired'), trigger: ['input', 'blur'] },
}
const editingId = ref<number | null>(null)
const blank = (): ModuleInput => ({ code: '', title: '', icon: '', defaultRoute: '', apiPrefix: '', sort: 0, enabled: true, remark: '' })
const form = reactive<ModuleInput>(blank())

/** 行数据 → 完整入参:openEdit 回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。 */
const toInput = (r: ModuleRow): ModuleInput => ({
  code: r.code, title: r.title, icon: r.icon ?? '', defaultRoute: r.defaultRoute ?? '',
  apiPrefix: r.apiPrefix ?? '', sort: r.sort, enabled: r.enabled, remark: r.remark ?? '',
})

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
function openEdit(r: ModuleRow) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}

/** FormContainer onConfirm:校验失败 reject / API 失败 return false → 弹层不关;成功正常返回自动关。 */
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await moduleApi.add({ ...form })
    else await moduleApi.update(editingId.value, { ...form })
    message.success(t('module.saved'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

const columns: DataTableColumns<ModuleRow> = [
  { title: () => t('module.code'), key: 'code' },
  {
    title: () => t('module.name'),
    key: 'title',
    render: (r) =>
      h(NSpace, { align: 'center', size: 6, wrapItem: false }, () => [
        r.icon ? h(AppIcon, { icon: r.icon, size: 18 }) : null,
        r.title,
        isBuiltin(r) ? h(NTag, { size: 'small', type: 'info', bordered: false }, () => t('module.builtin')) : null,
      ]),
  },
  { title: () => t('module.defaultRoute'), key: 'defaultRoute', render: (r) => r.defaultRoute || '—' },
  { title: () => t('module.apiPrefix'), key: 'apiPrefix', width: 120, render: (r) => r.apiPrefix || '—' },
  { title: () => t('module.sort'), key: 'sort', width: 80 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        // 内置 system 应用停用会让门户失联(模块/菜单管理页都在它下面,停了就没法从 UI 恢复)——
        // 与「禁删」同级的前端保护;后端 UpdateAsync 暂未拦内置模块,只护删除(42013)。
        disabled: isBuiltin(r),
        // 停用会把应用从门户隐藏,先确认;启用无副作用,跳过确认(返回 null)。
        confirm: (next: boolean) => (next ? null : t('module.disableConfirm', { title: r.title })),
        request: (next: boolean) => moduleApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 140,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        isBuiltin(r)
          ? null
          : h(
              NPopconfirm,
              {
                // popconfirm 留在模板层当触发器,「执行→toast」后半段交给 useConfirm().run。
                onPositiveClick: () =>
                  run(() => moduleApi.remove(r.id), t('module.deleted')).then((ok) => {
                    if (ok) load()
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('module.deleteConfirm', { title: r.title }),
              },
            ),
      ]),
  },
]
</script>

<template>
  <div class="view">
    <n-card :bordered="true">
      <div class="bar">
        <n-button type="primary" @click="openAdd">
          <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
        </n-button>
      </div>
      <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="(r: ModuleRow) => r.id" />
    </n-card>

    <FormContainer
      v-model:show="show"
      :title="editingId === null ? t('module.addTitle') : t('module.editTitle')"
      :width="520"
      :on-confirm="save"
      :confirm-text="t('common.save')"
    >
      <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="90">
        <n-form-item :label="t('module.code')" path="code">
          <n-input v-model:value="form.code" :placeholder="t('module.code')" :disabled="editingId !== null" />
        </n-form-item>
        <n-form-item :label="t('module.name')" path="title">
          <n-input v-model:value="form.title" :placeholder="t('module.name')" />
        </n-form-item>
        <n-form-item :label="t('module.icon')">
          <IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />
        </n-form-item>
        <n-form-item :label="t('module.defaultRoute')">
          <n-input v-model:value="(form.defaultRoute as string)" placeholder="/system/user" />
        </n-form-item>
        <n-form-item :label="t('module.apiPrefix')">
          <n-space vertical :size="2" style="width: 100%">
            <n-input v-model:value="(form.apiPrefix as string)" placeholder="sys" />
            <span class="hint">{{ t('module.apiPrefixHint') }}</span>
          </n-space>
        </n-form-item>
        <n-form-item :label="t('module.sort')">
          <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
        </n-form-item>
        <n-form-item :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item>
        <n-form-item :label="t('module.remark')">
          <n-input v-model:value="(form.remark as string)" type="textarea" :autosize="{ minRows: 2 }" />
        </n-form-item>
      </n-form>
    </FormContainer>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.bar h3 {
  font-size: var(--font-size-md);
  font-weight: 600;
  color: var(--color-text-primary);
}
.hint {
  font-size: 12px;
  color: var(--color-text-secondary, #999);
}
</style>
