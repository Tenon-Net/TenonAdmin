# 创建前端 CRUD 页面 (Create Frontend CRUD)

为一个已有后端 API 创建完整的前端 CRUD 页面。前提：后端 CRUD 已完成（参考 `create-crud-backend.md`）。

> **本文是 `web/`（Vue 3 + Naive UI）版**；`web-react/`（React 19 + antd 6）用 `create-crud-frontend-react.md`。两套模板零共享、各自维护是产品决定（`docs/react-template-ledger.md`），别把一边的写法搬到另一边。

产出为 Types、API、页面与中英 i18n；系统模块修改共享文件，业务模块按下表新建独立文件。

开始实现前，按 [AGENTS.md：Vue 开发规范](../AGENTS.md#vue-开发规范) 加载基础与相关专项技能。下方示例用于说明项目接入方式，Vue 写法与组件边界遵循该规范。

## 第一步：确定模式

**这一步决定后面每个产出写进哪个文件，选错会给消费者制造永久合并冲突。**

消费者是 fork 本仓库、在 `web/` 上长自己的业务的（见文档站 `guide/sync-fork`）。冲突只在**双方都改同一个文件**时发生，而 `types/api.ts`(28 次改动)、`api/index.ts`(34)、`locales/zh-CN.ts`(52) 恰是 `web/src` 里 churn 最高的几个文件。所以：

| 产出 | 系统模块（内核维护者） | 业务模块（消费者二开） |
|---|---|---|
| Types | 追加进 `web/src/types/api.ts` | **新建** `web/src/types/<模块>.ts` |
| API | 追加进 `web/src/api/index.ts` | **新建** `web/src/api/<域>.ts`，从 `./index` 导入 `unwrap`/`pageParams`/`toPage`/`ApiError` |
| i18n | 追加进 `web/src/locales/zh-CN.ts` + `en-US.ts` | **新建** `web/src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`（glob 自动并入，无需注册；见 `web/src/locales/ext/README.md`） |
| 页面 | `web/src/views/<模块>/<实体>/index.vue` | 同左（本来就是新文件） |

两种模式下页面代码本身完全一样，只是 import 来源不同（`@/api` → `@/api/<域>`，`@/types/api` → `@/types/<模块>`）。

**业务模块模式的自检**：做完跑 `git status`，`api/index.ts`、`types/api.ts`、`locales/zh-CN.ts`、`locales/en-US.ts` **必须一个都没被修改**。有的话就是放错了文件。

下面的模板按**系统模块**写。业务模块照上表换文件即可，代码形态不变。

## 前置步骤

确保后端已启动，然后重新生成 API 类型：

```bash
cd web && npm run gen:api
```

这会更新 `web/src/api/schema.d.ts`，让 `openapi-fetch` 的 `client` 获得新端点的类型。

---

## 产出 1：Types（类型定义）

文件：系统模块 → 追加进 `web/src/types/api.ts`；**业务模块 → 新建 `web/src/types/<模块>.ts`**（见「第一步：确定模式」）。

### 规则

- **行类型**（列表展示用）：`{Entity}` 接口，字段与后端实体对齐，`id: number` + 业务字段 + `createTime?: string`
- **输入类型**（新增/编辑用）：`{Entity}Input` 接口，只含业务字段（无 id、无 createTime）
- 后端 `long`（int64）在前端统一收敛为 `number`
- 可选字段用 `?` + `| null`

### 实现示例

实现 Types 时读取 [Vue CRUD Types 示例](references/create-crud-frontend/examples.md#types-示例)。

---

## 产出 2：API 函数

文件：系统模块 → 追加进 `web/src/api/index.ts`；**业务模块 → 新建 `web/src/api/<域>.ts`**，顶部写 `import { client } from './client'` + `import { unwrap, pageParams, toPage } from './index'`（见「第一步：确定模式」）。

### 规则

- 对象名 camelCase：`{module}Api`
- 四个标准方法：`page`, `add`, `update`, `remove`
- `page` 入参含 `{ page, pageSize, ...过滤字段, sortField?, sortOrder? }`
  - 用 `pageParams(params)` 映射为后端的 `{ Current, Size }`
  - 过滤字段手动映射为 PascalCase（后端 record 属性是 PascalCase）
  - 返回 `toPage<T>(r)` → `{ items: T[], total: number }`
- `add` / `update` / `remove` 用 `unwrap<T>(r)` 解包
- 路由字符串与后端 Controller 路由一致
- 别忘了在文件顶部的 `import type { ... }` 中追加新类型

### 实现示例

实现 API 时读取 [Vue CRUD 实现示例](references/create-crud-frontend/examples.md#api-示例)。

---

## 产出 3：Vue 页面

文件：`web/src/views/{module}/index.vue`（如 `web/src/views/system/position/index.vue`）。

### 规则

- 使用 `<script setup lang="ts">` + `<template>` 双段式
- **路由无需代码**——在后台「菜单管理」中新建菜单，`component` 字段填 `{path}/index`（如 `system/position/index`），动态路由自动注册

### 核心组件及用法

| 组件/工具 | 来源 | 用途 |
|---|---|---|
| `ProTable` | `tenon-naive-pro-table` | 列定义驱动表格 + 搜索表单 + 分页 + 列设置 |
| `FormContainer` | `@/components/FormContainer/index.vue` | 新增/编辑弹窗（自动跟随全局 modal/drawer 设置） |
| `StatusSwitch` | `@/components/StatusSwitch/index.vue` | 行内启用/禁用切换（悲观更新，失败自动回滚） |
| `useConfirm` | `@/composables/useConfirm` | `run(fn, msg)` 执行+toast，`confirm(opts)` 确认+执行 |
| `useBatchDelete` | `@/composables/useBatchDelete` | 批量删除勾选 + 确认 + 执行 |
| `AppIcon` | `@/components/AppIcon.vue` | Iconify 图标 |
| `v-auth` | `@/directives/auth` | 按钮权限（值 = 路由权限码，如 `POST:/api/v1/sys/position/add`） |
| `translateError` | `@/utils/error` | 错误对象 → i18n 文案 |

### 页面结构模式

```
<script setup>
  1. imports
  2. composables (useI18n, useMessage, useConfirm)
  3. tableRef
  4. toInput 辅助函数(行数据 → Input 类型)
  5. columns 数组(驱动表格+搜索+列设置)
  6. 表单状态(show, formRef, editingId, rules, blank, form)
  7. openAdd / openEdit / save 函数
</script>

<template>
  <ProTable :columns :fetcher :storage-key @error>
    <template #toolbar> 新增按钮(v-auth) </template>
  </ProTable>

  <FormContainer v-model:show :title :on-confirm="save">
    <n-form :model :rules> 表单字段 </n-form>
  </FormContainer>
</template>
```

### 实现示例

实现页面时读取 [Vue CRUD 完整页面与可选模式](references/create-crud-frontend/examples.md#页面完整示例与可选模式)。其中包含可运行页面、搜索、行内状态、删除确认、批量删除和字典字段。

---

## i18n（容易漏）

文件：系统模块 → `web/src/locales/zh-CN.ts`（及 `en-US.ts`）；**业务模块 → 新建 `web/src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`**，`export default { ... }` 直接写下面的键内容（文件名即顶层命名空间，glob 自动并入，无需注册）。见「第一步：确定模式」。

新模块需要三处 key：

### 1. 模块自身（顶层 key）

```typescript
// 在 zh-CN.ts 的对应位置追加
position: {
  title: '岗位管理',
  name: '岗位名称',
  code: '岗位编码',
  sort: '排序',
  codePlaceholder: '不填则自动生成',
  nameRequired: '请输入岗位名称',
  addTitle: '新增岗位',
  editTitle: '编辑岗位',
  deleteConfirm: '确定删除岗位「{name}」?',
  saved: '保存成功',
  deleted: '删除成功',
},
```

### 2. 错误码翻译（`error` 对象下）

```typescript
error: {
  // ...已有的...
  position: { notFound: '职位不存在', codeExists: '职位编码已存在' },
},
```

MsgKey 与后端 `ErrorCode` 的 `[MsgKey("error.position.notFound")]` 严格对应。

### 3. 通用 key（已有，无需重复添加）

`common.status`, `common.operation`, `common.add`, `common.edit`, `common.delete`, `common.save`, `common.createTime`, `common.batchDelete`, `common.batchDeleteConfirm` 等已在 `common` 下定义。

---

## 路由配置

**不需要写任何路由代码。** 在后台管理界面的「菜单管理」中：

1. 新建一个菜单节点
2. `component` 字段填写页面路径（如 `system/position/index`），对应 `web/src/views/` 下的文件
3. 动态路由会自动注册该页面

**系统模块**还需要在后端 `DefaultMenuSeed.cs` 中添加菜单种子数据（含权限按钮），否则首次启动页面不会出现。详见 `create-crud-backend.md` 的「容易忽略的点 → 菜单种子数据」。

---

## 容易忽略的点

### 1. `api/index.ts` 顶部 import 行（**仅系统模块**）

追加 API 对象后，别忘了在文件开头的 `import type { ... }` 中补上新类型：

```typescript
import type { ..., SysPosition, PositionInput } from '@/types/api'
```

这行很长，容易漏加，漏了 TypeScript 会报错但错误信息指向 API 函数而非 import。

**业务模块不适用**——你的 API 在自己的 `api/<域>.ts` 里，类型从自己的 `@/types/<模块>` 导入，压根不碰 `api/index.ts`。

### 2. `v-auth` 权限码格式

值是 **`METHOD:/路由模板`**，与后端 Controller 路由 + 菜单种子 Permission 完全一致：

```vue
<n-button v-auth="'POST:/api/v1/sys/position/add'" ...>
<n-button v-auth="'PUT:/api/v1/sys/position/{id}'" ...>
<n-button v-auth="'DELETE:/api/v1/sys/position/{id}'" ...>
```

路径参数用 `{id}` 占位（与路由模板一致），不是具体数字。

### 3. `storage-key` 唯一性

`ProTable` 的 `storage-key` 用于持久化列设置/密度偏好到 localStorage。每个页面的 key 必须全局唯一，建议命名 `{模块}-{实体}`（如 `sys-position`）。

### 4. 新增/编辑共用表单时的字段区分

某些字段在新增时可编辑、编辑时只读（如 `code`）。用 `:disabled="editingId !== null"` 控制。如果新增和编辑的字段差异较大，考虑用两个 `FormContainer` 而非一个。

---

## 检查清单

```bash
cd web
npm run typecheck   # 类型检查通过
npm run lint        # 代码规范检查通过
npm run dev         # 启动开发服务器，浏览器访问确认页面正常
```

手动验证：
- [ ] 表格加载、搜索、分页正常
- [ ] 新增保存成功、表格刷新
- [ ] 编辑回填正确、保存成功
- [ ] 删除确认弹窗 → 删除成功 → 表格刷新
- [ ] StatusSwitch 切换后刷新页面状态不回弹
- [ ] 无权限的按钮被 `v-auth` 隐藏
- [ ] 错误提示显示中文（i18n key 正确）
