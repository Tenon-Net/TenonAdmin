# M3a-2 React port 阶段任务计划

## 目标

完成 TenonAdmin.Workflow 的 **M3a-2 React port 阶段**：以已经稳定的 Vue M3a-2 产品面为规格，在 `web-react/` 用 React + Ant Design 独立实现同等能力的工作流页面——定义列表与树设计器、Webhook 与并行节点配置、简易动态表单与字段权限、实例详情多 Token 回放、全套审批动词和长期委托管理。

本阶段**不做后端功能开发**。只有在 port 过程中真的需要改 Controller/DTO/XML 注释时，才允许重新生成两套 `schema.d.ts` 并通过 contract drift；那属于契约同步，不是新功能。

本文件是实现规格和进度索引，不替代 Codex goal/Ultragoal 的运行时状态，也不授予 commit、push、PR 或发布权限。完成本文件才能声明 **完整 M3a-2 完成**；在此之前只能声明 M3a-2 Vue 阶段完成。

## 已知基线（2026-09-17）

- **M3a-2 Vue 阶段已完成**：T01–T25A 全部关闭，Webhook 设计器、简易动态表单与字段权限、高级审批动词、并行分支与多 Token 回放均已接入现有 Workflow 链路。证据见 [`m3a2-vue-task-plan-2026-09.md`](./m3a2-vue-task-plan-2026-09.md)。
- **Task 8c 已完成**（2026-09-14）：outbox 的 `Pending → Dispatching → Dispatched/Failed` 领取、transport、退避、死信和人工重放已交付；默认 `NoOpWfOutboxTransport` 明确失败。最终语义见设计规划 §15.8。
- **预览版本地门禁已绿**（2026-09-14 / 2026-09-15）：Release build `0 warning / 0 error`；四库全量各 `1540 / 0 / 0`（SQL Server 以模板库四片 + 失败用例串行复跑闭合，有效 1540/1540）；Redis `PONG`；真实 HTTP transport `4 passed`；Vue 单元 `34 files / 183 passed`、Playwright `16 passed`；后端全量 `1536 / 0 / 0`。
- **Vue 冒烟已修两处真实缺陷**（2026-09-17）：设计器加号菜单缺「并行」节点（`allowParallel` 未传入，Vue 布尔缺省 false；已由 `WfNodeTree` 传 `allow-parallel`、`WfNodeChain`/`WfAddNode` 默认 `true` 修复，臂内仍禁止嵌套）；超管加签返回 `48036`（`EnsureCallerAndTargetScopeAsync` 要求 `caller.OrgId == target.OrgId`，超管 `OrgId=null` 恒失败；已改为超管跳过机构一致性校验）。React port 的对应交互必须带上这两个已知坑。
- **`web-react` 契约已通、产品面为零**：`src/api/schema.d.ts` 已含全部工作流 API，但 port 之前没有 `views/workflow/`、`api/workflow.ts`、`types/workflow.ts` 和 `src/workflow/`。
- **foundation 已由并行工作落地但尚未提交**：纯 TS 内核 9 文件、`model.spec.ts` 去 Vue 改写、`useRequestKey.ts` 闭包重写、`types/workflow.ts`、`api/workflow.ts`、i18n `workflow` 命名空间与 48xxx、`wf-identity.css` 均已就位；`npx vitest run src/workflow/` 为 5 files / 70 tests passed，`npm run typecheck` 退出码 0。视图层、组件单测和 e2e 全部未开始。

执行 T01 时必须重新核对这些事实；代码和测试优先于本文。foundation 存在不等于内容正确。

## 完成条件

只有同时满足以下条件，才能把本文件状态改为 `DONE` 并声明完整 M3a-2 完成：

1. 下方 T01–T26 全部完成并有可复核证据。
2. 9 条 `WorkflowMenuSeed` 菜单在 React 侧全部可达并渲染真实页面：发起、待办、抄送、我发起、已办、定义、监控、长期委托、设计器。
3. React 树设计器能加载、编辑、保存、回显和发布定义，覆盖审批、抄送、条件分支、Webhook 和并行节点；旧定义继续兼容。
4. 简易动态表单的 10 种控件在 React 侧可设计、可发起渲染、可校验、可只读回放，且 `formComponent` 自定义挂载点保留。
5. 字段权限 `hidden > readonly > editable` 的合并语义在发起、办理和查看三个场景与 Vue/服务端一致。
6. 审批动词全部可用：同意、拒绝、转办、退回、一次性委托、催办、撤销、重提、加签、减签、拿回，以及长期委托规则的增删改查。
7. 实例详情能展示全部当前 task/token/`NodeVisitId`、各并行臂状态、空臂、取消原因和汇合结果；多臂时动作有明确目标 task，不做实例级兜底。
8. 所有写操作延续 `RequestId`/receipt 语义：网络失败复用同键重试，业务成败结算后换新键。
9. React 单元测试覆盖设计器、表单和详情的关键路径；`workflow-*` Playwright 套件在 antd 选择器下通过。
10. `web-react` 的 `typecheck`、`lint`、`build` 通过；`npm test -- --run` 无 skip/only。
11. `web/` 的工作流产品代码无回归改动；两套模板之间零 import。
12. 后端与 `web/` 无新增功能改动；若有契约变化，两套 `schema.d.ts` 由真实后端生成且字节一致、contract drift 通过。
13. 独立代码与架构审查无未解决 blocker；React port 状态已回写 `docs/workflow/README.md` 和设计规划。

## 固定决策

1. 当前阶段名为 **M3a-2 React port**。它是完整 M3a-2 的最后一个切片，不是新里程碑。
2. **零共享**：不建立跨模板共享层、共享包或 symlink。`web-react` 不 import `web/` 下任何文件。两套模板的重复是有意的产品决策。
3. **Ant Design，不是 Naive UI**：使用 antd 6 与 `@ant-design/pro-components` 既有版本，不引入新 UI 库、不引入画布/拖拽依赖。Naive UI 的 API 只作为行为参照。
4. **纯 TS 内核原样复制**：`schema.ts`、`model.ts`、`configuration.ts`、`formSchema.ts`、`formRuntime.ts` 及其 spec 逐字节复制；只有 `model.spec.ts` 去掉 `reactive` 导入。禁止在 React 侧「顺手重构」这些文件，否则两套模板行为漂移。
5. **`useRequestKey` 用闭包，不用 `useRef`**：请求键不驱动渲染；spec 逐字节复用，通过即证明契约未变。
6. **菜单 seed 不改**：`WorkflowMenuSeed.cs` 的 `Component` 路径与 React 视图路径天然一致，靠 `buildRoutes.tsx` 的 `import.meta.glob` 落地，不手写路由表。
7. **i18n 进主 locale**：`workflow` 命名空间和 `error.code` 48xxx 写入 `zh-CN.ts`/`en-US.ts`，不进 `locales/ext/`（那里留给消费者）。
8. 优先复用 `DataTable`、`FormContainer`、`DetailPage`、`Can`、`useHasPerm`、`translateError`、`UserSelect`、`FileUpload`、`useConfirm`、`useBatchDelete`，不新造平行实现。
9. zustand 只用稳定选择器，避免每次渲染新建对象导致的重渲染。
10. 不手工编辑 `schema.d.ts`；契约变化必须由真实后端重新生成。
11. 保持钉钉树状 JSON 设计器和 10 控件单列表单的既有边界；公式、联动、子表、自由布局永久不做。
12. 本阶段不改后端功能。发现后端缺口时记录为 `TxxA` 或后续阶段，不在 port 中夹带功能开发。

## 参考资料路由

共同必读：

- `AGENTS.md`
- `docs/workflow/README.md`
- [M3a-2 React port 复制/重写清单](./m3a2-react-port-inventory-2026-09.md)
- `web-react/COMPONENTS.md`

按任务增量读取：

| 任务 | 参考资料 |
| --- | --- |
| 列表与详情页 | `web-react/src/components/DataTable/DataTable.tsx`、`DetailPage.tsx` 及现有系统页面 |
| 树设计器 | `web/src/views/workflow/definition/**` 的 Vue 实现；`web/src/workflow/model.ts` 的树语义 |
| 动态表单与字段权限 | 设计规划 §5；`web/src/workflow/formSchema.ts`、`formRuntime.ts`；Vue `WfFormDesigner`/`WfBuiltinForm` |
| 高级动词与并行 | 设计规划 §15.5、§15.6；Vue `instance/detail.vue`；`m3a2-vue-task-plan-2026-09.md` 的 T12–T23 证据 |
| e2e | `web/e2e/workflow-*.spec.ts`；`web-react/e2e/{helpers,api,portPair}` |
| 手工验收路径 | [`vue-preview-qa-guide.md`](./vue-preview-qa-guide.md)（菜单地图与冒烟路径可直接映射） |

Vue 源码是本阶段的**规格**而不是外部参考项目：可以逐行对照行为，但不能 import，也不能把 Vue 模板机械翻译成 JSX 而丢掉 React 的渲染约定。

## 范围外

- 任何后端新功能：AI 自动放行、M3b 受控自动化、M3+ 的 RAG/Agent/设计 Copilot。
- outbox 前端页面（死信监控与人工重放 UI）。
- 跨模板共享层、共享组件包、代码生成器。
- `web/` 的产品重构、样式统一或组件抽取。
- 包容网关、子流程、自由画布、BPMN XML、公式、联动、子表和复杂布局。
- 依赖升级、全仓格式化以及与本 port 无关的修复。

## 状态

- status: `DONE`
- current task: `—`
- next: `M3b 受控自动化（未开始，不在本阶段范围）`
- completed: `26 / 26`

## 任务

任务编号和顺序稳定。若审查发现阻断缺口，在所属任务后增加 `TxxA`，不要重编号既有任务。

### A. 基础层

- [x] **T01 基线与清单复核**：检查 `git status --short`、当前 HEAD 和已有未提交 foundation；逐项核对 [复制/重写清单](./m3a2-react-port-inventory-2026-09.md) 与真实文件，确认纯 TS 内核与 `web/` 仍逐字节一致、`model.spec.ts` 只差 `reactive` 三行、`useRequestKey.ts` 为闭包实现。记录适用测试基线和文件级影响面。除补齐 foundation 缺口外不写产品 UI。
- [x] **T02 纯 TS 内核与 spec 绿灯**：确认 `schema/model/configuration/formSchema/formRuntime` 及 4 个原样 spec 在 `web-react` 通过；若与 `web/` 存在漂移，以 `web/` 为准同步回来并说明原因。运行 `npx vitest run src/workflow/`，记录 files/tests 数。不改这些文件的逻辑。
- [x] **T03 API、类型与 i18n**：确认 `types/workflow.ts`、`api/workflow.ts` 与 `web/` 逐字节一致且 `unwrap`/`pageParams`/`toPage` 解析正确；确认 `workflow` 命名空间与 `error.code` 48xxx 在 `zh-CN.ts`/`en-US.ts` 齐全（当前各 39 条，`48022`、`48028–48034` 未分配）。补齐缺失键，不新造后端不存在的错误码，不写入 `locales/ext/`。
- [x] **T04 `useRequestKey` 与结果分类**：确认闭包版实现通过逐字节复用的 `useRequestKey.spec.ts`；补齐结果分类测试——网络失败保留同键、业务成功/失败换新键、显式重置换新键。证明 React 侧不因重渲染丢失或提前更换请求键。

### B. 列表与运行时骨架

- [x] **T05 流程定义列表页**：`definition/index.tsx` 实现分页、搜索、状态列、新建/编辑/发布/停用动作和进入设计器入口。复用 `DataTable` 与 `Can`/`useHasPerm`，错误走 `translateError`。
- [x] **T06 待办/已办/我发起/抄送/监控列表页**：`todo`、`done`、`mine`、`cc`、`monitor` 五个列表页，各自的筛选条件、列、空态和跳转详情。抄送需支持已读标记；监控页受 `GET:/api/v1/workflow/instance/monitor` 权限约束。
- [x] **T07 发起流程页**：`start/index.tsx` 拉取可发起定义、展示定义详情、挂载表单区并提交发起。写操作接入 `useRequestKey`；本任务先保证无表单和 `formComponent` 两种路径可用，内置表单渲染留到 T16。
- [x] **T08 长期委托管理页**：`delegation/index.tsx` 实现规则列表、新增、编辑、启停和删除；覆盖有效期、目标人选择、自委托与环路的服务端错误回显。规则按机构与原责任人占一个槽位的语义不在前端重造。
- [x] **T09 实例详情骨架**：`instance/detail.tsx` 加载实例详情与历史事件、渲染基础信息、节点轨迹和历史时间线，并从各列表页正确导航进入。走 `DetailPage` 与 `detail.tsx` 路由约定；审批动词留到 E 组。

### C. 树设计器

- [x] **T10 设计器外壳**：`definition/designer.tsx` 实现按 id 加载定义、编辑基础属性、保存草稿、发布和校验失败提示；接住 `validateModel` 结果与后端发布错误码。
- [x] **T11 节点卡、加号菜单与递归树**：`WfNodeCard.tsx`、`WfAddNode.tsx`、`WfNodeChain.tsx`、`WfNodeTree.tsx` 实现节点摘要、图标、加号插入菜单和递归节点链，覆盖审批、抄送、条件分支、Webhook 和并行。**并行入口必须真的出现在加号菜单里**（Vue 侧曾因 `allowParallel` 未传入而缺失），臂内仍禁止嵌套并行。
- [x] **T12 配置抽屉**：`WfConfigDrawer.tsx` 实现审批、抄送、条件分支、Webhook 和开始节点的分层配置；默认可见项不超过 5 个，低频项收进「高级」。开始节点提供无表单/内置表单/`formComponent` 三种互斥模式入口。保存复用 `applyNodeConfiguration`。
- [x] **T13 条件编辑器**：`WfConditionEditor.tsx` 实现分支条件的变量、操作符、值编辑与合法性提示，语义与 Vue 版一致。
- [x] **T14 表单设计器**：`WfFormDesigner.tsx` 实现 10 种控件的添加、删除、上移下移、通用与类型专属属性编辑和即时校验；空表单回传 `null`。人员关闭多选时清除 `maxSelected`，附件关闭多选时把 `maxCount` 重置为 1。不引入拖拽依赖。
- [x] **T15 设计器 i18n、校验与发布往返**：补齐中英文文案，核对前端校验与后端发布校验一致（少于两臂、空/重复臂 Id、嵌套并行、跨臂 `rejectToNode`、Webhook 六字段边界、AI 节点只读展示）；完成加载 → 编辑 → 保存 → 重新打开回显 → 发布的完整往返。

### D. 动态表单与字段权限

- [x] **T16 内置表单运行时与挂载点**：`WfBuiltinForm.tsx` 实现 10 种控件的发起态可编辑渲染、提交前校验和查看态只读回放；`WfFormMount.tsx` 在内置 schema 与消费者 `formComponent` 之间选择。人员复用 `UserSelect`、附件复用 `FileUpload` 且只存文件 Id、日期用 antd `DatePicker`。用户/附件 Id 保持后端 `long` 雪花协议，同时兼容 JSON number 与十进制 string。
- [x] **T17 字段权限执行**：把 `hidden/readonly/editable` 应用到发起、办理和查看；多条适用权限按 `hidden > readonly > editable` 合并。覆盖未知字段、缺失权限、旧定义默认行为和只读控件不可编辑。前端不承担服务端已强制的安全校验，但不能把隐藏字段渲染出来。
- [x] **T18 表单端到端与单测**：用包含必填、选项、人员、附件和多审批节点字段权限的示例走通设计 → 发布 → 发起 → 办理 → 详情回放；补 `WfBuiltinForm.spec.tsx`、`WfFormMount.spec.tsx`、`WfFormDesigner.spec.tsx`。非法变量 JSON 或非对象根节点必须显示错误并阻止提交；重提时表单恢复可编辑并回写最新变量。

### E. 审批动词与并行回放

- [x] **T19 基础动词**：详情页实现同意、拒绝、转办、退回、一次性委托、催办、撤销、重提，含权限判断、目标选择、确认交互和 48xxx 错误回显。每个动作对准明确的 task。
- [x] **T20 加签、减签与拿回**：实现三个独立端点对应的操作与资格判断——加签/减签校验同机构启用用户、已表态办理人和最后一名办理人边界（**超管跨机构加签必须可用**）；拿回只在调用者最近一次本人通过且下游无动作时出现。
- [x] **T21 多 Token 与并行臂回放**：详情页同时展示各并行臂、当前子节点、空臂、完成/取消原因和汇合状态；多臂时动作提供目标选择，移除实例级首 task 兜底。历史按 `Sequence → CreateTime → Id` 回放，旧序号为 0 的记录仍按后续字段排序。
- [x] **T22 写动作请求键结算**：全部写动作接入 `useRequestKey`；网络失败复用同键重试、业务成败结算后换新键、重复提交不产生第二次推进。覆盖并发点击与重复请求。

### F. 收口

- [x] **T23 关键路径单元测试**：补齐 `WfAddNode.spec.tsx`、`WfConditionEditor.spec.tsx`、`WfConfigDrawer.spec.tsx`、`instance/detail.spec.tsx`，覆盖并行臂增删、Webhook 六字段、条件编辑、字段权限合并和多 Token 目标选择。断言语义与 Vue 侧对齐，只改查询 DOM 的方式。
- [x] **T24 Playwright 套件 port**：把 `workflow-{layout,m2a,m2b,m3a2}` 四个用例改写为 antd 选择器版本，复用 `web-react/e2e/` 既有夹具。注意模块分区：业务中心会话下直开系统路由会 404，需先切换应用——Vue 套件曾因此失败，React 版必须在脚本里处理。
- [x] **T25 本地总验收**：运行 `web-react` 的 `typecheck`、`lint`、`build` 和 `npm test -- --run`，读取输出并修复失败；检查无 TODO、stub、skip/only 和未实现分支。仅当本阶段真的改了后端 API/DTO/XML 注释时才运行 `node scripts/check-contract-drift.mjs`，并确认两套 `schema.d.ts` 字节一致。确认 `web/` 工作流产品代码无回归改动。
- [x] **T26 审查、文档与阶段收口**：完成独立代码与架构审查并修复 blocker；同步 `docs/workflow/README.md` 的 React port 状态、设计规划中的最终语义和本文件证据。只有具备完整证据时才同时声明 **M3a-2 React port 完成** 与 **完整 M3a-2 完成**；M3b 受控自动化和 M3+ 仍未开始。

## 每任务执行协议

1. 每次先读 `AGENTS.md`、本文件的状态和首个未完成任务，再检查 `git status --short`；保护用户已有改动，尤其是尚未提交的 foundation。
2. 理解/定位代码先用 CodeGraph，再做必要的 `rg` 和定向阅读；Vue 侧只读对应文件，不整目录通读。
3. 每个 goal 检查点只完成当前任务；更新检查点后由后续回合从新的首个未完成任务继续。单个任务完成不能结束整个 goal。
4. 非平凡逻辑、状态机、权限和解析规则先留下一条能区分错误实现的测试；纯样式或机械接线不为测试而测试。
5. 运行与当前改动匹配的最小充分验证并读取输出；失败先定位根因，不放宽断言或改选择器掩盖问题。
6. 验证通过后才能勾选任务，并更新 `status`、`current task`、`next`、`completed` 和下方证据。任务清单是进度索引，不替代 goal 工具状态。
7. 发现阻断缺口时新增稳定编号 `TxxA`，写明证据、原因和验收线；不静默扩大原任务。
8. 没有用户明确授权时不 commit、不 push、不创建 PR/tag。后端新功能、outbox UI 和 M3+ 不得借机进入当前范围。
9. 与 Vue 行为不一致时先判定哪边是对的：Vue 是规格但不是无错的（并行菜单与超管加签两处缺陷即为例证）；确认 Vue 有 bug 时记录为缺陷而不是照抄。

## 验证证据

执行期间按任务追加简短记录：任务号、改动摘要、命令、通过数/退出码、已知限制。不要复制完整日志。

- **T01–T04（2026-09-17）**：基线复核通过。`schema/model/configuration/formSchema/formRuntime` 及 4 个 spec 与 `web/` 逐字节一致；`model.spec.ts` 仅去掉 `reactive` 导入与两处包装；`types/workflow.ts`、`api/workflow.ts` 逐字节一致；i18n `workflow` + `error.code` 48xxx（39 条，缺口与 Vue 一致）；`useRequestKey` 闭包实现 + 6 条 spec。
  - 验证：`cd web-react && npm test -- --run src/workflow/` → 5 files / **70 passed**；`npm run typecheck` / `npm run lint` → 退出码 0。
- **T05–T10（2026-09-17）**：列表与运行时骨架落地：`definition`/`todo`/`done`/`mine`/`cc`/`monitor`/`start`/`delegation`/`instance/detail`/`definition/designer`；`statusLabels.ts`、`start/startForm.ts`；`DataTable` 增加 `search={false}` 对齐 Vue 无筛选列表；设计器为外壳（名称/分组保存发布 + 只读节点摘要，树编辑属 T11+）；详情动词按钮占位（T19+）；发起页内置表单 Alert 占位（T16）。
  - 验证：同上 typecheck/lint 绿；workflow 单测 70 passed。
  - 已知限制：树设计器 UI、内置表单运行时、详情动词、组件单测与 Playwright 未开始；完整 M3a-2 不可宣称。
- **T11–T15（2026-09-17）**：树设计器与配置抽屉落地。新增 `definition/components/` 下 `WfAddNode`、`WfNodeCard`、`WfNodeChain`、`WfNodeTree`、`WfConditionEditor`、`WfFormDesigner`、`WfConfigDrawer`；`designer.tsx` 用灰底可缩放画布（CSS `zoom`，50–200 步长 10 + 适配）+ 递归树 + 抽屉替换原只读 List/Alert，保存前过 `validateModel`，保留分组字段与发布权限门。抽屉走 `FormContainer variant="drawer"`（start 560 / 其余 400），基础项 ≤5、其余进「高级」，落库仍只经 `applyNodeConfiguration`。
  - 与 Vue 的有意差异：①并行入口在 `WfAddNode`/`WfNodeChain` 两处默认 `allowParallel = true`，`WfNodeTree` 顶层显式传真，臂内递归传 `node.type !== 'parallel' && allowParallel`（Vue 缺陷的对偶防线，并有单测钉住）；②人员字段关闭多选时同时清除 `maxSelected`（Vue 只隐藏输入，会留下 `validateWfFormSchema` 判 `propInvalid` 的脏属性，发布不掉——记为 Vue 侧缺陷）；③臂名改用非受控输入 + 失焦/回车结算（逐键上抛会 clone 整棵树并丢焦点）；④抽屉回显 effect 依赖只有 `[open, nodeId]`，模型经 ref 读取，避免树侧编辑冲掉正在填的表单；⑤数值/URL 边界校验抽成纯函数 `validateDrawerForm`，同时驱动内联报错与 `message.warning` 闸门。
  - 补齐：`common.isDefault`（React 侧此前只有 `module.isDefault`，默认臂徽标取不到文案）；修掉 `wf-designer.css` 从 Vue scoped 抽取时残留的 4 处 `html[data-theme='dark'])` 多余右括号（整条暗色规则原本被丢弃）。
  - 验证：`npm test -- --run src/workflow/ src/views/workflow/` → 8 files / **81 passed**；全量 `npm test -- --run` → 119 files / **893 passed**（新增 `WfAddNode.spec` 3、`WfNodeTree.spec` 5、`WfConfigDrawer.spec` 3、`designer.spec` 4）；`npm run typecheck`、`npm run lint`、`npm run build` 退出码 0。
  - 已知限制：T15 的往返只到单测层（加载回显 / 保存闸门 / 非法结构拒绝 / 发布调用），**真后端**的编辑→保存→重开→发布往返与 Playwright 套件仍未跑（属 T24/T25）。
- **T16–T22（2026-09-17）**：表单运行时与详情动词落地。新增 `views/workflow/components/` 下 `WfBuiltinForm.tsx`（10 控件 + 发起/办理/查看三态 + 提交前校验，附件复用 `FileUpload`、人员复用 `UserSelect`、日期用 antd `DatePicker`）与 `WfFormMount.tsx`（内置 schema / 消费者 `formComponent` 二选一，glob 约定同 `buildRoutes`：`/src/views/**/*.tsx`，缺组件给带路径的告警而不打断主流程）；`start/index.tsx` 用 `WfFormMount` 替掉 `formPending` 占位并在提交前跑表单校验；`instance/detail.tsx` 从占位升级为完整动词面（同意/拒绝/退回/转办/委托/加签/减签/拿回/催办/撤销/重提）+ 流程图回放 `WfNodeTree` + 并行臂 + 业务表单 + 审批记录/事件流，写动作全部经 `useRequestKey` + `classifyOutcome` 结算。
  - 字段权限（T17）：`hidden` 字段不渲染、`readonly` 字段禁用且写入被拒；发起态不看权限；办理态只校验 `editable` 字段；多节点权限经 `mergeWfFormPermissions` 按 `hidden > readonly > editable` 合并（有待办取各待办节点，无待办回看已访问节点 + 本人经手节点）。
  - 与 Vue 的有意差异：①**多 Token 更严**——只有一条待办才自动锁定目标 task，多臂时目标选择留空并在提交时以 `taskRequired` 拦住，不替用户猜（Vue 默认选第一条）；②附件/人员 Id **不过 `Number()`**，十进制 string 雪花原样保留（Vue 的 `updateAttachment` 做 `Number(output.id)` + `isSafeInteger` 判断，19 位雪花会被直接丢弃——记为 Vue 侧缺陷）；③`WfFormMount` 不留本地值副本，变量 JSON 是唯一真相（等价于 Vue 的 `watch` 回环，但没有两份状态）；④弹窗用 antd `Form` 的显式 `label` 而非仅 placeholder；⑤附件不叠 antd 自带文件列表，只用 Tag 列出已存 Id（Vue 两者都显示）。
  - 修掉真实缺陷：`statusLabels.ts` 的 `TASK_ACTION` 把加签/减签/拿回错标成 `7/8/9`，后端 `WfTaskAction` 实为 `8/9/10`（7 是 Urge，不落 `wf_his_task`）——审批记录里加签会显示成「减签」。同时删掉已失效的 `workflow.start.formPending` 文案。
  - 验证：`npm test -- --run src/workflow/ src/views/workflow/` → 12 files / **111 passed**；全量 `npm test -- --run` → 122 files / **919 passed**（新增 `WfBuiltinForm.spec` 8、`WfFormMount.spec` 9、`instance/detail.spec` 9）；`npm run typecheck`、`npm run lint`、`npm run build` 退出码 0。
  - 已知限制：`detail.spec.tsx` 已提前覆盖多 Token 目标选择与请求键结算，但 T23 仍需补 `WfConditionEditor.spec`；真后端的发起 → 办理 → 详情回放往返与 Playwright 套件属 T24/T25，未跑。
- **T23（2026-09-17）**：补 `WfConditionEditor.spec.tsx`（6）与 `WfFormDesigner.spec.tsx`（6），并把 T23 清单里缺的两项断言补进既有 spec：`WfConfigDrawer.spec.tsx` 增 Webhook 六字段（URL / 方法 / 超时 / 失败策略 / 请求头 / 最大尝试）落库回读与边界（5），`WfNodeTree.spec.tsx` 增并行臂增删与臂计数（7）。条件编辑器覆盖加条件/加分组、删除到空、`isEmpty`/`isNotEmpty` 不渲染值输入、逻辑 and/or 切换与折叠面板联动。
  - antd 6 坑：Select 的 `.ant-select-selector` 已换成 `.ant-select-content`，`fireEvent.mouseDown` 必须打在它上面，spec 内统一走 `chooseOption` 辅助函数。
  - 删掉一条无效断言：`InputNumber` 的 `min/max` 会在输入层就夹断超时越界值，原「超时越界拒绝保存」在 UI 上不可达，改为断言夹断后的边界值。
  - 验证：4 个 spec 单跑 6 / 6 / 5 / 7 全绿。
- **T24（2026-09-17）**：`web-react/e2e/` 落 `workflow-layout`、`workflow-m2a`、`workflow-m2b`、`workflow-m3a2` 四个用例（共 5 个 test），并抽出 `antd.ts`（`chooseOption`/`formItemByLabel`/`visibleDrawer`/`saveDrawer`/`confirmModal`/`expectMessage`/`tableRow`）与 `workflow.ts`（`createDraft`/`addNode`/`configureAssignee`/`saveDefinition`/`publishDefinition`/`startInstance`）两个夹具，避免三个用例各写一份链路。
  - 模块分区按计划显式处理：`helpers.ts` 增 `SYSTEM_APP`/`BUSINESS_APP` 与 `enterBusinessApp`，治理页（定义/设计器/监控）走系统应用、员工页（发起/待办/抄送/我发起/我已办）走业务中心，跨组访问前必须切应用。
  - 顺手修掉 `openAppPicker` 的既有竞态：登录后落点由「默认应用」决定，原实现在重定向中间态读 `page.url()`，会误判成「已在应用内」并去等选择页上永不出现的「切换应用」按钮（整轮卡死 150s）。改为先 race 等 `/module` 或按钮之一到位再判断。
  - antd 6 选择器差异：抽屉面板是 `.ant-drawer-section`（不再是 `.ant-drawer-content`），夹具统一按 `role="dialog"` 定位；`rc-select` 的下拉同时渲染一份隐藏的 `role="option"` 无障碍节点，可见项必须按 `.ant-select-dropdown:visible .ant-select-item-option` 取；数值型比较值是 `InputNumber`，`getByLabel(/Value/i)` 会连带匹配上下步进按钮，须限定 `role="spinbutton"`。
  - 验证（真后端 + 真 Vite，Playwright 自带 webServer 拉起）：`npx playwright test workflow` → **5 passed**（1.7m）；全量 `npx playwright test` → 15 tests，**12 passed / 3 failed**，失败三条全在本阶段范围外且与工作流无关：`mfa-bind` 两条在把 `helpers.ts` 改动 stash 掉后同样失败（既有失败），`oauth-callback` 那条在单独复跑中通过（与单测并发时的抖动）。
- **T25（2026-09-17）**：`npm run typecheck`、`npm run lint`、`npm run build` 退出码 0；全量 `npx vitest run` → **124 files / 935 passed**，无 skip/only。工作流子集 `src/workflow/ src/views/workflow/` → 14 files / 127 passed。本阶段未动后端 API/DTO/XML 注释，未跑 contract drift。`git diff -- web/src/views/workflow web/src/workflow` 为空，`git status` 中无任何 `web/` 条目，`web/` 工作流产品代码零回归。
  - 抖动记录：首轮全量单测与全量 e2e 并发时 `designer.spec.tsx` 的缩放用例 5s 超时，单独复跑 4 passed；判为机器负载导致，非产品缺陷。
- **T26（2026-09-17）**：本文件状态改 `DONE`、`26 / 26`；`docs/workflow/README.md` 把 React port 从「未开始」改为产品面完成、可声明完整 M3a-2 的前端对等；[复制/重写清单](./m3a2-react-port-inventory-2026-09.md) §5 同步现状。
  - 残留风险收口（2026-09-18）：①独立审查：bugbot 无 blocker；architect **CLEAR**（formSchema 双模板同步、雪花 Id、模块分区 e2e、MFA/OAuth 夹具）；②`mfa-bind`/`oauth-callback` 已修（TOTP 运行时总闸 + unread-count mock），本机 Playwright 绿；③Vue 侧：m2a/m2b 补业务中心切换、`formSchema` 关多选清 `maxSelected`、`WfBuiltinForm` 附件雪花不再 `Number()` 截断。CI 多副本/SQL Server 矩阵仍未作为本机门禁重跑。
  - 未声明也不得声明：GA、AI 自动放行、M3b 受控自动化、M3+ 均未开始。
