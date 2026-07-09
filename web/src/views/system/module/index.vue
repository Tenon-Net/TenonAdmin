<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue'
import {
  NCard, NButton, NSpace, NDataTable, NTag, NModal, NForm, NFormItem, NInput, NInputNumber, NSwitch,
  NPopconfirm, useMessage, type DataTableColumns,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import IconPicker from '@/components/IconPicker/index.vue'
import { moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ModuleInput, ModuleRow } from '@/types/api'

const { t } = useI18n()
const message = useMessage()

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

// ── 新增/编辑弹窗 ────────────────────────────────────────────────
const showModal = ref(false)
const saving = ref(false)
const editingId = ref<number | null>(null)
const form = reactive<ModuleInput>({ code: '', title: '', icon: '', defaultRoute: '', sort: 0, enabled: true, remark: '' })

function openAdd() {
  editingId.value = null
  Object.assign(form, { code: '', title: '', icon: '', defaultRoute: '', sort: 0, enabled: true, remark: '' })
  showModal.value = true
}
function openEdit(r: ModuleRow) {
  editingId.value = r.id
  Object.assign(form, {
    code: r.code, title: r.title, icon: r.icon ?? '', defaultRoute: r.defaultRoute ?? '',
    sort: r.sort, enabled: r.enabled, remark: r.remark ?? '',
  })
  showModal.value = true
}

async function submit() {
  if (!form.code.trim() || !form.title.trim()) return
  saving.value = true
  try {
    if (editingId.value === null) await moduleApi.add({ ...form })
    else await moduleApi.update(editingId.value, { ...form })
    message.success(t('module.saved'))
    showModal.value = false
    await load()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}

async function remove(r: ModuleRow) {
  try {
    await moduleApi.remove(r.id)
    message.success(t('module.deleted'))
    await load()
  } catch (e) {
    message.error(translateError(e))
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
  { title: () => t('module.sort'), key: 'sort', width: 80 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 90,
    render: (r) =>
      h(NTag, { type: r.enabled ? 'success' : 'default', size: 'small', bordered: false }, () =>
        r.enabled ? t('common.enabled') : t('common.disabled'),
      ),
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
              { onPositiveClick: () => remove(r) },
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
  <div class="page">
    <n-card :bordered="true">
      <div class="bar">
        <h3>{{ t('module.manage') }}</h3>
        <n-button type="primary" @click="openAdd">
          <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
        </n-button>
      </div>
      <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="(r: ModuleRow) => r.id" />
    </n-card>

    <n-modal
      v-model:show="showModal"
      preset="card"
      :title="editingId === null ? t('module.addTitle') : t('module.editTitle')"
      style="width: 520px"
    >
      <n-form :model="form" label-placement="left" :label-width="90">
        <n-form-item :label="t('module.code')">
          <n-input v-model:value="form.code" :placeholder="t('module.code')" />
        </n-form-item>
        <n-form-item :label="t('module.name')">
          <n-input v-model:value="form.title" :placeholder="t('module.name')" />
        </n-form-item>
        <n-form-item :label="t('module.icon')">
          <IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />
        </n-form-item>
        <n-form-item :label="t('module.defaultRoute')">
          <n-input v-model:value="(form.defaultRoute as string)" placeholder="/system/user" />
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
      <template #footer>
        <n-space justify="end">
          <n-button @click="showModal = false">{{ t('common.cancel') }}</n-button>
          <n-button type="primary" :loading="saving" @click="submit">{{ t('common.save') }}</n-button>
        </n-space>
      </template>
    </n-modal>
  </div>
</template>

<style scoped>
.page {
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
</style>
