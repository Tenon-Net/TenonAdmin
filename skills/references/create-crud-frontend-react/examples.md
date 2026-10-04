# React CRUD 实现示例

仅在实现 `web-react/` CRUD 时读取。入口流程、系统/业务模块分流、i18n、路由和验收仍以 [create-crud-frontend-react.md](../../create-crud-frontend-react.md) 为准。

## Types 示例

```typescript
/** 职位行(后端 SysPosition)。int64 收敛为 number。 */
export interface SysPosition {
  id: number
  name: string
  code: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 职位新增/编辑入参(后端 PositionInput;增改同一份字段)。 */
export interface PositionInput {
  name: string
  code: string
  sort: number
  enabled: boolean
}
```


## API 示例

```typescript
export const positionApi = {
  /** 职位分页;搜索键 name → PascalCase Name;sortField/sortOrder → 后端安全排序。 */
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
  add: (body: PositionInput) => client.POST('/api/v1/sys/position/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: PositionInput) =>
    client.PUT('/api/v1/sys/position/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/position/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```


## 页面完整示例与可选模式

```tsx
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, InputNumber, Space, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { positionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { PositionInput, SysPosition } from '@/types/api'

export default function PositionPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 行数据 → 入参(StatusSwitch 行内改状态 + openEdit 回填共用)
  const toInput = (r: SysPosition): PositionInput => ({
    name: r.name, code: r.code, sort: r.sort, enabled: r.enabled,
  })

  // 分页取数:ProTable 搜索表单值(unknown)→ positionApi.page 强类型入参。有意不 memo——
  // ProTable 经 ref 读 request,父组件重渲染不会触发重取;加 useCallback 反而是 stale-closure footgun。
  const fetchPositions: PageFetcher<SysPosition> = (q) =>
    positionApi.page({
      page: q.page,
      pageSize: q.pageSize,
      name: typeof q.name === 'string' ? q.name : undefined,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })

  // ── 新增/编辑弹窗(FormContainer owns loading+close)──
  const [form] = Form.useForm<PositionInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openAdd = () => {
    setEditingId(null)
    form.setFieldsValue({ name: '', code: '', sort: 0, enabled: true })
    setOpen(true)
  }
  const openEdit = useCallback(
    (r: SysPosition) => {
      setEditingId(r.id)
      form.setFieldsValue(toInput(r))
      setOpen(true)
    },
    [form],
  )

  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    try {
      if (editingId === null) await positionApi.add(v)
      else await positionApi.update(editingId, v)
      message.success(t('position.saved'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false // 留在弹层,不关
    }
  }

  const handleDelete = useCallback(
    (r: SysPosition) => {
      confirm({ content: t('position.deleteConfirm', { name: r.name }), action: () => positionApi.remove(r.id), successMsg: t('position.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<SysPosition>[]>(
    () => [
      { title: t('position.name'), dataIndex: 'name' }, // 无 search:false → 默认可搜,唯一搜索项
      { title: t('position.code'), dataIndex: 'code', search: false },
      { title: t('position.sort'), dataIndex: 'sort', search: false, width: 90 },
      {
        title: t('common.status'), dataIndex: 'enabled', search: false, width: 90,
        render: (_, r) => (
          <StatusSwitch
            value={r.enabled}
            disabled={!has('PUT:/api/v1/sys/position/{id}')}
            request={(next) => positionApi.update(r.id, { ...toInput(r), enabled: next })}
            onChange={reload}
          />
        ),
      },
      { title: t('common.createTime'), dataIndex: 'createTime', search: false },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 140, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            {has('PUT:/api/v1/sys/position/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {has('DELETE:/api/v1/sys/position/{id}') && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, reload, openEdit, handleDelete],
  )

  return (
    <>
      <DataTable<SysPosition>
        ref={tableRef}
        columns={columns}
        fetcher={fetchPositions}
        persistKey="sys-position"
        toolbar={
          <Can code="POST:/api/v1/sys/position/add">
            <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
          </Can>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('position.addTitle') : t('position.editTitle')}
        width={480}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item
            name="name" label={t('position.name')}
            rules={[{ required: true, whitespace: true, message: t('position.nameRequired') }]}
          >
            <Input placeholder={t('position.name')} />
          </Form.Item>
          <Form.Item name="code" label={t('position.code')}>
            {/* 这个页面选择建后不改岗位编码；属于 UI 约束 */}
            <Input disabled={editingId !== null} placeholder={t('position.codePlaceholder')} />
          </Form.Item>
          <Form.Item name="sort" label={t('position.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
}
```

### 关键模式速查

#### ProTable columns 搜索(注意:语义与 Vue 版相反)

antd ProTable 的列**默认就出现在搜索栏**(只要有 `dataIndex`)。所以套路是给**不想搜**的列都点名 `search: false`,想搜的那一两列**什么都不用加**——不是像 Vue/Naive ProTable 那样给想搜的列加 `search: true`。抄 Vue 版肌肉记忆到这里会导致每一列都出现在搜索栏。

```typescript
{ title: t('xxx.name'), dataIndex: 'name' },              // 可搜(默认)
{ title: t('xxx.code'), dataIndex: 'code', search: false }, // 不可搜
```

#### StatusSwitch(无独立启停端点时)

走全量 update,`disabled` 用 `useHasPerm()` 控权限:

```tsx
render: (_, r) => (
  <StatusSwitch
    value={r.enabled}
    disabled={!has('PUT:/api/v1/sys/xxx/{id}')}
    request={(next) => api.update(r.id, { ...toInput(r), enabled: next })}
    onChange={reload}
  />
)
```

**全量 update 语义下少回传一个字段就会把该行其它字段抹空**——`toInput(r)` 必须带全部业务字段,这是行内启停(StatusSwitch)与编辑回填(openEdit)共用同一个映射函数的唯一原因,别为图省事各写各的。

#### 删除确认

用 `useConfirm().confirm`,不需要额外的 Popconfirm 组件(那是 Vue/Naive 的模式,antd 版走 `useConfirm` 统一弹 `Modal.confirm`):

```typescript
const handleDelete = useCallback(
  (r: Entity) => {
    confirm({ content: t('xxx.deleteConfirm', { name: r.name }), action: () => api.remove(r.id), successMsg: t('xxx.deleted') })
      .then((ok) => { if (ok) reload() })
  },
  [confirm, t, reload],
)
```

#### FormContainer save 协议

- `onConfirm` 接收异步函数
- 校验失败(`form.validateFields()` 抛)或返回 `false` → 弹窗保持打开
- 正常结束(含 `resolve(undefined)`)→ 弹窗自动关闭,loading 全程由 `FormContainer` 管

#### 批量删除(useBatchDelete)

岗位没有批量删除端点——真实范例是角色页(`views/system/role/index.tsx`,`roleApi.batchRemove`)。要给新模块加,先在后端补 `batch-delete` 端点(`create-crud-backend.md` 的「批量删除」节),再:

```typescript
const batch = useBatchDelete({ remove: roleApi.batchRemove, refresh: reload, successMsg: t('role.deleted') })
```

```tsx
<DataTable
  rowSelection={{ selectedRowKeys: batch.selectedKeys, onChange: batch.setSelectedKeys }}
  toolbar={
    <Can code="POST:/api/v1/sys/role/batch-delete">
      <Button danger disabled={!batch.hasSelection} onClick={batch.run}>{t('common.batchDelete')}</Button>
    </Can>
  }
/>
```

`useBatchDelete` 内部已封装:勾选态 + 二次确认 + 执行 + 成功后清选并刷新(失败保留勾选让用户重试)。

#### 字典字段

```tsx
import { DictSelect } from '@/components/DictSelect'
import { DictTag } from '@/components/DictTag'

// 列渲染
{ title: t('xxx.gender'), dataIndex: 'gender', search: false, render: (_, r) => <DictTag typeCode="gender" value={r.gender} /> }

// 表单
<Form.Item name="gender" label={t('xxx.gender')}>
  <DictSelect typeCode="gender" allowClear />
</Form.Item>
```
