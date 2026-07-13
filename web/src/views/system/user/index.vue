<script setup lang="ts">
// 用户管理(写侧)= ProTable(列表/搜索/分页)+ 手写弹窗 CRUD + 重置密码 + 专用启停端点。
// 角色多选:新增/编辑均可分配角色(选项来自 roleApi 拉一大页);编辑回显走 userApi.detail 的 roleIds。
// 超管行(isSuperAdmin)删除/停用置灰防自锁;启停走专用 setEnabled(非全量 update)。
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import {
  NButton, NCard, NTree, NSpace, NTag, NInput, NForm, NFormItem, NSelect, NSwitch, NPopconfirm,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import PasswordStrength from '@/components/PasswordStrength/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { userApi, positionApi, roleApi, orgApi } from '@/api'
import { translateError } from '@/utils/error'
import { buildTree, type Tree } from '@/utils/tree'
import type { AddUserInput, SysOrg, UpdateUserInput, UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const tableRef = ref<ProTableInst<UserItem>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: userApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('user.deleted'),
})

// 职位/角色下拉:各拉一页足量。ponytail: 200 覆盖绝大多数系统;真超再上分页搜索。
const positionOptions = ref<{ label: string; value: number }[]>([])
const roleOptions = ref<{ label: string; value: number }[]>([])
// 左侧机构树筛选:选中节点即按其机构过滤用户;params 深监听联动 ProTable(回第 1 页重查)。
const orgTree = ref<Tree<SysOrg>[]>([])
const selectedOrgId = ref<number | null>(null)
const tableParams = computed(() => (selectedOrgId.value == null ? {} : { orgId: selectedOrgId.value }))
function onOrgSelect(keys: (string | number)[]) {
  // 再点选中项 → 取消选中 → 恢复全部
  selectedOrgId.value = keys.length ? Number(keys[0]) : null
}
onMounted(async () => {
  try {
    const { items } = await positionApi.page({ page: 1, pageSize: 200 })
    positionOptions.value = items.map((p) => ({ label: p.name, value: p.id }))
  } catch {
    // 静默:职位是配角,不打断列表
  }
  try {
    const { items } = await roleApi.page({ page: 1, pageSize: 200 })
    roleOptions.value = items.map((r) => ({ label: r.name, value: r.id }))
  } catch {
    // 静默:角色下拉拉取失败不打断列表
  }
  try {
    orgTree.value = buildTree(await orgApi.list())
  } catch {
    // 静默:机构树是筛选辅助,拉取失败不打断列表
  }
})

// 角色反查:角色页「查看用户」跳这里并带 ?roleId=。
// 必须 watch 而非 onMounted:标签页按 path 做主键且页面被 keep-alive,本页标签已开着时再跳一次只换 query,
// 组件实例被复用、onMounted 不会再触发。
// 同时把 tableRef 一起纳入监听源:immediate 在 setup 期求值,那时表格实例还没挂上(ref 为空),
// 只听 query 的话冷启动这一路会静默失效——实例就绪时这条 watch 会再触发一次,把筛选补上。
const route = useRoute()
watch(
  [tableRef, () => route.query.roleId],
  ([inst, roleId]) => {
    if (!inst) return
    const next = roleId == null ? undefined : Number(roleId)
    if (inst.params.roleId === next) return // 防抖:实例就绪与 query 变化可能各触发一次
    inst.params.roleId = next
    inst.search() // 回第 1 页重查
  },
  { immediate: true },
)

const columns: ProTableColumn<UserItem>[] = [
  // 超管行禁勾:批量删除同样不可含超管(后端也会整体拒绝)
  { type: 'selection', disabled: (r: UserItem) => r.isSuperAdmin },
  { key: 'account', title: () => t('user.account'), search: true, sorter: true },
  { key: 'name', title: () => t('user.name'), search: true },
  // 只作搜索项,不进表格也不进列设置。options 直接吃编辑表单已拉好的 roleOptions(ProTable 支持 Ref 选项源),不额外发请求;
  // 列上有 options,搜索控件自动是 select。
  {
    key: 'roleId',
    title: () => t('user.roles'),
    hideInTable: true,
    options: roleOptions,
    search: { props: { clearable: true } },
  },
  { key: 'orgName', title: () => t('user.org'), render: (r) => r.orgName || '—' },
  { key: 'positionName', title: () => t('user.position'), render: (r) => r.positionName || '—' },
  {
    key: 'enabled',
    title: () => t('user.status'),
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        // 超管不可停用(防自锁 —— 停了就没法从 UI 恢复;后端也保护)。
        disabled: r.isSuperAdmin,
        confirm: (next: boolean) => (next ? null : t('user.disableConfirm', { name: r.name })),
        request: (next: boolean) => userApi.setEnabled(r.id, next),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  {
    key: 'isSuperAdmin',
    title: () => t('user.superAdmin'),
    width: 90,
    render: (r) =>
      r.isSuperAdmin ? h(NTag, { type: 'warning', size: 'small', bordered: false }, () => t('user.superAdmin')) : '—',
  },
  { key: 'createTime', title: () => t('user.createTime'), format: 'datetime', sorter: true },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 210,
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(NButton, { size: 'small', quaternary: true, onClick: () => openReset(r) }, () => t('user.resetPassword')),
        r.isSuperAdmin
          ? null
          : h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => userApi.remove(r.id), t('user.deleted')).then((ok) => {
                    if (ok) tableRef.value?.refresh()
                  }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('user.deleteConfirm', { name: r.name }),
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
  account: { required: true, whitespace: true, message: () => t('user.accountRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('user.nameRequired'), trigger: ['input', 'blur'] },
}
interface UserForm {
  account: string
  password: string
  name: string
  orgId: number | null
  positionId: number | null
  enabled: boolean
  roleIds: number[]
}
const blank = (): UserForm => ({ account: '', password: '', name: '', orgId: null, positionId: null, enabled: true, roleIds: [] })
const form = reactive<UserForm>(blank())

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
/** 编辑:先取 detail(拿 roleIds + 回显),成功再开弹层;失败弹码不开。 */
async function openEdit(r: UserItem) {
  try {
    const d = await userApi.detail(r.id)
    editingId.value = r.id
    Object.assign(form, {
      account: d.account, password: '', name: d.name,
      orgId: d.orgId ?? null, positionId: d.positionId ?? null, enabled: d.enabled,
      roleIds: d.roleIds ?? [],
    })
    show.value = true
  } catch (e) {
    message.error(translateError(e))
  }
}

async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) {
      const body: AddUserInput = {
        account: form.account,
        password: form.password || undefined, // 留空 → 省略字段 → 后端生成随机强口令(明文经出参回传)
        name: form.name, orgId: form.orgId, positionId: form.positionId, enabled: form.enabled,
        roleIds: form.roleIds,
      }
      const out = await userApi.add(body)
      // 管理员没自己指定口令时,系统生成的随机口令只有这一次机会展示——不弹出来这个号谁也登不进去。
      // 复用重置密码那套只读+复制弹层(同一语义:明文仅供管理员当场转达)。
      if (!form.password) {
        resetResult.value = out.initialPassword
        resultFromCreate.value = true
        showResetResult.value = true
      }
    } else {
      const body: UpdateUserInput = {
        name: form.name, orgId: form.orgId, positionId: form.positionId, enabled: form.enabled,
        roleIds: form.roleIds,
      }
      await userApi.update(editingId.value, body)
    }
    message.success(t('user.saved'))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 重置密码(输入新密码 / 留空则系统随机生成)→ 成功后只读展示实际生效密码 ──
// 同一个结果弹层也服务「留空口令建号」(见 save):两者语义相同——明文只此一次,管理员当场转达。
const showReset = ref(false)
const resetTarget = ref<UserItem | null>(null)
const resetForm = reactive({ newPassword: '' })
const showResetResult = ref(false)
const resetResult = ref('')
/** 结果弹层是"建号后"还是"重置后"打开的,只影响标题文案。 */
const resultFromCreate = ref(false)

function openReset(r: UserItem) {
  resetTarget.value = r
  resetForm.newPassword = ''
  showReset.value = true
}
async function doReset() {
  if (!resetTarget.value) return
  try {
    resetResult.value = await userApi.resetPassword(resetTarget.value.id, resetForm.newPassword || null)
    resultFromCreate.value = false
    showResetResult.value = true // 关闭输入弹层、弹出结果
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
async function copyResult() {
  try {
    await navigator.clipboard.writeText(resetResult.value)
    message.success(t('user.copied'))
  } catch {
    message.error(t('user.copyFailed'))
  }
}
</script>

<template>
  <div class="user-layout">
    <!-- 左侧机构树筛选 -->
    <n-card class="org-filter" :bordered="false" size="small">
      <div class="org-filter-head">
        <span class="org-filter-title">{{ t('user.org') }}</span>
        <n-button v-if="selectedOrgId != null" text size="tiny" type="primary" @click="selectedOrgId = null">
          {{ t('user.allOrgs') }}
        </n-button>
      </div>
      <n-tree
        block-line
        selectable
        :data="orgTree"
        key-field="id"
        label-field="name"
        children-field="children"
        :selected-keys="selectedOrgId == null ? [] : [selectedOrgId]"
        @update:selected-keys="onOrgSelect"
      />
    </n-card>

    <ProTable
      ref="tableRef"
      class="user-table"
      :columns="columns"
      :fetcher="userApi.page"
      :params="tableParams"
      storage-key="sys-user"
      :checked-row-keys="checkedKeys"
      @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
      @error="(e) => message.error(translateError(e))"
    >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/user'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
      </n-button>
      <n-button
        v-auth="'POST:/api/v1/sys/user/batch-delete'"
        type="error"
        :disabled="!hasSelection"
        @click="batchDelete"
      >
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('common.batchDelete') }}
      </n-button>
    </template>
    </ProTable>
  </div>

  <!-- 新增/编辑 -->
  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('user.addTitle') : t('user.editTitle')"
    :width="520"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="90">
      <n-form-item v-if="editingId === null" :label="t('user.account')" path="account">
        <n-input v-model:value="form.account" :placeholder="t('user.account')" />
      </n-form-item>
      <n-form-item v-if="editingId === null" :label="t('user.password')">
        <div class="pw-field">
          <n-input v-model:value="form.password" type="password" show-password-on="click" :placeholder="t('user.passwordHint')" />
          <PasswordStrength :value="form.password" />
        </div>
      </n-form-item>
      <n-form-item :label="t('user.name')" path="name">
        <n-input v-model:value="form.name" :placeholder="t('user.name')" />
      </n-form-item>
      <n-form-item :label="t('user.org')">
        <OrgTreeSelect v-model:value="form.orgId" :placeholder="t('user.orgPlaceholder')" />
      </n-form-item>
      <n-form-item :label="t('user.position')">
        <n-select v-model:value="form.positionId" :options="positionOptions" clearable :placeholder="t('user.positionPlaceholder')" />
      </n-form-item>
      <n-form-item :label="t('user.roles')">
        <n-select v-model:value="form.roleIds" :options="roleOptions" multiple clearable :placeholder="t('user.rolesPlaceholder')" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>

  <!-- 重置密码输入 -->
  <FormContainer
    v-model:show="showReset"
    :title="t('user.resetPassword')"
    :width="420"
    :on-confirm="doReset"
    :confirm-text="t('common.confirm')"
  >
    <n-form :model="resetForm" label-placement="left" :label-width="90">
      <n-form-item :label="t('user.newPassword')">
        <n-input v-model:value="resetForm.newPassword" type="password" show-password-on="click" :placeholder="t('user.newPasswordHint')" />
      </n-form-item>
    </n-form>
  </FormContainer>

  <!-- 初始密码结果(只读,可复制):建号留空口令 / 重置密码 共用 -->
  <FormContainer
    v-model:show="showResetResult"
    :title="resultFromCreate ? t('user.createDone') : t('user.resetDone')"
    :width="420"
    :on-confirm="() => {}"
    :confirm-text="t('common.confirm')"
  >
    <p class="reset-hint">{{ t('user.resetDoneHint') }}</p>
    <n-input :value="resetResult" readonly>
      <template #suffix>
        <n-button text type="primary" @click="copyResult">{{ t('user.copy') }}</n-button>
      </template>
    </n-input>
  </FormContainer>
</template>

<style scoped>
/* 密码框 + 强度条竖排(表单为 label-left 横排,此格内需纵向堆叠) */
.pw-field {
  flex: 1;
  min-width: 0;
}
.reset-hint {
  margin: 0 0 12px;
  font-size: var(--font-size-sm, 13px);
  color: var(--color-text-secondary, #888);
}
/* 左树 + 右表:窄屏下机构树收窄,不换行(树本身可竖向滚动) */
.user-layout {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.org-filter {
  flex: 0 0 220px;
  align-self: stretch;
}
.org-filter-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}
.org-filter-title {
  font-weight: 600;
  font-size: var(--font-size-sm, 13px);
}
.user-table {
  flex: 1;
  min-width: 0;
}
</style>
