# M3a-2 React port 复制/重写清单（2026-09）

本清单是 M3a-2 React port 的**文件级影响面台账**：逐文件写明「原样复制」「改写后复制」「完全重写」三类归属，供 [M3a-2 React 任务计划](./m3a2-react-task-plan-2026-09.md) 的 T01 基线复核直接引用。

事实源是代码本身。清单只描述当前工作区的真实状态，不预告尚未存在的文件；与代码冲突时以代码为准。

## 归属判定规则

| 类别 | 判定条件 | 目标位置 |
| --- | --- | --- |
| 原样复制 | 纯 TypeScript，不 import 任何 Vue/Naive UI 符号，不依赖响应式代理语义 | `web-react/src/workflow/` |
| 改写后复制 | 逻辑框架无关，但源文件里有少量 Vue 专属引用 | 同上，去掉 Vue 依赖 |
| 完全重写 | SFC、模板、Naive UI 组件、Vue 生命周期或 composable 语义 | `web-react/src/views/workflow/**` |

判定不看文件名，只看它是否真的与框架无关：`useRequestKey.ts` 名字像 composable，但它同时被判为**重写**，因为实现用了 `ref`。

## 一、纯 TS 内核 → 原样复制到 `web-react/src/workflow/`

| 文件 | 字节 | 复制方式 | 说明 |
| --- | --- | --- | --- |
| `schema.ts` | 5619 | 逐字节相同 | 节点/模型/表单类型定义，无运行时依赖 |
| `model.ts` | 14233 | 逐字节相同 | 节点工厂、树遍历、`validateModel` |
| `configuration.ts` | 8720 | 逐字节相同 | `applyNodeConfiguration` 及分层配置 |
| `formSchema.ts` | 20824 | 逐字节相同 | 10 种控件的 schema 与类型专属 props |
| `formRuntime.ts` | 8180 | 逐字节相同 | 值解析、序列化、按类型校验 |
| `configuration.spec.ts` | 21170 | 逐字节相同 | 不 import Vue |
| `formSchema.spec.ts` | 4798 | 逐字节相同 | 同上 |
| `formRuntime.spec.ts` | 3692 | 逐字节相同 | 同上 |
| `useRequestKey.spec.ts` | 1667 | 逐字节相同 | 只断言纯函数行为，因此可作为两侧实现对齐的证据 |

`wf-identity.css` 同样原样复制，但目标是 `web-react/src/views/workflow/wf-identity.css`（跟随视图目录，不进 `src/workflow/`）。

### 唯一需要改写的 spec：`model.spec.ts`

Vue 版本用 `reactive()` 包了两个对象，验证 `validateModel` 在响应式代理下同样正确。React 侧没有等价代理，改为直接传普通对象，断言完全保留：

- 删除首行 `import { reactive } from 'vue'`
- `const modelProxy = reactive(raw)` → `const modelProxy = raw`
- `const nodeProxy = reactive(branch)` → `const nodeProxy = branch`

改动仅 3 行，测试数量与断言语义不变（16841 vs 16892 字节）。

## 二、完全重写

### 2.1 `useRequestKey.ts`：改写成 React 友好工厂

Vue 版用 `ref<string | null>(null)` 持有请求键。React 版**刻意不用 `useRef`**：请求键不驱动渲染，用 `useRef` 只会把它绑死在组件树上并让单测必须走 renderer。改为普通闭包变量：

| Vue | React |
| --- | --- |
| `import { ref } from 'vue'` | 无 import |
| `const key = ref<string \| null>(null)` | `let key: string \| null = null` |
| `key.value` 读写（4 处） | `key` 读写（4 处） |

对外三个方法（取键、按结果结算、强制重置）签名与语义不变，所以 `useRequestKey.spec.ts` 可以逐字节复用——它通过即证明重写没有改变行为契约。同时把注释里的「composable」改为「工厂」。

### 2.2 视图层：19 个 `.vue` → `web-react/src/views/workflow/**/*.tsx`

Vue 侧共 4939 行模板/脚本，按重写体量排序：

| Vue 源文件 | 行数 | React 目标 |
| --- | --- | --- |
| `instance/detail.vue` | 807 | `instance/detail.tsx` |
| `definition/components/WfConfigDrawer.vue` | 668 | `definition/components/WfConfigDrawer.tsx` |
| `definition/designer.vue` | 385 | `definition/designer.tsx` |
| `definition/components/WfNodeChain.vue` | 345 | `definition/components/WfNodeChain.tsx` |
| `definition/components/WfNodeCard.vue` | 296 | `definition/components/WfNodeCard.tsx` |
| `definition/components/WfConditionEditor.vue` | 296 | `definition/components/WfConditionEditor.tsx` |
| `components/WfBuiltinForm.vue` | 287 | `components/WfBuiltinForm.tsx` |
| `start/index.vue` | 270 | `start/index.tsx` |
| `definition/components/WfFormDesigner.vue` | 186 | `definition/components/WfFormDesigner.tsx` |
| `monitor/index.vue` | 185 | `monitor/index.tsx` |
| `definition/index.vue` | 177 | `definition/index.tsx` |
| `delegation/index.vue` | 176 | `delegation/index.tsx` |
| `definition/components/WfAddNode.vue` | 152 | `definition/components/WfAddNode.tsx` |
| `done/index.vue` | 146 | `done/index.tsx` |
| `definition/components/WfNodeTree.vue` | 143 | `definition/components/WfNodeTree.tsx` |
| `components/WfFormMount.vue` | 133 | `components/WfFormMount.tsx` |
| `mine/index.vue` | 114 | `mine/index.tsx` |
| `todo/index.vue` | 91 | `todo/index.tsx` |
| `cc/index.vue` | 82 | `cc/index.tsx` |

### 2.3 单元测试：7 个 spec 就地重写为 `.spec.tsx`

与被测组件同目录，命名对齐 `web-react` 现有约定：

`components/WfBuiltinForm.spec.tsx`、`components/WfFormMount.spec.tsx`、`definition/components/WfAddNode.spec.tsx`、`definition/components/WfConditionEditor.spec.tsx`、`definition/components/WfConfigDrawer.spec.tsx`、`definition/components/WfFormDesigner.spec.tsx`、`instance/detail.spec.tsx`。

断言覆盖的行为契约（Webhook 六字段、并行臂增删、字段权限合并、多 Token 回放目标选择）必须保留；只有查询 DOM 的方式随 antd 改写。

### 2.4 Playwright e2e：4 个用例改 antd 选择器

`web/e2e/workflow-{layout,m2a,m2b,m3a2}.spec.ts` → `web-react/e2e/workflow-*.spec.ts`。Naive UI 的 `.n-message`、`.n-data-table` 等类名全部换成 antd 等价物；复用 `web-react/e2e/` 已有的 `helpers.ts`、`api.ts`、`portPair.mjs`，不新建一套夹具。

## 三、相关支撑文件

| 项目 | 归属 | 当前状态 |
| --- | --- | --- |
| `src/types/workflow.ts` | 原样复制（1565 字节，逐字节相同） | 已就位 |
| `src/api/workflow.ts` | 原样复制（7806 字节，逐字节相同） | 已就位 |
| `src/api/schema.d.ts` | 生成物，**不手工编辑** | 已含工作流 API |
| i18n `workflow` 命名空间 | 翻译内容复制，键结构不变 | 已并入 |
| i18n `error.code` 48xxx | 同上 | 已并入 |
| `WorkflowMenuSeed.cs` | **不改** | 无需改动 |

### 3.1 `api/workflow.ts` 为什么能原样复制

它只依赖两套模板都存在的同名 API 基建，没有任何框架符号：

- `import { client } from './client'`
- `import { pageParams, toPage, unwrap } from './index'`
- 其余全部是 `import type { ... }`，来自生成的 `schema.d.ts`

### 3.2 i18n：进 `zh-CN.ts`/`en-US.ts`，不进 `locales/ext/`

工作流是**内核产品能力**，不是消费者扩展，所以文案落在主 locale 文件：

| 内容 | web | web-react |
| --- | --- | --- |
| `workflow` 命名空间 | `zh-CN.ts:1219` 起约 401 行 | `zh-CN.ts:1250` 起 |
| `error.code` 48xxx | 39 条 | 39 条（zh-CN 与 en-US 各 39） |

`locales/ext/` 按设计留空给消费者，工作流文案放进去会被误当作可删的示例。48xxx 实际是 `48001–48047` 区间内的 39 条，中间 `48022`、`48028–48034` 未分配，复制时按后端实际错误码对齐，不要补造不存在的键。

### 3.3 菜单：seed 不动，靠约定落地

`backend/src/TenonAdmin.Workflow/WorkflowMenuSeed.cs` 的 9 条菜单 `Component` 路径与 React 视图路径天然一致，因此后端零改动：

| Component | 归属 | React 文件 |
| --- | --- | --- |
| `workflow/start/index` | 业务中心 | `views/workflow/start/index.tsx` |
| `workflow/todo/index` | 业务中心 | `views/workflow/todo/index.tsx` |
| `workflow/cc/index` | 业务中心 | `views/workflow/cc/index.tsx` |
| `workflow/mine/index` | 业务中心 | `views/workflow/mine/index.tsx` |
| `workflow/done/index` | 业务中心 | `views/workflow/done/index.tsx` |
| `workflow/definition/index` | 系统 | `views/workflow/definition/index.tsx` |
| `workflow/monitor/index` | 系统 | `views/workflow/monitor/index.tsx` |
| `workflow/delegation/index` | 系统 | `views/workflow/delegation/index.tsx` |
| `workflow/definition/designer` | 系统（`Visible=false`） | `views/workflow/definition/designer.tsx` |

`web-react/src/router/buildRoutes.tsx` 用 `import.meta.glob('/src/views/**/*.tsx')`（排除 `*.spec.tsx`）解析菜单落点，`detailRoutes.tsx` 另用 `'/src/views/**/detail.tsx'`。所以 `instance/detail.tsx` 走 detail 路由约定，其余按菜单 `Component` 自动匹配，**不需要手写路由表**。

### 3.4 port 之前的 `web-react` 基线

- `src/api/schema.d.ts` 已由真实 contract drift 生成，包含全部工作流 API
- 没有 `src/views/workflow/`，没有 `src/api/workflow.ts`，没有 `src/types/workflow.ts`，没有 `src/workflow/`
- 也就是说：**契约已通，产品面为零**

## 四、必须复用的 React 既有模式

| 能力 | React 实现 | 用途 |
| --- | --- | --- |
| 列表页 | `src/components/DataTable/DataTable.tsx` | 定义/待办/已办/我发起/抄送/监控/委托 7 个列表 |
| 表单容器 | `src/components/FormContainer.tsx` | 配置抽屉、委托规则、发起页 |
| 详情页 | `src/components/DetailPage.tsx` | 实例详情 |
| 声明式权限 | `src/components/Can.tsx` | 按钮级可见性 |
| 命令式权限 | `useHasPerm()`（`src/stores/auth.ts:111`） | 动作可用性判断；非组件处用 `hasPerm(state, code)` |
| 错误文案 | `translateError()`（`src/utils/error.ts:40`） | 48xxx 业务错误本地化 |
| 人员/附件/日期 | `UserSelect`、`FileUpload`、antd `DatePicker` | 内置表单控件 |
| 批量与二次确认 | `useBatchDelete`、`useConfirm` | 列表操作 |

**零跨模板引用**：`web-react` 不得 import `web/` 下任何文件，`web/` 同样不引用 `web-react/`。两套模板的重复是有意的产品决策，复制是唯一合法的共享方式。技术栈映射为 Naive UI → antd 6 + `@ant-design/pro-components`，zustand 选择器必须稳定。

## 五、当前完成情况（2026-09-17）

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 纯 TS 内核 9 文件 + `model.spec.ts` 改写 | 已就位 | 与 `web/` 逐字节一致（`model.spec` 仅去 `reactive`）；`npm test -- --run src/workflow/` → 5 files / 70 passed |
| `useRequestKey.ts` 闭包重写 | 已就位 | 6 条 spec 通过 |
| `types/workflow.ts`、`api/workflow.ts` | 已就位 | 与 web 侧 `diff` 无输出 |
| i18n `workflow` + 48xxx | 已就位 | zh-CN/en-US 各 39 条错误码 |
| `wf-identity.css` | 已就位 | 已复制 |
| 列表/发起/委托/详情骨架/设计器外壳 | 已就位（T05–T10） | 见 `views/workflow/**`；typecheck/lint 绿 |
| 树设计器组件（节点卡/抽屉/表单设计器） | 已就位（T11–T15） | `definition/components/**`；画布用 CSS `zoom` 50–200；保存前过 `validateModel` |
| 内置表单运行时与字段权限 | 已就位（T16–T18） | `components/WfBuiltinForm.tsx`、`WfFormMount.tsx`；权限按 `hidden > readonly > editable` 合并 |
| 详情动词与并行回放完善 | 已就位（T19–T22） | `instance/detail.tsx` 11 个动词 + 并行臂 + 多 Token 目标选择，写动作全过 `useRequestKey` |
| 7 个 `.spec.tsx` | **已就位（T23）** | 工作流子集 `src/workflow/ src/views/workflow/` → 14 files / 127 passed |
| 4 个 e2e 用例 | **已就位（T24）** | `e2e/workflow-{layout,m2a,m2b,m3a2}.spec.ts` → `npx playwright test workflow` 真后端 **5 passed**；新增 `e2e/antd.ts`、`e2e/workflow.ts` 两个夹具，`helpers.ts` 增 `enterBusinessApp` 处理跨应用路由 |

T01–T26 全部关闭，前端对等达成，**完整 M3a-2 成立**（不等于 GA）。全量 `npx vitest run` → 124 files / 935 passed；`typecheck`/`lint`/`build` 退出码 0。证据与残留风险见 [React 任务计划](./m3a2-react-task-plan-2026-09.md) 的验证证据段。
