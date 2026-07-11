<script setup lang="ts">
// 用户管理(写侧)= ProTable(列表/搜索/分页)+ 手写弹窗 CRUD + 重置密码 + 专用启停端点。
// 角色多选:新增/编辑均可分配角色(选项来自 roleApi 拉一大页);编辑回显走 userApi.detail 的 roleIds。
// 超管行(isSuperAdmin)删除/停用置灰防自锁;启停走专用 setEnabled(非全量 update)。
import { h, onMounted, reactive, ref } from 'vue'
import {
  NButton, NSpace, NTag, NInput, NForm, NFormItem, NSelect, NSwitch, NPopconfirm,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { userApi, positionApi, roleApi } from '@/api'
import { translateError } from '@/utils/error'
import type { AddUserInput, UpdateUserInput, UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<UserItem>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: userApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('user.deleted'),
})

// 职位/角色下拉:各拉一页足量。ponytail: 200 覆盖绝大多数系统;真超再上分页搜索。
const positionOptions = ref<{ label: string; value: number }[]>([])
const roleOptions = ref<{ label: string; value: number }[]>([])
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
})

const columns: ProTableColumn<UserItem>[] = [
  // 超管行禁勾:批量删除同样不可含超管(后端也会整体拒绝)
  { type: 'selection', disabled: (r: UserItem) => r.isSuperAdmin },
  { key: 'account', title: () => t('user.account'), search: true },
  { key: 'name', title: () => t('user.name'), search: true },
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
  { key: 'createTime', title: () => t('user.createTime'), format: 'datetime' },
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
        password: form.password || undefined, // 留空 → 省略字段 → 后端用默认初始密码
        name: form.name, orgId: form.orgId, positionId: form.positionId, enabled: form.enabled,
        roleIds: form.roleIds,
      }
      await userApi.add(body)
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

// ── 重置密码(输入新密码 / 留空取默认)→ 成功后只读展示实际生效密码 ──
const showReset = ref(false)
const resetTarget = ref<UserItem | null>(null)
const resetForm = reactive({ newPassword: '' })
const showResetResult = ref(false)
const resetResult = ref('')

function openReset(r: UserItem) {
  resetTarget.value = r
  resetForm.newPassword = ''
  showReset.value = true
}
async function doReset() {
  if (!resetTarget.value) return
  try {
    resetResult.value = await userApi.resetPassword(resetTarget.value.id, resetForm.newPassword || null)
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
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="userApi.page"
    :title="t('user.title')"
    :labels="labels"
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
        <n-input v-model:value="form.password" type="password" show-password-on="click" :placeholder="t('user.passwordHint')" />
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

  <!-- 重置结果(只读,可复制)-->
  <FormContainer
    v-model:show="showResetResult"
    :title="t('user.resetDone')"
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
.reset-hint {
  margin: 0 0 12px;
  font-size: var(--font-size-sm, 13px);
  color: var(--color-text-secondary, #888);
}
</style>
