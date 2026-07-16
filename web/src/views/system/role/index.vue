<script setup lang="ts">
// 角色管理 = ProTable CRUD(照职位范式)+ 两个专属抽屉:授权菜单(勾选菜单树)、数据范围(选范围类型 + 自定义机构)。
// 删角色会**物理**删掉用户↔角色关联(不可恢复)——所以删前先查一次持有人数当面告知,单删与批量删都盖到,
// 只给行内按钮加警告的话,勾选 + 批量删就绕过去了。
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NSwitch, NForm, NFormItem, NSelect, NDropdown,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import UserPicker from '@/components/UserPicker/index.vue'
import GrantMenuTable from './components/GrantMenuTable.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { roleApi, menuApi, moduleApi, userApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { DataScopeType, type ModuleRow, type RoleInput, type SysRole } from '@/types/api'
import type { MenuTreeNode } from '@/types/menu'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const auth = useAuthStore()
const tableRef = ref<ProTableInst<SysRole>>()

/** 「未分配」哨兵:雪花 id 无 0,用它表示 moduleId==null 的顶级目录分组。 */
const UNASSIGNED = 0
/** 授权抽屉的「所属应用」下拉数据源。 */
const modules = ref<ModuleRow[]>([])
onMounted(async () => {
  try {
    modules.value = await moduleApi.list()
  } catch {
    // 模块列表仅供授权抽屉的「所属应用」下拉;拉取失败不阻塞角色管理主流程。
  }
})

/**
 * 持有该角色的用户数。复用用户分页端点(Size=1 只要 total),不新开接口。
 * 查不到(如只有角色 CRUD、没有用户查询权限的管理员会 403)返回 null → 退回通用文案,不阻断删除:
 * 今天能删的人,改完照样能删,本次改动只加知情,不改变谁能删什么。
 */
async function countUsers(roleId: number): Promise<number | null> {
  try {
    const { total } = await userApi.page({ page: 1, pageSize: 1, roleId })
    return total
  } catch {
    return null
  }
}

const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: roleApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('role.deleted'),
  content: async (ids) => {
    const counts = await Promise.all(ids.map(countUsers))
    if (counts.some((n) => n === null)) return t('common.batchDeleteConfirm', { count: ids.length })
    const users = counts.reduce<number>((sum, n) => sum + n!, 0)
    return users === 0
      ? t('common.batchDeleteConfirm', { count: ids.length })
      : t('role.batchDeleteWithUsers', { count: ids.length, users })
  },
})

async function askDelete(r: SysRole) {
  const n = await countUsers(r.id)
  const content =
    n === null
      ? t('role.deleteConfirm', { name: r.name })
      : n === 0
        ? t('role.deleteConfirmNoUsers', { name: r.name })
        : t('role.deleteConfirmWithUsers', { name: r.name, count: n })
  const ok = await confirm({ content, type: 'error', action: () => roleApi.remove(r.id), successMsg: t('role.deleted') })
  if (ok) tableRef.value?.refresh()
}

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
        disabled: !auth.hasPerm('PUT:/api/v1/sys/role/{id}'),
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
    width: 240,
    hideInSetting: true,
    render: (r) => {
      // 更多▾:授权菜单/数据范围/授权用户各按自己的权限码显隐。
      const dropdownOptions = [
        auth.hasPerm('PUT:/api/v1/sys/role/menu') ? { label: t('role.grantMenus'), key: 'menus' } : null,
        auth.hasPerm('PUT:/api/v1/sys/role/datascope') ? { label: t('role.dataScope'), key: 'scope' } : null,
        auth.hasPerm('PUT:/api/v1/sys/role/users') ? { label: t('role.grantUsers'), key: 'users' } : null,
      ].filter((o): o is { label: string; key: string } => o !== null)
      return h(NSpace, { size: 4, wrapItem: false }, () => [
        auth.hasPerm('PUT:/api/v1/sys/role/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        auth.hasPerm('DELETE:/api/v1/sys/role/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'error', onClick: () => askDelete(r) }, () => t('common.delete'))
          : null,
        dropdownOptions.length
          ? h(NDropdown, {
              options: dropdownOptions,
              onSelect: (key: string) => {
                if (key === 'menus') openMenus(r)
                else if (key === 'scope') openScope(r)
                else openUsers(r)
              },
            }, () => h(NButton, { size: 'small', quaternary: true }, () => t('common.more')))
          : null,
      ])
    },
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

// ── 授权菜单抽屉(分组表格 → 全量替换角色授权)──
const showMenus = ref(false)
const menuTree = ref<MenuTreeNode[]>([])
const menuGranted = ref<number[]>([])
const menuRoleId = ref<number | null>(null)
const defaultModuleId = ref(UNASSIGNED)

async function openMenus(r: SysRole) {
  menuRoleId.value = r.id
  try {
    const [tree, granted] = await Promise.all([menuApi.tree(), roleApi.getMenus(r.id)])
    menuTree.value = tree
    menuGranted.value = granted
    defaultModuleId.value = auth.currentModuleId ?? modules.value[0]?.id ?? UNASSIGNED
    showMenus.value = true
  } catch (e) {
    message.error(translateError(e))
  }
}
function onMenuCheckedUpdate(ids: number[]) {
  menuGranted.value = ids
}
async function saveMenus() {
  if (menuRoleId.value === null) return
  try {
    await roleApi.setMenus(menuRoleId.value, menuGranted.value)
    message.success(t('role.grantSaved'))
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 授权用户(UserPicker) ──
const userPickerRef = ref<InstanceType<typeof UserPicker> | null>(null)
const userRoleId = ref<number | null>(null)

async function openUsers(r: SysRole) {
  userRoleId.value = r.id
  try {
    const ids = await roleApi.getUsers(r.id)
    userPickerRef.value?.open(ids)
  } catch (e) {
    message.error(translateError(e))
  }
}
async function onUserConfirm(ids: number[]) {
  if (userRoleId.value === null) return
  try {
    await roleApi.setUsers(userRoleId.value, ids)
    message.success(t('role.grantUsersSaved'))
  } catch (e) {
    message.error(translateError(e))
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
    :width="820"
    :on-confirm="saveMenus"
    :confirm-text="t('common.save')"
  >
    <GrantMenuTable
      :tree="menuTree"
      :granted="menuGranted"
      :modules="modules"
      :default-module-id="defaultModuleId"
      @update:checked="onMenuCheckedUpdate"
    />
  </FormContainer>

  <!-- 授权用户 -->
  <UserPicker ref="userPickerRef" @confirm="onUserConfirm" />

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
