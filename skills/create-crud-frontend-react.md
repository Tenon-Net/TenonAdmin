# 创建前端 CRUD 页面 · React 版 (Create Frontend CRUD, web-react)

本文是 `web-react/`(React 19 + antd 6)版。`web/`(Vue 3 + Naive UI)用 `create-crud-frontend.md`。两模板**零共享、双维护是设计决定**,见 `docs/react-template-ledger.md`——本文的示例代码全部改编自 `web-react/` 真实源码,不与 Vue 版共用一字。

为一个已有后端 API 创建完整的前端 CRUD 页面。前提:后端 CRUD 已完成(参考 `create-crud-backend.md`)。

产出共 3 处文件改动。

## 第一步:确定模式

**这一步决定后面每个产出写进哪个文件,选错会给消费者制造永久合并冲突。**

消费者是 fork/degit `web-react/`、在上面长自己业务的。冲突只在**双方都改同一个文件**时发生,而 `types/api.ts`、`api/index.ts`、`locales/zh-CN.ts`/`en-US.ts` 是上游自留地、改动频繁。所以:

| 产出 | 系统模块(内核维护者) | 业务模块(消费者二开) |
|---|---|---|
| Types | 追加进 `web-react/src/types/api.ts` | **新建** `web-react/src/types/<模块>.ts` |
| API | 追加进 `web-react/src/api/index.ts` | **新建** `web-react/src/api/<域>.ts`,从 `./client` 导入 `client`,从 `./index` 导入 `unwrap`/`pageParams`/`toPage` |
| i18n | 追加进 `web-react/src/locales/zh-CN.ts` + `en-US.ts` | **新建** `web-react/src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`(glob 自动并入,无需注册;见 `web-react/src/locales/ext/README.md`) |
| 页面 | `web-react/src/views/<模块>/<实体>/index.tsx` | 同左(本来就是新文件) |

两种模式下页面代码本身完全一样,只是 import 来源不同(`@/api` → `@/api/<域>`,`@/types/api` → `@/types/<模块>`)。

`api/index.ts` 里 `unwrap`/`pageParams`/`toPage`/`ApiError` 这几个 export 站内并非到处都直接调用,但**是消费者写自己 API 模块的接缝**,别当"未使用导出"清掉。

**业务模块模式的自检**:做完跑 `git status`,`api/index.ts`、`types/api.ts`、`locales/zh-CN.ts`、`locales/en-US.ts` **必须一个都没被修改**。有的话就是放错了文件。

下面的模板按**系统模块**写,以真实存在的岗位管理(`SysPosition`/`positionApi`/`system/position`)为例。业务模块照上表换文件即可,代码形态不变。

## 前置步骤

确保后端已启动,然后重新生成 API 类型:

```bash
cd web-react && npm run gen:api
```

这会更新 `web-react/src/api/schema.d.ts`,让 `openapi-fetch` 的 `client` 获得新端点的类型。

---

## 产出 1:Types(类型定义)

文件:系统模块 → 追加进 `web-react/src/types/api.ts`;**业务模块 → 新建 `web-react/src/types/<模块>.ts`**(见「第一步:确定模式」)。

### 规则

- **行类型**(列表展示用):`{Entity}` 接口,字段与后端实体对齐,`id: number` + 业务字段 + `createTime?: string`
- **输入类型**(新增/编辑用):`{Entity}Input` 接口,只含业务字段(无 id、无 createTime)
- 后端 `long`(int64)在前端统一收敛为 `number`
- 可选字段用 `?`,后端可为 null 的用 `| null`

### 参考模板(`types/api.ts` 里的真实岗位类型)

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

/** 职位新增/编辑入参(后端 PositionInput;增改同一份字段,code 编辑禁用)。 */
export interface PositionInput {
  name: string
  code: string
  sort: number
  enabled: boolean
}
```

---

## 产出 2:API 函数

文件:系统模块 → 追加进 `web-react/src/api/index.ts`;**业务模块 → 新建 `web-react/src/api/<域>.ts`**,顶部写 `import { client } from './client'` + `import { unwrap, pageParams, toPage } from './index'`(见「第一步:确定模式」)。

### 规则

- 对象名 camelCase:`{module}Api`
- 四个标准方法:`page`, `add`, `update`, `remove`
- `page` 入参含 `{ page, pageSize, ...过滤字段, sortField?, sortOrder? }`
  - 用 `...pageParams(params)` 展开为后端的 `{ Current, Size }`(`pageParams` 定义:`(p) => ({ Current: p.page, Size: p.pageSize })`)
  - 过滤字段手动映射为 PascalCase(后端 record 属性是 PascalCase),`SortField`/`SortOrder` 同样透传
  - 返回 `toPage<T>(r)` → `{ items: T[], total: number }`
- `add` / `update` / `remove` 用 `unwrap<T>(r)` 解包
- 路由字符串与后端 Controller 路由一致
- **仅系统模块**:别忘了在文件顶部的 `import type { ... }` 中追加新类型(见「容易忽略的点」)

### 参考模板(`api/index.ts` 里的真实 `positionApi`)

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

---

## 产出 3:页面(React 组件)

文件:`web-react/src/views/{module}/index.tsx`(如 `web-react/src/views/system/position/index.tsx`)。

### 规则

- 函数组件 + hooks,`export default function {Entity}Page()`
- **路由无需代码**——在后台「菜单管理」中新建菜单,`component` 字段填页面路径(如 `system/position/index`)。web-react 的菜单表单「组件路径」是**下拉**,选项由 `router/buildRoutes.tsx` 的 `viewKeysFrom` 从真实存在的 `.tsx` 文件反推,不会手滑打错——种子/历史数据里的 `component` 对不上任何真实文件时,菜单项仍在,路由渲染成 `MissingRoute` 占位视图并 `console.warn`;页面"打不开"时先查这里

### 核心组件及用法

| 组件/工具 | 来源 | 用途 |
|---|---|---|
| `DataTable` | `@/components/DataTable` | 隔离 `@ant-design/pro-components` 的表格封装:列定义驱动表格 + 搜索表单 + 分页 + 列设置持久化 |
| `FormContainer` | `@/components/FormContainer` | 新增/编辑弹窗(Modal/Drawer 二合一,跟随全局 `formStyle` 偏好;`onConfirm` 接管 loading + 关闭) |
| `StatusSwitch` | `@/components/StatusSwitch` | 行内启用/禁用切换(悲观更新,失败自动回滚) |
| `Can` | `@/components/Can` | 按钮权限门,替代 Vue 的 `v-auth`:`<Can code="POST:/api/v1/...">按钮</Can>` |
| `useHasPerm` | `@/stores/auth` | 权限判定 hook,返回 `(code) => boolean`;用于 `render` 回调里做不了 JSX 包裹的场景(行内按钮、`disabled` 判断) |
| `useConfirm` | `@/hooks/useConfirm` | `confirm(opts)` 二次确认 + 执行 + toast,`run(fn, msg)` 不弹框直接执行+toast,`ask(opts)` 仅确认不执行 |
| `useBatchDelete` | `@/hooks/useBatchDelete` | 批量删除:勾选态管理 + 二次确认 + 执行 + 成功后清选并刷新 |
| `DictSelect` / `DictTag` | `@/components/DictSelect` / `@/components/DictTag` | 字典下拉 / 字典值只读标签 |
| `translateError` | `@/utils/error` | 错误对象 → i18n 文案 |

### 页面结构模式

```
export default function EntityPage() {
  1. useTranslation / App.useApp(message) / useConfirm / useHasPerm
  2. tableRef + reload
  3. fetcher(PageFetcher<T>):ProTable 搜索表单值 → api.page 强类型入参
  4. 新增/编辑弹窗状态(antd Form 实例、open、editingId)
  5. openAdd / openEdit / save
  6. handleDelete(经 useConfirm)
  7. columns(useMemo)
  8. return <DataTable/> + <FormContainer><Form/></FormContainer>
}
```

### 参考模板(完整可运行,改编自 `views/system/position/index.tsx`)

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
            {/* 岗位编码建后不可改(后端也拒);编辑时置灰 */}
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

---

## i18n(容易漏)

文件:系统模块 → `web-react/src/locales/zh-CN.ts`(及 `en-US.ts`);**业务模块 → 新建 `web-react/src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`**,`export default { ... }` 直接写下面的键内容(文件名即顶层命名空间,glob 自动并入,无需注册)。见「第一步:确定模式」。

新模块需要三处 key:

### 1. 模块自身(顶层 key)

```typescript
// 在 zh-CN.ts 的对应位置追加
position: {
  title: '岗位管理',
  code: '岗位编码',
  name: '岗位名称',
  sort: '排序',
  codePlaceholder: '不填则自动生成',
  nameRequired: '请输入岗位名称',
  addTitle: '新增岗位',
  editTitle: '编辑岗位',
  deleteConfirm: '确定删除岗位「{name}」?',
  saved: '保存成功',
  deleted: '已删除',
},
```

插值占位符是**单花括号** `{name}`(沿用 Vue 模板文案写法,`locales/index.ts` 已把 i18next 默认的 `{{name}}` 改过来了)。

### 2. 错误码翻译(`error` 对象下)

```typescript
error: {
  // ...已有的...
  position: { notFound: '职位不存在', codeExists: '职位编码已存在' },
},
```

MsgKey 与后端 `ErrorCode` 的 `[MsgKey("error.position.notFound")]` 严格对应,去掉 `error.` 前缀后逐字对上。`translateError` 只按 msgKey 取字,从不读数字 code。

### 3. 通用 key(已有,无需重复添加)

`common.status`, `common.operation`, `common.add`, `common.edit`, `common.delete`, `common.save`, `common.createTime`, `common.batchDelete`, `common.batchDeleteConfirm` 等已在 `common` 下定义。

---

## 路由配置

**不需要写任何路由代码。** 在后台管理界面的「菜单管理」中:

1. 新建一个菜单节点
2. `component` 字段填写页面路径(如 `system/position/index`),对应 `web-react/src/views/` 下的文件——该字段是下拉,选项来自真实存在的 `.tsx` 文件,不会手滑打错
3. 菜单树一变,`useRoutes` 自然重渲染、重新匹配——不需要 Vue 版那种"重建后当前 URL 得手动重解析"的 trick

**系统模块**还需要在后端 `DefaultMenuSeed.cs` 中添加菜单种子数据(含权限按钮),否则首次启动页面不会出现。详见 `create-crud-backend.md` 的「容易忽略的点 → 菜单种子数据」(后端种子数据两模板共用同一份)。

---

## 容易忽略的点

### 1. antd v6 改名 props——写前必查

antd 6 相对 5 有一批静默改名的 props,`tsc` 不会报错(旧名多半仍能编译,只是不生效或行为变了),常见的:

- `Select` 的 `filterOption` / `optionFilterProp` / `onSearch` 顶层属性已废弃,收进 `showSearch` **对象**:`showSearch={{ optionFilterProp: 'label' }}`
- `Tag` 默认 `variant="filled"` 即无边框,不要再写废弃的 `bordered`
- `Modal.styles.body` 取代 `bodyStyle`

写含 antd 组件的代码前先跑离线 CLI 核对:`antd info <Component> --version 6.x`(必要时 `antd demo <Component>`),别凭记忆写。

### 2. zustand selector 必须返回原始值或稳定引用

`useHasPerm`、`useDictOptions`、`useAppStore(s => ...)` 这类 selector 一旦返回**新对象或新闭包**,每次渲染都判定"变了"从而重渲染,组件树会**无限重渲染**。永远只选原始值(`s.enabled`)或已经稳定的引用,不要在 selector 里 `{ ...s }` 或 `s.list.map(...)`。

### 3. `<Can code="VERB:/api/v1/...">` 权限码即规范化路由

值必须是 **`METHOD:/路由模板`**,与后端 Controller 路由、菜单种子 Permission、页面里 `useHasPerm()` 的调用点**四处完全一致**:

```tsx
<Can code="POST:/api/v1/sys/position/add">...</Can>
has('PUT:/api/v1/sys/position/{id}')
has('DELETE:/api/v1/sys/position/{id}')
```

路径参数用 `{id}` 占位(与路由模板一致),不是具体数字。

### 4. `persistKey` 全局唯一

`DataTable` 的 `persistKey` 落到 `localStorage['protable:{persistKey}']`,持久化列设置/密度偏好。每个页面必须全局唯一,建议命名 `{模块}-{实体}`(如 `sys-position`)。

### 5. 新增/编辑共用表单时的字段区分

某些字段新增时可编辑、编辑时只读(如 `code`)。用 `disabled={editingId !== null}` 控制。新增/编辑字段差异大时,考虑两个 `FormContainer` 而非一个共享表单。

### 6. 单文件默认,别现建 `columns.ts`/`api.ts` 镜像树

页面单文件是默认(`views/{模块}/index.tsx`)。只有页面 >~350 行或含多个重弹窗时才拆到同目录的 `components/`。仓库里 `positionForm.ts` 那种把行→入参映射抽成独立文件的做法,是**因为该映射被行内启停与编辑回填两处共用、且全量 update 语义下漏字段会静默清空数据**才值得单独钉住,不是常规套路——没有这个风险就照上面模板直接写在组件里的一个局部函数。

### 7. `api/index.ts` 顶部 import 行(**仅系统模块**)

追加 API 对象后,别忘了在文件开头的 `import type { ... }` 中补上新类型:

```typescript
import type { ..., SysPosition, PositionInput } from '@/types/api'
```

这行很长,容易漏加,漏了 TypeScript 会报错但错误信息指向 API 函数而非 import。

**业务模块不适用**——你的 API 在自己的 `api/<域>.ts` 里,类型从自己的 `@/types/<模块>` 导入,压根不碰 `api/index.ts`。

### 8. i18n 双语键两份都要加

`zh-CN.ts` 加了对应 `en-US.ts` 不加,英文界面会显示 i18next 的 key 兜底文本(或直接报 key 未找到的调试字符串)。业务模块同理:`ext/zh-CN/<模块>.ts` 和 `ext/en-US/<模块>.ts` 两个文件都要建。

---

## 检查清单

```bash
cd web-react
npm run typecheck   # 类型检查通过
npm run lint        # 代码规范检查通过
npm run dev          # 启动开发服务器(:5174),浏览器访问确认页面正常
```

手动验证:
- [ ] 表格加载、搜索(只有意图内的列出现在搜索栏)、分页正常
- [ ] 新增保存成功、表格刷新
- [ ] 编辑回填正确、保存成功
- [ ] 删除确认弹窗 → 删除成功 → 表格刷新
- [ ] StatusSwitch 切换后刷新页面状态不回弹
- [ ] 无权限的按钮被 `<Can>`/`useHasPerm` 隐藏
- [ ] 错误提示显示中文(i18n key 正确),切到英文界面文案也对得上
