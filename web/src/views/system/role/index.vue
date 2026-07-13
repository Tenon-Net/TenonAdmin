<script setup lang="ts">
// 角色管理 = ProTable CRUD(照职位范式)+ 两个专属抽屉:授权菜单(勾选菜单树)、数据范围(选范围类型 + 自定义机构)。
// 与职位唯一不同:删角色后端会级联清关联(用户↔角色/角色↔菜单/角色数据范围),前端无需额外处理。
import { computed, h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NSwitch, NPopconfirm, NForm, NFormItem, NSelect, NTree,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { roleApi, menuApi } from '@/api'
import { translateError } from '@/utils/error'
import { DataScopeType, type RoleInput, type SysRole } from '@/types/api'
import type { MenuTreeNode } from '@/types/menu'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysRole>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: roleApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('role.deleted'),
})

const toInput = (r: SysRole): RoleInput => ({ name: r.name, code: r.code, sort: r.sort, enabled: r.enabled, remark: r.remark ?? '' })

const columns: ProTableColumn<SysRole>[] = [
  { type: 'selection' },
  { key: 'code', title: () => t('role.code') },
  { key: 'name', title: () => t('role.name'), search: true },
  { key: 'sort', title: () => t('role.sort'), width: 80 },
  {
    key: 'enabled',
    title: () => t('common.status'),
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => roleApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  { key: 'remark', title: () => t('role.remark'), ellipsis: { tooltip: true }, render: (r) => r.remark || '—' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 260,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, onClick: () => openMenus(r) }, () => t('role.grantMenus')),
        h(NButton, { size: 'small', quaternary: true, onClick: () => openScope(r) }, () => t('role.dataScope')),
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => roleApi.remove(r.id), t('role.deleted')).then((ok) => {
                if (ok) tableRef.value?.refresh()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('role.deleteConfirm', { name: r.name }),
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
  code: { required: true, whitespace: true, message: () => t('role.codeRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('role.nameRequired'), trigger: ['input', 'blur'] },
}
const blank = (): RoleInput => ({ name: '', code: '', sort: 0, enabled: true, remark: '' })
const form = reactive<RoleInput>(blank())

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
function openEdit(r: SysRole) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await roleApi.add({ ...form })
    else await roleApi.update(editingId.value, { ...form })
    message.success(t('role.saved'))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 授权菜单抽屉(勾选菜单树 → 全量替换角色授权)──
// cascade=false:每个节点独立勾选,checkedKeys 即要保存的菜单行集合,getMenus 回显 1:1 无歧义
// (目录/页面节点权限码为空,勾了也不产生权限码,后端 RbacPermissionProvider 只取 Permission!="" 的行)。
const showMenus = ref(false)
const menuTree = ref<MenuTreeNode[]>([])
const menuChecked = ref<number[]>([])
const menuRoleId = ref<number | null>(null)
async function openMenus(r: SysRole) {
  menuRoleId.value = r.id
  try {
    const [tree, granted] = await Promise.all([menuApi.tree(), roleApi.getMenus(r.id)])
    menuTree.value = tree
    menuChecked.value = granted
    showMenus.value = true
  } catch (e) {
    message.error(translateError(e))
  }
}
async function saveMenus() {
  if (menuRoleId.value === null) return
  try {
    await roleApi.setMenus(menuRoleId.value, menuChecked.value)
    message.success(t('role.grantSaved'))
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 数据范围抽屉 ──
const showScope = ref(false)
const scopeRoleId = ref<number | null>(null)
const scopeForm = reactive<{ scopeType: DataScopeType; customOrgIds: number[] }>({ scopeType: DataScopeType.Org, customOrgIds: [] })
const isCustom = computed(() => scopeForm.scopeType === DataScopeType.Custom)
const scopeOptions = computed(() => [
  { label: t('role.scope.all'), value: DataScopeType.All },
  { label: t('role.scope.org'), value: DataScopeType.Org },
  { label: t('role.scope.orgAndChildren'), value: DataScopeType.OrgAndChildren },
  { label: t('role.scope.self'), value: DataScopeType.Self },
  { label: t('role.scope.custom'), value: DataScopeType.Custom },
])
async function openScope(r: SysRole) {
  scopeRoleId.value = r.id
  try {
    const cfg = await roleApi.getDataScope(r.id)
    scopeForm.scopeType = cfg?.scopeType ?? DataScopeType.Org
    scopeForm.customOrgIds = cfg?.customOrgIds ? cfg.customOrgIds.split(',').filter(Boolean).map(Number) : []
    showScope.value = true
  } catch (e) {
    message.error(translateError(e))
  }
}
async function saveScope() {
  if (scopeRoleId.value === null) return
  try {
    await roleApi.setDataScope(scopeRoleId.value, scopeForm.scopeType, isCustom.value ? scopeForm.customOrgIds : undefined)
    message.success(t('role.scopeSaved'))
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
    :fetcher="roleApi.page"
    :labels="labels"
    storage-key="sys-role"
    :checked-row-keys="checkedKeys"
    @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/role/add'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
      <n-button
        v-auth="'POST:/api/v1/sys/role/batch-delete'"
        type="error"
        :disabled="!hasSelection"
        @click="batchDelete"
      >
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('common.batchDelete') }}
      </n-button>
    </template>
  </ProTable>

  <!-- 新增/编辑 -->
  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('role.addTitle') : t('role.editTitle')"
    :width="480"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-form-item :label="t('role.code')" path="code">
        <n-input v-model:value="form.code" :placeholder="t('role.code')" :disabled="editingId !== null" />
      </n-form-item>
      <n-form-item :label="t('role.name')" path="name">
        <n-input v-model:value="form.name" :placeholder="t('role.name')" />
      </n-form-item>
      <n-form-item :label="t('role.sort')">
        <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('role.remark')">
        <n-input v-model:value="(form.remark as string)" type="textarea" :rows="2" :placeholder="t('role.remark')" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>

  <!-- 授权菜单 -->
  <FormContainer
    v-model:show="showMenus"
    :title="t('role.grantMenus')"
    :width="420"
    :on-confirm="saveMenus"
    :confirm-text="t('common.save')"
  >
    <n-tree
      v-model:checked-keys="menuChecked"
      :data="(menuTree as any)"
      key-field="id"
      label-field="title"
      children-field="children"
      checkable
      :cascade="false"
      :selectable="false"
      block-line
      default-expand-all
    />
  </FormContainer>

  <!-- 数据范围 -->
  <FormContainer
    v-model:show="showScope"
    :title="t('role.dataScope')"
    :width="480"
    :on-confirm="saveScope"
    :confirm-text="t('common.save')"
  >
    <n-form :model="scopeForm" label-placement="left" :label-width="90">
      <n-form-item :label="t('role.scopeType')">
        <n-select v-model:value="scopeForm.scopeType" :options="scopeOptions" />
      </n-form-item>
      <n-form-item v-if="isCustom" :label="t('role.customOrgs')">
        <OrgTreeSelect v-model:value="scopeForm.customOrgIds" multiple :placeholder="t('role.customOrgsHint')" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
