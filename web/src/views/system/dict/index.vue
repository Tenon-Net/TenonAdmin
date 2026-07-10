<script setup lang="ts">
// 字典管理 = 主从:左=类型 ProTable(CRUD),点击行选中 → 右=该类型的字典项(裸 n-data-table + CRUD)。
// 关键约束:右侧走管理端 dict/item/page(含停用项、带 id);下拉用的 dict/items/{code} 只回启用项且丢 id,不能复用。
// 任何类型/项的增删改后调 useDictStore().invalidate(code) 失效下拉缓存,变更即时生效。
import { h, reactive, ref } from 'vue'
import {
  NButton, NCard, NSpace, NInput, NInputNumber, NPopconfirm, NForm, NFormItem, NDataTable, NEmpty,
  useMessage, type DataTableColumns, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { dictAdminApi } from '@/api'
import { useDictStore } from '@/stores/dict'
import { translateError } from '@/utils/error'
import type { DictItemInput, DictTypeInput, SysDictItem, SysDictType } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const dictStore = useDictStore()
const typeTableRef = ref<ProTableInst<SysDictType>>()

// ── 主从选中态 + 右侧字典项 ──
const selectedType = ref<SysDictType | null>(null)
const items = ref<SysDictItem[]>([])
const itemsLoading = ref(false)

async function loadItems() {
  const code = selectedType.value?.code
  if (!code) {
    items.value = []
    return
  }
  itemsLoading.value = true
  try {
    const list = await dictAdminApi.items(code)
    // 竞态守卫:await 期间用户可能已切换类型,过期响应不得覆盖当前选中项
    if (selectedType.value?.code === code) items.value = list
  } catch (e) {
    if (selectedType.value?.code === code) {
      message.error(translateError(e))
      items.value = []
    }
  } finally {
    if (selectedType.value?.code === code) itemsLoading.value = false
  }
}
async function selectType(r: SysDictType) {
  selectedType.value = r
  await loadItems()
}

// ── 左:字典类型 ProTable ──
const typeToInput = (r: SysDictType): DictTypeInput => ({
  code: r.code, name: r.name, sort: r.sort, enabled: r.enabled, remark: r.remark ?? '',
})
const typeColumns: ProTableColumn<SysDictType>[] = [
  { key: 'code', title: () => t('dict.code'), search: true },
  { key: 'name', title: () => t('dict.name'), search: true },
  { key: 'sort', title: () => t('dict.sort'), width: 70 },
  {
    key: 'enabled',
    title: () => t('common.status'),
    width: 90,
    // 包一层 stopPropagation:开关点击不得冒泡到行 onClick(否则误切选中类型)
    render: (r) =>
      h('div', { onClick: (e: Event) => e.stopPropagation() }, [
        h(StatusSwitch, {
          value: r.enabled,
          request: (next: boolean) => dictAdminApi.typeUpdate(r.id, { ...typeToInput(r), enabled: next }),
          'onUpdate:value': (v: boolean) => {
            r.enabled = v
            dictStore.invalidate(r.code)
          },
        }),
      ]),
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 130,
    hideInSetting: true,
    // stopPropagation:操作按钮点击不得冒泡到行 onClick(否则误切选中类型)
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false, onClick: (e: Event) => e.stopPropagation() }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openTypeEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => dictAdminApi.typeRemove(r.id), t('dict.typeDeleted')).then((ok) => {
                if (!ok) return
                dictStore.invalidate(r.code)
                if (selectedType.value?.id === r.id) {
                  selectedType.value = null
                  items.value = []
                }
                typeTableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('dict.typeDeleteConfirm', { name: r.name }),
          },
        ),
      ]),
  },
]

// ── 类型 新增/编辑弹窗 ──
const typeShow = ref(false)
const typeFormRef = ref<FormInst | null>(null)
const typeEditingId = ref<number | null>(null)
const typeRules: FormRules = {
  code: { required: true, whitespace: true, message: () => t('dict.codeRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('dict.nameRequired'), trigger: ['input', 'blur'] },
}
const blankType = (): DictTypeInput => ({ code: '', name: '', sort: 0, enabled: true, remark: '' })
const typeForm = reactive<DictTypeInput>(blankType())

function openTypeAdd() {
  typeEditingId.value = null
  Object.assign(typeForm, blankType())
  typeShow.value = true
}
function openTypeEdit(r: SysDictType) {
  typeEditingId.value = r.id
  Object.assign(typeForm, typeToInput(r))
  typeShow.value = true
}
async function saveType() {
  await typeFormRef.value?.validate()
  try {
    if (typeEditingId.value === null) await dictAdminApi.typeAdd({ ...typeForm })
    else await dictAdminApi.typeUpdate(typeEditingId.value, { ...typeForm })
    dictStore.invalidate(typeForm.code)
    message.success(t('dict.typeSaved'))
    await typeTableRef.value?.refresh()
    // 若编辑的是当前选中类型,同步其名称/编码到右栏标题与后续项提交
    if (selectedType.value?.id === typeEditingId.value) Object.assign(selectedType.value, { name: typeForm.name })
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 右:字典项 表格 ──
const itemToInput = (r: SysDictItem): DictItemInput => ({
  dictTypeCode: r.dictTypeCode, label: r.label, value: r.value, sort: r.sort, enabled: r.enabled,
})
const itemColumns: DataTableColumns<SysDictItem> = [
  { title: () => t('dict.itemLabel'), key: 'label' },
  { title: () => t('dict.itemValue'), key: 'value' },
  { title: () => t('dict.sort'), key: 'sort', width: 70 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => dictAdminApi.itemUpdate(r.id, { ...itemToInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
          dictStore.invalidate(r.dictTypeCode)
        },
      }),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 130,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openItemEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => dictAdminApi.itemRemove(r.id), t('dict.itemDeleted')).then((ok) => {
                if (!ok) return
                dictStore.invalidate(r.dictTypeCode)
                loadItems()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('dict.itemDeleteConfirm', { label: r.label }),
          },
        ),
      ]),
  },
]

// ── 字典项 新增/编辑弹窗 ──
const itemShow = ref(false)
const itemFormRef = ref<FormInst | null>(null)
const itemEditingId = ref<number | null>(null)
const itemRules: FormRules = {
  label: { required: true, whitespace: true, message: () => t('dict.itemLabelRequired'), trigger: ['input', 'blur'] },
  value: { required: true, whitespace: true, message: () => t('dict.itemValueRequired'), trigger: ['input', 'blur'] },
}
const blankItem = (): DictItemInput => ({ dictTypeCode: '', label: '', value: '', sort: 0, enabled: true })
const itemForm = reactive<DictItemInput>(blankItem())

function openItemAdd() {
  if (!selectedType.value) return
  itemEditingId.value = null
  Object.assign(itemForm, blankItem(), { dictTypeCode: selectedType.value.code })
  itemShow.value = true
}
function openItemEdit(r: SysDictItem) {
  itemEditingId.value = r.id
  Object.assign(itemForm, itemToInput(r))
  itemShow.value = true
}
async function saveItem() {
  await itemFormRef.value?.validate()
  try {
    if (itemEditingId.value === null) await dictAdminApi.itemAdd({ ...itemForm })
    else await dictAdminApi.itemUpdate(itemEditingId.value, { ...itemForm })
    dictStore.invalidate(itemForm.dictTypeCode)
    message.success(t('dict.itemSaved'))
    await loadItems()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <div class="dict-layout">
    <div class="dict-pane">
      <ProTable
        ref="typeTableRef"
        :columns="typeColumns"
        :fetcher="dictAdminApi.typePage"
        :title="t('dict.title')"
        :labels="labels"
        storage-key="sys-dict-type"
        :row-props="(row: SysDictType) => ({
          style: 'cursor: pointer',
          class: row.id === selectedType?.id ? 'dict-row--active' : '',
          onClick: () => selectType(row),
        })"
        @error="(e) => message.error(translateError(e))"
      >
        <template #toolbar>
          <n-button v-auth="'POST:/api/v1/sys/dict/type'" type="primary" @click="openTypeAdd">
            <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
          </n-button>
        </template>
      </ProTable>
    </div>

    <div class="dict-pane">
      <n-card v-if="selectedType" :bordered="true" :title="t('dict.itemsOf', { name: selectedType.name })">
        <template #header-extra>
          <n-button v-auth="'POST:/api/v1/sys/dict/item'" size="small" type="primary" @click="openItemAdd">
            <template #icon><AppIcon icon="ph:plus" :size="15" /></template>{{ t('dict.addItem') }}
          </n-button>
        </template>
        <n-data-table
          :columns="itemColumns"
          :data="items"
          :loading="itemsLoading"
          :row-key="(r: SysDictItem) => r.id"
          size="small"
        />
      </n-card>
      <n-card v-else :bordered="true">
        <n-empty :description="t('dict.selectTypeHint')" style="padding: 48px 0" />
      </n-card>
    </div>
  </div>

  <FormContainer
    v-model:show="typeShow"
    :title="typeEditingId === null ? t('dict.addTypeTitle') : t('dict.editTypeTitle')"
    :width="480"
    :on-confirm="saveType"
    :confirm-text="t('common.save')"
  >
    <n-form ref="typeFormRef" :model="typeForm" :rules="typeRules" label-placement="left" :label-width="80">
      <n-form-item :label="t('dict.code')" path="code">
        <n-input v-model:value="typeForm.code" :placeholder="t('dict.code')" :disabled="typeEditingId !== null" />
      </n-form-item>
      <n-form-item :label="t('dict.name')" path="name">
        <n-input v-model:value="typeForm.name" :placeholder="t('dict.name')" />
      </n-form-item>
      <n-form-item :label="t('dict.sort')">
        <n-input-number v-model:value="typeForm.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('dict.remark')">
        <n-input v-model:value="(typeForm.remark as string)" type="textarea" :autosize="{ minRows: 2 }" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="typeForm.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>

  <FormContainer
    v-model:show="itemShow"
    :title="itemEditingId === null ? t('dict.addItemTitle') : t('dict.editItemTitle')"
    :width="480"
    :on-confirm="saveItem"
    :confirm-text="t('common.save')"
  >
    <n-form ref="itemFormRef" :model="itemForm" :rules="itemRules" label-placement="left" :label-width="80">
      <n-form-item :label="t('dict.itemLabel')" path="label">
        <n-input v-model:value="itemForm.label" :placeholder="t('dict.itemLabel')" />
      </n-form-item>
      <n-form-item :label="t('dict.itemValue')" path="value">
        <n-input v-model:value="itemForm.value" :placeholder="t('dict.itemValue')" />
      </n-form-item>
      <n-form-item :label="t('dict.sort')">
        <n-input-number v-model:value="itemForm.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="itemForm.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>

<style scoped>
.dict-layout {
  display: flex;
  flex-wrap: wrap;
  gap: var(--gap-card);
  align-items: flex-start;
}
.dict-pane {
  flex: 1 1 380px;
  min-width: 0;
}
/* 选中行高亮:主色淡背景,配合 row-props 的指针光标 */
.dict-pane :deep(.dict-row--active > td) {
  background-color: var(--n-merged-td-color-hover, rgba(99, 102, 241, 0.08));
}
</style>
