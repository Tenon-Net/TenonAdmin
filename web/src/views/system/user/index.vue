<script setup lang="ts">
// 用户管理(写侧)= ProTable(列表/搜索/分页)+ UserFormModal(新增/编辑)+ ResetPasswordModal(重置/初始口令展示)+ 专用启停端点。
// 超管行(isSuperAdmin)删除/停用置灰防自锁;启停走专用 setEnabled(非全量 update)。
import { computed, h, onMounted, ref, watch } from 'vue'
import { NButton, NCard, NTree, NSpace, NTag, NAvatar, NPopconfirm, useMessage } from 'naive-ui'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import DictTag from '@/components/DictTag/index.vue'
import UserFormModal from './components/UserFormModal.vue'
import ResetPasswordModal from './components/ResetPasswordModal.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { userApi, positionApi, roleApi, orgApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { buildTree, type Tree } from '@/utils/tree'
import type { SysOrg, UserItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<UserItem>>()
const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: userApi.batchRemove,
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('user.deleted'),
})

// 新增/编辑弹窗 + 重置密码弹窗(表单/校验/保存均在各自组件内,父页只传下拉选项 + 收 saved/passwordGenerated)。
const userFormRef = ref<InstanceType<typeof UserFormModal> | null>(null)
const resetModalRef = ref<InstanceType<typeof ResetPasswordModal> | null>(null)

// 职位/角色/主管下拉:各拉一页足量。ponytail: 200 覆盖绝大多数系统;真超再上分页搜索。
// roleOptions 一份两用:既喂编辑弹窗,也当列表页「角色」搜索项的选项源(ProTable 支持 Ref 选项源)。
const positionOptions = ref<{ label: string; value: number }[]>([])
const roleOptions = ref<{ label: string; value: number }[]>([])
const directorOptions = ref<{ label: string; value: number }[]>([])
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
    // 主管候选就是用户自身;拉一页足量作下拉源(允许把某用户选为自己主管,后台系统无害,不加环检)
    const { items } = await userApi.page({ page: 1, pageSize: 200 })
    directorOptions.value = items.map((u) => ({ label: `${u.name}(${u.account})`, value: u.id }))
  } catch {
    // 静默:主管下拉是配角,拉取失败不打断列表
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

// 头像首字母兜底。NAvatar 的规矩:default 插槽一有内容就渲染它、彻底无视 src(见 naive Avatar.render)。
// 所以有头像时 default 必须留空让 <img> 出来,首字母只放 #fallback(图挂了/链过期才兜底);无头像才用 default 当文字头像。
const initial = (name?: string | null) => (name || '?').slice(0, 1)

const columns: ProTableColumn<UserItem>[] = [
  // 超管行禁勾:批量删除同样不可含超管(后端也会整体拒绝)
  { type: 'selection', disabled: (r: UserItem) => r.isSuperAdmin },
  {
    key: 'avatar',
    title: () => t('user.avatar'),
    width: 64,
    align: 'center',
    hideInSetting: false,
    render: (r) =>
      h(
        NAvatar,
        { round: true, size: 'small', src: r.avatar || undefined },
        r.avatar ? { fallback: () => initial(r.name) } : { default: () => initial(r.name) },
      ),
  },
  { key: 'account', title: () => t('user.account'), search: true, sorter: true },
  { key: 'name', title: () => t('user.name'), search: true },
  { key: 'phone', title: () => t('user.phone'), render: (r) => r.phone || '—' },
  {
    key: 'gender',
    title: () => t('user.gender'),
    width: 80,
    hideInTable: true, // 次要列:默认隐藏收窄整表,列设置抽屉可打开
    render: (r) => (r.gender ? h(DictTag, { typeCode: 'gender', value: r.gender }) : '—'),
  },
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
  { key: 'positionName', title: () => t('user.position'), hideInTable: true, render: (r) => r.positionName || '—' },
  {
    key: 'enabled',
    title: () => t('user.status'),
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        // 超管不可停用(防自锁 —— 停了就没法从 UI 恢复;后端也保护);无启停权限亦置灰。
        disabled: r.isSuperAdmin || !authStore.hasPerm('PUT:/api/v1/sys/user/{id}/enabled'),
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
    fixed: 'right', // 钉右:横向滚动时操作列始终可见
    hideInSetting: true,
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('PUT:/api/v1/sys/user/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => userFormRef.value?.openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('PUT:/api/v1/sys/user/{id}/password')
          ? h(NButton, { size: 'small', quaternary: true, onClick: () => resetModalRef.value?.openReset(r) }, () => t('user.resetPassword'))
          : null,
        r.isSuperAdmin || !authStore.hasPerm('DELETE:/api/v1/sys/user/{id}')
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
      :columns="columns"
      :fetcher="userApi.page"
      :params="tableParams"
      storage-key="sys-user"
      :checked-row-keys="checkedKeys"
      @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
      @error="(e) => message.error(translateError(e))"
    >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/user'" type="primary" @click="userFormRef?.openAdd()">
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

  <UserFormModal
    ref="userFormRef"
    :position-options="positionOptions"
    :role-options="roleOptions"
    :director-options="directorOptions"
    @saved="() => tableRef?.refresh()"
    @password-generated="(p) => resetModalRef?.showResult(p, true)"
  />

  <ResetPasswordModal ref="resetModalRef" />
</template>

<style scoped>
/* 左树 + 右表:窄屏下机构树收窄,不换行(树本身可竖向滚动) */
.user-layout {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.org-filter {
  flex: 0 0 200px;
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
/* ProTable 设了 inheritAttrs:false,class 会转发到内部 data-table,拿不到布局;需 :deep 命中真正根元素 .pro-table */
.user-layout > :deep(.pro-table) {
  flex: 1;
  min-width: 0;
}
</style>
