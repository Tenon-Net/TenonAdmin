# Vue CRUD 实现示例

仅在实现 `web/` CRUD 时读取。入口流程、系统/业务模块分流、i18n、路由和验收仍以 [create-crud-frontend.md](../../create-crud-frontend.md) 为准。

## Types 示例

```typescript
// 在 web/src/types/api.ts 末尾追加

/** 职位行(后端 SysPosition) */
export interface SysPosition {
  id: number
  name: string
  code: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 职位新增/编辑入参(后端 PositionInput) */
export interface PositionInput {
  name: string
  code: string
  sort: number
  enabled: boolean
}
```


## API 示例

```typescript
// 在 web/src/api/index.ts 中追加

export const positionApi = {
  page: (params: { page: number; pageSize: number; name?: string; sortField?: string; sortOrder?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        params: {
          query: {
            ...pageParams(params),
            Name: params.name,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<SysPosition>(r)),
  add: (body: PositionInput) =>
    client.POST('/api/v1/sys/position/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: PositionInput) =>
    client.PUT('/api/v1/sys/position/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/position/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```


## 页面完整示例与可选模式

```vue
<script setup lang="ts">
import { h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NPopconfirm,
  NForm, NFormItem, NSwitch,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { positionApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { PositionInput, SysPosition } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<ProTableInst<SysPosition>>()

// 行数据 → 入参(StatusSwitch 行内改状态 + openEdit 回填共用)
const toInput = (r: SysPosition): PositionInput => ({
  name: r.name, code: r.code, sort: r.sort, enabled: r.enabled,
})

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
        disabled: !authStore.hasPerm('PUT:/api/v1/sys/position/{id}'),
        request: (next: boolean) =>
          positionApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => { r.enabled = v },
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
        authStore.hasPerm('PUT:/api/v1/sys/position/{id}')
          ? h(NButton, {
              size: 'small', quaternary: true, type: 'primary',
              onClick: () => openEdit(r),
            }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sys/position/{id}') ? h(NPopconfirm, {
          onPositiveClick: () =>
            run(() => positionApi.remove(r.id), t('position.deleted'))
              .then((ok) => { if (ok) tableRef.value?.refresh() }),
        }, {
          trigger: () => h(NButton, {
            size: 'small', quaternary: true, type: 'error',
          }, () => t('common.delete')),
          default: () => t('position.deleteConfirm', { name: r.name }),
        }) : null,
      ]),
  },
]

// ── 新增/编辑弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  name: {
    required: true, whitespace: true,
    message: () => t('position.nameRequired'),
    trigger: ['input', 'blur'],
  },
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
    return false  // 返回 false 阻止 FormContainer 关闭
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
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>
        {{ t('common.add') }}
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
        <n-input v-model:value="form.code" :placeholder="t('position.codePlaceholder')"
          :disabled="editingId !== null" />
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
```

### 关键模式速查

#### ProTable columns 搜索

列加 `search: true` 即自动出现在搜索栏，参数名 = `key`：

```typescript
{ key: 'name', title: () => t('xxx.name'), search: true }
```

#### StatusSwitch（无独立启停端点时）

走全量 update，并用权限 store 置灰：

```typescript
render: (r) => h(StatusSwitch, {
  value: r.enabled,
  disabled: !authStore.hasPerm('PUT:/api/v1/sys/xxx/{id}'),
  request: (next: boolean) => api.update(r.id, { ...toInput(r), enabled: next }),
  'onUpdate:value': (v: boolean) => { r.enabled = v },
})
```

#### 删除确认

用 `NPopconfirm` + `useConfirm().run()`：

```typescript
h(NPopconfirm, {
  onPositiveClick: () =>
    run(() => api.remove(r.id), t('xxx.deleted'))
      .then((ok) => { if (ok) tableRef.value?.refresh() }),
}, {
  trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
  default: () => t('xxx.deleteConfirm', { name: r.name }),
})
```

#### FormContainer save 协议

- `on-confirm` 接收异步函数
- 返回 `false` 或抛异常 → 弹窗保持打开（用于校验失败/接口报错）
- 正常结束 → 弹窗自动关闭

#### 批量删除（useBatchDelete）

表格加勾选 + 工具栏批量删除按钮：

```typescript
// script setup 中
import { useBatchDelete } from '@/composables/useBatchDelete'

const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: positionApi.batchRemove,  // 需要 API 中有 batchRemove 方法
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('position.deleted'),
})
```

```vue
<!-- template 中 -->
<ProTable
  v-model:checked-row-keys="checkedKeys"
  :row-key="(r) => r.id"
  ...
>
  <template #toolbar>
    <n-button type="error" :disabled="!hasSelection" @click="batchDelete">
      {{ t('common.batchDelete') }}
    </n-button>
  </template>
</ProTable>
```

`useBatchDelete` 内部已封装：勾选态管理 + 二次确认对话框 + 成功后清选并刷新。

#### 字典字段

需要字典选择框的字段用 `DictSelect`/`DictTag`：

```typescript
import DictSelect from '@/components/DictSelect/index.vue'
import DictTag from '@/components/DictTag/index.vue'

// 列渲染
{ key: 'gender', render: (r) => h(DictTag, { code: 'gender', value: r.gender }) }

// 表单
<DictSelect v-model:value="form.gender" code="gender" />
```
