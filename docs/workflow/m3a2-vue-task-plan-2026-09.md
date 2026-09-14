# M3a-2 Vue 阶段任务计划

## 目标

完成 TenonAdmin.Workflow 的 **M3a-2 Vue 阶段**：先在 `web/` 打磨 Webhook 设计器、简易动态表单、字段权限、高级审批动词和并行分支，并补齐这些产品能力所需的后端契约。`web-react/` 的工作流页面 port 等 Vue schema 与交互稳定后另开阶段。

本文件是实现规格和进度索引，不替代 Codex goal/Ultragoal 的运行时状态，也不授予 commit、push、PR 或发布权限。完成本文件只能声明 **M3a-2 Vue 阶段完成**，不能声明完整 M3a-2 或 GA 完成。

## 已知基线（2026-09-09）

- M3a-1 和 Task 8b 已交付 Webhook 可靠执行闭环。本 Vue 计划开始时 Task 8c 尚未实现；该切片已于 2026-09-14 另开完成，见设计规划 §15.8。
- M3b-0 已交付且保持 shadow-only，不能自动批准、拒绝、完成 task 或推进 token。
- 后端已经接受并执行 Webhook 节点，[`WfNodeProps`](../../backend/src/TenonAdmin.Workflow/Schema/WfNode.cs#L81) 已有 `WebhookUrl`、`WebhookMethod`、`WebhookHeaders`、`WebhookTimeoutSeconds`、`WebhookOnFailure` 和 `MaxAttempts`。
- Vue schema 已预留 `webhook`，但当前 [`createNode`](../../web/src/workflow/model.ts#L118)、新增节点菜单、节点卡、配置抽屉和发布前校验尚未启用；[`validateModel`](../../web/src/workflow/model.ts#L227) 仍将 Webhook 判为不支持。
- 动态表单已有后端 [`WfFormSchema`](../../backend/src/TenonAdmin.Workflow/Schema/WfFormSchema.cs#L9)、10 种字段类型、`WfFormFieldPerm` 和 Vue [`WfFormMount`](../../web/src/views/workflow/components/WfFormMount.vue#L1) 自定义业务表单挂载点；尚缺完整发布校验、Vue 表单设计器、内置运行时渲染及字段权限闭环。
- [`AllPassRatio`](../../backend/src/TenonAdmin.Workflow/Schema/WfNode.cs#L48)、[`AddSign/RemoveSign/TakeBack`](../../backend/src/TenonAdmin.Workflow/Entities/WfEnums.cs#L113) 等只有预留字段或枚举，不能当成已交付功能；[`IWfTaskService`](../../backend/src/TenonAdmin.Workflow/Services/IWfTaskService.cs#L16) 明确要求 M3 动词另开接口，长期委托尚未实现。
- `parallel` 已进入枚举和 Vue 类型，但后端 [`ValidateModelForPublish`](../../backend/src/TenonAdmin.Workflow/Services/WfDefinitionService.cs#L253) 仍明确拒绝，并行 fork/join 与多 Token 产品面尚未实现。

执行 T01 时必须重新核对这些事实；代码和测试优先于本文。

## 完成条件

只有同时满足以下条件，才能把本文件状态改为 `DONE`：

1. 下方 T01–T25 全部完成并有可复核证据。
2. Vue 设计器能添加、配置、保存、回显和发布 Webhook 节点；旧定义继续兼容。
3. 简易动态表单支持既定 10 种单列控件，能在发起和实例页面按稳定 schema 渲染并提交/读取变量。
4. 审批节点字段权限的隐藏、只读、可编辑语义已经定稿，并在服务端和 Vue 的适用边界内得到验证。
5. 比例票签、加签、减签、拿回和长期委托均具有明确权限、状态机、并发、幂等、历史与 Vue 交互契约。
6. 并行分支具有明确 fork/join 身份、多 Token 状态机、失败/取消/重提语义，且 Vue 能设计并回放。
7. M2c 幂等、M3a-1 execution/lease/fence、M3b-0 shadow-only、租户/机构隔离和审计不回归。
8. 后端改动通过相关聚焦测试、Release build 和需要的四数据库契约验证。
9. `web/` 的工作流测试、typecheck、lint、build 通过；涉及真实交互时运行对应 Playwright 用例。
10. OpenAPI 变化通过 contract drift；两套生成的 `schema.d.ts` 保持一致且两套前端仍能 typecheck/build，但不开发 React 页面。
11. 独立代码、安全和简化审查无未解决 blocker；最终语义已经回写权威工作流文档。
12. `web-react/` 除 OpenAPI 生成文件外没有工作流产品代码变化；React port、Task 8c 和 M3+ 明确保留为后续阶段。

## 固定决策

1. 当前阶段名为 **M3a-2 Vue**。完整 M3a-2 仍包含后续 React port，二者不能混称。
2. 前端只实现 `web/`；后端可以为当前 Vue 纵切补齐必要契约和运行时，但每次扩展都须有任务和验收线。
3. 保持钉钉树状 JSON 设计器，不引入 LogicFlow、X6、BPMN 或新的画布依赖。
4. 动态表单限定约 10 种控件、单列布局；公式、联动、子表、自由布局永久不做，复杂业务表单继续走 `IWorkflowFormBinder`/`formComponent` 挂载点。
5. 远程节点继续复用 M3a-1 的单一 execution → worker → dispatcher → tx2 链路，不建立第二套执行器。
6. 模型输出只能是 proposal；M3b-0 全程 shadow-only。
7. Task 8c 不属于本计划；终态 outbox 只承诺已有的 `Pending` 幂等入队。
8. 新写命令必须延续 M2c `RequestId`/operation receipt、同事务回执和首次结果重放语义。
9. 新任务状态转换必须延续实例、Token、任务级 CAS 和 `NodeVisitId` 语义；并行身份在 T17 定稿后再增加最小字段或表。
10. 优先复用现有服务、组件和依赖，不新增跨模板共享层，不手工编辑 `schema.d.ts`。

## 参考资料路由

共同必读：

- `AGENTS.md`
- `docs/workflow/README.md`
- `docs/workflow/workflow-design-plan-2026-08-17.md` §5、§6、§8、§15.2–§15.3
- `web/COMPONENTS.md`、`web/DESIGN.md`
- `../参考项目/工作流/SUMMARY.md`

按任务增量读取：

| 任务 | 参考资料 |
| --- | --- |
| Webhook 设计器 | `../参考项目/工作流/Workflow-Vue3/_TENON_REF.md` 及其 `addNode.vue`、`nodeWrap.vue`、`drawer/`；只借交互语言 |
| 动态表单/字段权限 | 设计规划 §5；`../参考项目/工作流/jnpf/_TENON_REF.md`；需要 UX 对照时再看 `wflow-web-next`，不整仓通读 |
| 高级动词 | 设计规划 §10；`flowlong`、`warm-flow`、`AntFlow.net` 的 `_TENON_REF.md`；只借语义和边界，不复制源码 |
| 并行分支 | 数据库评审 §4.5、§8、§10；现有 `WfModelIndex`、Token/Agenda/CAS 测试；当前仍用树设计器，不读画布库 |

固定参考 commit 未变化时优先读 `_TENON_REF.md`，不要重复通读外部仓库。参考项目许可不明确或带附加限制时，只参考产品语义和交互，不复制代码、DOM 或 CSS。

## 范围外

- React 工作流页面、组件、路由、状态、交互和测试 port。
- Task 8c outbox consumer、transport、投递重试、死信和手工重放。
- AI 自动放行、自动拒绝、RAG、Agent、设计 Copilot 和模型供应商扩展。
- 包容网关、子流程、自由画布、BPMN XML、公式、联动、子表和复杂布局。
- 无关重构、依赖升级、全仓格式化以及与当前任务无关的修复。

## 状态

- status: `DONE`
- current task: `无（M3a-2 Vue 阶段完成）`
- next: `功能测试已收口；下一步是 React 工作流页面 port（不属于本 Vue 计划）。Task 8c 已另开切片完成，见设计规划 §15.8；M3+ 仍未开始`
- completed: `25 / 25`

## 任务

任务编号和顺序稳定。若审查发现阻断缺口，在所属任务后增加 `TxxA`，不要重编号既有任务。

### A. Webhook 设计器纵切

- [x] **T01 基线复核与执行拆解确认**：检查 Git 状态、当前 HEAD、相关代码/测试、本文“已知基线”和参考目录；用 CodeGraph 确认 Webhook、表单、动词、并行的真实调用链。记录适用测试基线和文件级影响面，不改产品代码。
- [x] **T02 Webhook Vue 模型与区分性测试**：补齐 Vue `WfNodeProps` 类型、Webhook 节点工厂、可插入/可发布类型和前端校验；测试默认值、六个配置字段、clone/序列化往返、非法值和旧模型兼容。先证明当前实现会失败，再做最小实现。
- [x] **T03 Webhook 节点卡与配置抽屉**：在新增菜单、节点卡、配置抽屉和 i18n 中接入 Webhook。默认可见项不超过 5 个，headers 等低频项放“高级”；复用现有 Naive UI 组件，不新增依赖。
- [x] **T04 Webhook 保存/回显/发布验收**：验证从定义列表进入设计器、添加 Webhook、保存、重新打开和发布；前端规则与后端发布/handler 契约一致。运行 Vue 工作流测试及必要 E2E，回写 Webhook UI 最终语义并关闭本纵切。

### B. 简易动态表单与字段权限

- [x] **T05 表单契约定稿**：基于现有 10 种 `WfFormFieldType` 定稿字段 key、label、required、placeholder、type-specific props、数量/长度、唯一性、`formComponent` 与 `formSchema` 的组合规则，以及 `formPerms` 的缺省/重复/未知字段语义。明确哪些规则由服务端强制；先回写设计文档，不写产品代码。
- [x] **T06 后端表单发布校验与持久化**：为 T05 定稿规则补区分性测试并实现最小发布校验；保持 `ModelJson`/`FormSchema` 发布快照一致，不新增业务变量表，不破坏旧定义和消费者替换能力。
- [x] **T07 Vue 表单 schema 与单列设计器**：把 `unknown` 收紧为与后端一致的类型；实现 10 种控件的添加、删除、排序、属性编辑和校验。使用简单列表/上移下移或仓内现有能力，不引入拖拽/布局依赖。
- [x] **T08 Vue 内置表单运行时**：实现发起态和查看态 JSON schema 渲染、校验、变量序列化/反序列化；复用已有用户选择、上传、日期和基础表单组件。保留 `formComponent` 自定义挂载点，不让内置表单替代消费者业务表单。
- [x] **T09 字段权限编辑与执行边界**：在审批节点配置 `hidden/readonly/editable`，并按 T05 定稿的服务端/前端责任应用到发起、办理和查看场景；覆盖未知字段、缺失权限、恶意提交和旧定义默认行为。
- [x] **T10 动态表单纵切验收**：用一个包含必填、选项、人员、附件和多审批节点字段权限的示例走通设计、发布、发起、办理、详情和回放；运行后端聚焦测试、Vue 测试/E2E 和 contract drift，回写最终 schema。

### C. 高级审批动词

- [x] **T11 动词语义与权限矩阵定稿**：逐项写死比例票签、加签、减签、拿回、长期委托的调用者、合法状态、目标选择、对 actor/task/token 的影响、历史事件、通知、RequestId/receipt、CAS 竞争和错误码。参考项目只提供词汇，不替代 Tenon 状态机决策。
- [x] **T12 比例票签纵切**：实现 `allPassRatio` 的发布校验、计票分母/取整/提前通过或失败规则、并发保护、历史和 Vue 配置/回放；证明默认 100% 与旧会签行为兼容。
- [x] **T13 加签与减签纵切**：实现独立权限端点、命令/服务、任务 actor 变化、历史、通知、幂等和 Vue 操作；保持任务级 CAS，覆盖并发加减签、非法目标、已表态办理人和最后一名办理人边界。
- [x] **T14 拿回纵切**：实现拿回的资格、时间窗口、已产生下游动作后的拒绝规则、任务/Token 回退、历史、通知、幂等和 Vue 操作；与发起人撤销、退回重提明确区分。
- [x] **T15 长期委托纵切**：在 T11 定稿后建立最小委托规则存储和可替换服务，接入办理人解析并保留原责任人审计；覆盖有效期、启停、链式/循环、自委托、租户/机构隔离和 Vue 管理入口。不把一次性任务委托重写成长期委托。
- [x] **T16 高级动词综合验收**：覆盖比例票签与加减签组合、拿回竞争、长期委托解析、重复请求、通知和历史回放；执行相关四数据库契约测试、Vue 测试/E2E、API 生成和文档回写。

### D. 并行分支与多 Token

- [x] **T17 并行语义和数据模型定稿**：先定义树 schema 的并行臂、fork/join 身份、父子 Token、汇合条件、空臂、嵌套限制、拒绝/取消/退回/重提、人工与自动节点混合以及历史回放语义；再决定最小 `ParentTokenId/ForkId` 字段或 join 表。未定稿前不写运行时代码。
- [x] **T18 并行 schema、索引和发布校验**：为 T17 模型实现后端 schema/实体/迁移、唯一约束、`WfModelIndex` 和发布校验；旧串行/排他定义保持兼容，数据库差异留在基础设施层。
- [x] **T19 fork/join 引擎实现**：在现有 Agenda、Token、`NodeVisitId` 和实例/Token CAS 上实现多 Token fork、各臂推进和确定性 join；不建立内存协调器，不让远程 handler 持有数据库事务。
- [x] **T20 并行竞争与恢复测试**：覆盖多副本重复 fork、并发到达 join、旧 owner 迟到、Webhook/AI 节点重试、拒绝/取消/退回/重提和进程崩溃恢复；证明同一臂只完成一次、join 只推进一次。
- [x] **T21 Vue 并行设计器**：沿现有树设计器增加并行容器、分支臂增删和配置校验；复用递归节点链，不引入自由画布。覆盖嵌套限制、空臂和非法模型提示。
- [x] **T22 Vue 多 Token 回放与任务体验**：实例详情同时展示各并行臂、当前节点、完成/取消状态和汇合结果；保证待办动作只影响目标 task/token，历史顺序可解释。
- [x] **T23 并行四库与端到端验收**：用包含人工、Webhook、AI shadow 和汇合后审批节点的代表流程验证定义、执行、崩溃恢复和回放；同一契约在 SQLite、MySQL、PostgreSQL、SQL Server 上通过并回写最终状态机。
- [x] **T23A 四数据库环境补跑**：待 MySQL、PostgreSQL、SQL Server 服务或 CI 矩阵可用后，按 T23 的同一代表流程和 CI 连接参数补跑三种外部方言；记录每腿真实通过数，并在全部通过后回写 T23。此补充任务不改变 T01–T25 编号。

### E. 总体验收与阶段收口

- [x] **T24 契约漂移与本地总验收**：运行受影响后端聚焦测试、Workflow/solution Release build、Vue workflow 测试、typecheck、lint、build 和必要 E2E；API 变化时运行真实后端生成两套 schema、通过 contract drift，并运行 `web-react` 的 typecheck/build 证明既有消费者未被契约破坏，不能为通过检查顺手 port React 工作流页面。检查无 TODO、stub、skip/only 和未实现分支。
- [x] **T25 审查、文档和 M3a-2 Vue 收口**：完成独立代码/架构审查，修复拿回资格与 Vue 表单联合类型 blocker 并复验；同步 `docs/workflow` 的最终语义、验证证据和剩余边界。仅关闭 M3a-2 Vue 阶段，完整 M3a-2 仍等待 React port，Task 8c/M3+ 仍未开始。

## 每任务执行协议

1. 每次先读 `AGENTS.md`、本文件的状态和首个未完成任务，再检查 `git status --short`；保护用户已有改动。
2. 理解/定位代码先用 CodeGraph，再做必要的 `rg` 和定向阅读；不重复扫描无关目录。
3. 每个 goal 检查点只完成当前任务；更新检查点后由后续回合从新的首个未完成任务继续。高风险任务先定稿语义和验收，普通实现不顺带吞并下一个任务；单个任务完成不能结束整个 goal。
4. 非平凡逻辑、状态机、安全/权限和解析规则先留下一条能区分错误实现的测试；纯样式或机械接线不为测试而测试。
5. 运行与当前改动匹配的最小充分验证并读取输出；失败先定位根因，不放宽断言掩盖问题。
6. 验证通过后才能勾选任务，并更新 `status`、`current task`、`next`、`completed` 和下方证据。任务清单是进度索引，不替代 goal 工具状态。
7. 发现阻断缺口时新增稳定编号 `TxxA`，写明证据、原因和验收线；不静默扩大原任务。
8. 没有用户明确授权时不 commit、不 push、不创建 PR/tag。React、Task 8c 和 M3+ 不得借机进入当前范围。

## 验证证据

执行期间按任务追加简短记录：任务号、改动摘要、命令、通过数/退出码、已知限制。不要复制完整日志。

- **T01（2026-09-09）**：只读完成基线复核，未改产品代码。
  - Git/参考：当前 HEAD `1889911`（`chore(agents): consolidate guidance and skills`）；工作区已有 6 个 `docs/workflow` 修改文件及本阶段两个未跟踪计划文件，均保留。已读取 `AGENTS.md`、`docs/workflow/README.md`、设计规划 §5/§6/§8/§15.2–§15.3、`web/COMPONENTS.md`、`web/DESIGN.md` 和外部 `../参考项目/工作流/SUMMARY.md`；参考目录存在但无可用 Git HEAD，未复制源码/DOM/CSS。
  - Webhook：后端 `WfNodeProps`、发布校验、`WebhookNodeHandler`、execution/worker/dispatcher 链路和可靠性测试已交付；Vue `schema.ts` 仅有 `webhookUrl`，`createNode`、新增菜单、节点卡、配置抽屉和 `validateModel` 仍拒绝/不展示 Webhook。影响面锁定为 `web/src/workflow/{schema,model}.ts`、`WfAddNode.vue`、`WfNodeTree.vue`、`WfNodeCard.vue`、`WfConfigDrawer.vue`、`designer.vue`、i18n 与 Vue 测试。
  - 表单：后端 `WfFormSchema/WfFormField/WfFormFieldPerm`、`WfModel.FormSchema`、版本快照和 OpenAPI 类型是预留；当前发布校验、Vue 设计器、内置渲染、运行时字段权限和专用测试均缺失。现有 `WfFormMount` 只挂载消费者 `formComponent` 并透传 `variablesJson`。影响面锁定为 `WfFormSchema.cs`、`WfDefinitionService.cs`、`WfConfigDrawer.vue`、发起/详情页及工作流测试。
  - 动词：`allPassRatio` 与 `AddSign/RemoveSign/TakeBack` 仅有字段/历史枚举；一次性 `Delegate` 已经通过 `WfTaskController` → `IWfTaskService` → `DelegateTaskOp` → 任务级 CAS、历史和 RequestId 闭环；长期委托及其规则存储不存在。后续从 T11 语义矩阵开始，不能把一次性委托当长期委托。
  - 并行：`WfNodeType.Parallel` 仅存在于枚举/OpenAPI；`WfDefinitionService` 发布校验明确拒绝，`EnterNodeOp` 无分派，`WfAgenda` 仍是单 FIFO；现有 Token/Instance CAS、`NodeVisitId`、`WfModelIndex` 和 branch 测试是可复用基线，尚无 fork/join、多 Token 数据模型或 Vue 设计器。
  - 验证：`cd web && npm run test -- --run src/workflow/model.spec.ts src/workflow/configuration.spec.ts` → 2 files / 29 tests passed，退出码 0；`dotnet test backend/TenonAdmin.slnx -c Release --filter 'FullyQualifiedName~WfWebhookNodeHandlerTests|FullyQualifiedName~WfBranchPublishValidationTests|FullyQualifiedName~WfDelegateTests|FullyQualifiedName~WfVersionCasTests|FullyQualifiedName~WfNodeVisitIdTests'` → 84 passed / 0 failed / 0 skipped，退出码 0。
  - 已知限制：当前验证是现状基线，不证明 M3a-2 新能力已实现；后端构建输出含既有 XML 文档和 nullable warnings。
- **T02（2026-09-09）**：在 Vue 框架无关模型层补齐 Webhook 六字段和 `fail/manual` 类型，新增默认工厂、`createNode('webhook')`、M3a-2 可发布节点集合及 URL/方法/超时/尝试次数校验；新增 4 个区分性模型测试。未改菜单、节点卡、抽屉、i18n、后端、React 或生成 schema。
  - 验证：先红后绿，`npm run test -- --run src/workflow/model.spec.ts` 首次为 3 failed / 15 passed（缺少 `createWebhookNode`），实现后为 18 passed；主线复跑同命令为 1 file / 18 tests passed，`npm run typecheck`、`npm run lint`、`git diff --check -- web/src/workflow/schema.ts web/src/workflow/model.ts web/src/workflow/model.spec.ts` 均退出码 0。
  - 已知限制：Webhook 节点尚未接入新增菜单、节点卡和配置抽屉；URL 的 SSRF/headers 安全围栏仍由后端 handler 负责，T02 只做前端可区分的基础值校验。
- **T03（2026-09-09）**：在新增节点菜单、递归节点链、节点树、节点卡和配置抽屉接入 Webhook；节点卡显示 URL/占位文案并使用 Webhook 图标；抽屉默认显示名称、URL、方法、超时、失败策略 5 项，headers 和 `maxAttempts` 放入“高级”，保存时复用 `applyNodeConfiguration`，中英文文案齐全。新增配置抽屉分层测试；未修改后端、React、`schema.d.ts` 或已有文档研究内容。
  - 验证：`cd web && npm run test -- --run src/workflow/configuration.spec.ts src/workflow/model.spec.ts src/views/workflow/definition/components/WfConditionEditor.spec.ts` → 3 files / 38 tests passed，退出码 0；`npm run typecheck`、`npm run lint`、`npm run build`、`git diff --check` 均退出码 0。
  - 已知限制：build 仅报告仓库既有依赖的 Rollup 注释和大 chunk 警告；真实定义列表进入设计器、保存/回显/发布链路留待 T04 验收。
- **T04（2026-09-09）**：新增 Vue Playwright 验收用例，覆盖定义列表进入设计器、新建草稿、添加并配置 Webhook 六字段、保存、reload 回显、发布、列表状态回显和再次进入设计器；使用 `https://example.com` 合法地址，不调用真实 Webhook。后端发布/处理契约聚焦测试确认 URL/方法/超时、失败策略和 `[1,100]` `maxAttempts` 边界；未修改后端或 React。
  - 验证：`cd web && npm run test:e2e -- e2e/workflow-m3a2.spec.ts --reporter=line` → 1 passed，退出码 0；`npm run test -- --run` → 26 files / 134 tests passed，退出码 0；后端 `dotnet test backend/TenonAdmin.slnx -c Release --filter 'FullyQualifiedName~WfWebhookNodeHandlerTests|FullyQualifiedName~WfAiDecisionNodeDefinitionTests'` → 86 passed / 0 failed / 0 skipped，退出码 0。
  - 已知限制：E2E 使用合法占位 URL 验证定义生命周期，不发送外部 HTTP；运行中 Vite dev proxy 报过一次既有 EPIPE 关闭连接警告，但用例正常通过。
- **T05（2026-09-09）**：在权威设计规划 §5 新增简易动态表单契约：10 种控件、schema 版本/字段数量与 JSON 大小、字段 key/label/required/placeholder、类型专属 props、`formComponent`/`formSchema` 互斥组合，以及 `formPerms` 缺省/重复/未知字段和旧定义兼容语义；明确服务端强制规则与前端体验责任。未写产品代码或新增 ADR。
  - 验证：`git diff --check` 通过；权威文档与现有 `WfFormSchema`、`WfModel.FormSchema`、`WfFormFieldPerm`、`WfFormMount` 逐项对照，未发现与既有二选一、单列、10 控件和 M1 预留语义冲突。
  - 已知限制：T05 只定稿契约；发布校验、快照一致性、Vue 表单设计器和运行时留待 T06–T10。
- **T06（2026-09-09）**：实现服务端动态表单发布校验与发布快照持久化；覆盖 schema 版本/大小/数量、十种控件及 props 边界、字段唯一性、审批字段权限、旧定义兼容，并将 `ModelJson.formSchema` 与 `FormSchema` 写为同一 canonical JSON。补充了 null 集合项和重复 JSON 键的损坏输入保护。
  - 验证：`dotnet test backend/TenonAdmin.slnx -c Release --filter 'FullyQualifiedName~WfAiDecisionNodeDefinitionTests' --no-restore` → 65 passed / 0 failed / 0 skipped；此前相关旧回归 → 80 passed / 0 failed / 0 skipped；Release 编译随测试通过，退出码 0。
  - 已知限制：运行时内置表单渲染、提交类型校验和字段权限执行留待 T08–T10；`formComponent` 仍由消费者挂载点承载。
- **T07（2026-09-09）**：在 `web/` 收紧 `WfFormSchema`、十种字段类型及类型专属 props；新增单列 `WfFormDesigner`，支持字段增删、上移下移、通用/专属属性编辑、即时校验和空表单 `null` 回传；在开始节点配置中接入无表单、内置 schema、`formComponent` 三种互斥模式。人员关闭多选时清除 `maxSelected`，附件关闭多选时重置 `maxCount` 为 1。未修改 `web-react/` 或生成的 `schema.d.ts`。
  - 验证：`cd web && npm run test -- --run src/workflow/formSchema.spec.ts src/workflow/configuration.spec.ts src/views/workflow/definition/components/WfFormDesigner.spec.ts` → 3 files / 33 tests passed；`npm run test -- --run` → 28 files / 150 tests passed；`npm run typecheck`、`npm run lint`、`npm run build`、`git diff --check` 均退出码 0。
  - 已知限制：内置表单发起/查看运行时、变量序列化/反序列化和字段权限执行留待 T08–T10；构建仅有仓库已有的 Rollup 注释与大 chunk 警告。
- **T08（2026-09-09）**：新增框架无关的 `formRuntime` 值解析、序列化和十种控件值校验；新增 `WfBuiltinForm` 单列运行时，发起态可编辑并在提交前校验，查看态只读回放。`WfFormMount` 统一选择内置 schema 或消费者 `formComponent`，发起页回写 `variablesJson`，实例详情读取已发布模型快照；人员复用 `UserSelect`，附件复用 `FileUpload` 并只保存文件 Id，日期复用 `NDatePicker`。未修改后端、`web-react/` 或生成的 `schema.d.ts`。
  - 验证：`cd web && npm run test -- --run src/workflow/formRuntime.spec.ts src/views/workflow/components/WfBuiltinForm.spec.ts src/views/workflow/components/WfFormMount.spec.ts` → 3 files / 5 tests passed；`npm run test -- --run` → 31 files / 155 tests passed；`npm run typecheck`、`npm run lint`、`npm run build`、`git diff --check` 均退出码 0。
  - 已知限制：审批节点 `formPerms` 的编辑器、发起/办理/查看权限执行和服务端恶意提交边界留待 T09–T10；构建仅有仓库已有的 Rollup 注释与大 chunk 警告。
- **T09（2026-09-09）**：后端 `WfFormRuntime` 在发起和审批时合并变量，按 `hidden/readonly/editable` 恢复、忽略或校验字段，并检查附件是否存在；`IWfFormTaskService` 保持旧 `IWfTaskService` 签名兼容。Vue 配置抽屉可配置字段权限，发起、办理和查看按当前节点渲染，只读态控件不可编辑；新增 `WfFormRuntimeTests` 和 Vue 相关权限测试。
  - 验证：`WfFormRuntimeTests` → 4 passed；`WfAiDecisionNodeDefinitionTests` + `WorkflowReplaceabilityTests` → 82 passed；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → 0 warning / 0 error；Vue 全量测试 → 33 files / 161 tests passed，`typecheck`、`lint`、`build` 通过；`git diff --check` 通过；contract drift 返回 1，因为两套 `schema.d.ts` 相对未提交 HEAD 存在预期的 `variablesJson` 差异，两套生成文件字节一致。
  - 已知限制：构建仅报告已有的 Rollup 注释和大 chunk 警告；contract drift 受两套 schema 相对未提交 HEAD 的预期差异限制。
- **T10（2026-09-09）**：用两级审批纵切验证必填、选项、人员、附件、节点字段权限、恶意提交、详情投影和终态回放；补强未知 schema 字段拒绝、附件 owner/扩展名/大小校验、旧任务服务带表单值时明确失败，并让终态详情使用已访问节点权限矩阵。后端详情/待办/列表不再直接返回内置表单隐藏值；两套前端继续只通过真实生成 schema 同步。
  - 验证：`WfFormRuntimeTests` → 5 passed；`cd web && npm run test -- --run` → 33 files / 162 tests passed（详情权限测试 3 passed）；`npm run typecheck`、`npm run lint`、`git diff --check` 均退出码 0；`npm run test:e2e -- e2e/workflow-m3a2.spec.ts` → 1 passed；`node scripts/check-contract-drift.mjs` 生成两套 schema 字节一致，因相对未提交 HEAD 的 `variablesJson` 预期 diff 返回 1。
  - 已知限制：E2E 仍验证既有 Webhook 定义纵切；contract drift 的非零退出仅由当前工作区尚未提交的 API schema 变化造成，不能按 clean HEAD 判定。
- **T11（2026-09-09）**：在权威设计规划 §15.5 冻结比例票签、加签、减签、拿回和长期委托的调用者与路由权限、合法状态和目标选择；明确 actor/task/token 变化、历史事件与结构化载荷、提交后通知、RequestId/receipt 重放和参数摘要、task/token/instance/规则 CAS 以及错误码。比例票签仅对 `all` 生效，默认 100%；拿回只允许调用者撤回自己最近一次通过且无下游动作；长期委托一跳解析、禁止自委托/环路、只影响新建 task，并与一次性 `DelegateAsync` 分离。
  - 验证：复核现有 `IWfTaskService`、`WfTaskController`、`WfCommands`、`WfOperationReceipt`、`ReassignTaskOpBase`、`CompleteTaskOp`、工作流枚举/错误码与数据库评审；权威语义已回写 `workflow-design-plan-2026-08-17.md` §15.5，`git diff --check` 通过。未改产品代码、API 或生成 schema。
  - 已知限制：T11 只定稿契约；比例计票、签核变更、拿回和长期委托存储/端点/前端实现留待 T12–T15。
- **T12（2026-09-09）**：后端在 `CompleteTaskOp` 和 `WfDefinitionService` 实现比例票签，新增 `WfAllPassRatioTests`；Vue schema、模型和配置测试覆盖 `all/any/seq`。默认 100% 保持旧会签兼容，门槛按 `ceil(有效审批人数 × allPassRatio / 100)` 计算；低比例达到门槛时提前通过，剩余票数已无法达到门槛时提前失败，并保留 task/token/instance CAS。
  - Vue 语义：`all` 允许配置 1–100；切换为 `any` 时清理比例；`seq` 强制 100%。
  - 验证：`WfAllPassRatioTests` → 10 passed；`WfNodeVisitIdTests` + `WfVersionCasTests` + `WfFormRuntimeTests` → 22 passed；Vue 全量测试 → 33 files / 166 tests passed，`typecheck`、`lint` 通过；代理证据中的 Release build → 0 warning / 0 error。
- **T13（2026-09-09）**：新增独立加签/减签服务与权限端点；以任务级 CAS 变更 actor，保留原审批历史并写入 sign-change 事件，提交后通知新增/移除办理人；请求回执支持原样重放和参数冲突检测，校验同机构启用用户、已表态办理人和最后办理人边界。Vue 详情页加入当前任务的加签/减签操作与错误文案。
  - 验证：`WfSignTests` → 3 passed；相关 Vue 工作流测试、typecheck、lint、build 通过；`git diff --check` 通过。
- **T14（2026-09-09）**：新增独立拿回服务与权限端点；只允许本人最近一次通过且下游没有人工动作或机器终态，事务内 CAS 关闭下游 task、回退 token/instance 并以新的 `NodeVisitId` 重入原审批节点，保留 actor/history 并提交后通知；请求回执支持重放和参数冲突检测。Vue 详情页加入无当前待办但有本人审批历史时的拿回操作与错误文案。
  - 验证：`WfTakeBackTests` → 2 passed；相关 Vue 工作流测试、typecheck、lint、build 通过；`git diff --check` 通过。
- **T15（2026-09-09）**：新增软删规则与审计历史实体、可替换的 `IWfDelegationService`、独立规则 API/权限菜单和 Vue 管理页；规则按机构与原责任人占一个槽位，UTC 窗口和启停只在新建任务时一跳解析，A→B、B→C 不递归，禁止自委托/环路/跨机构目标；actor/历史保留原责任人、实际办理人、RuleId 与机构快照。规则写入使用版本 CAS 和同事务 receipt，重放先命中回执，软删规则可复用同一 Id 重新启用并保留审计。
  - 验证：`WfDelegationTests` → 2 passed；T12–T15 与 `WorkflowReplaceabilityTests` 联合回归 → 34 passed / 0 failed / 0 skipped；Release 构建随测试通过；Vue/React 本轮无产品代码变化，React 工作树仍只有生成 schema；`git diff --check` 通过。
  - 已知限制：T16 仍需把高级动词组合、通知/历史回放及四数据库契约证据收口；完整并行语义留待 T17–T23。
- **T16（2026-09-09）**：加签扩大比例分母，减签后重算门槛并完成审批；拿回使用两个不同 requestId 进行真实并发竞争，仅成功一次，败者返回 `TaskConflict`，历史与流程事件各一条、回收通知一次；长期委托保持一跳解析，软删或禁用后原请求回执可重放，新请求返回 `NotFound`；完成相关回执、通知、历史与可替换性回归。
  - 验证：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfAllPassRatioTests|FullyQualifiedName~WfSignTests|FullyQualifiedName~WfTakeBackTests|FullyQualifiedName~WfDelegationTests|FullyQualifiedName~WorkflowReplaceabilityTests'` → 36 passed / 0 failed / 0 skipped；Vue 全量测试 → 33 files / 166 tests passed，`typecheck`、`lint`、`build` 通过；E2E → 1 passed（沿用本轮既有证据）；`node scripts/check-contract-drift.mjs` 真实生成两套 schema，字节一致，退出码 1 只因相对未提交 HEAD 的预期 API diff；`git diff --check` 通过。
  - 已知限制：当前环境无 MySQL、PostgreSQL、SQL Server 连接变量且无运行容器，四库契约待 T23/总验收执行；不把缺失环境写成通过。
- **T17（2026-09-09）**：在设计规划 §15.6 冻结 `parallelArms[]`、单访问 `ForkId`、父/子 token、`WfParallelArm`、空臂、CAS join、控制动作、统一执行链和历史回放语义；数据库评审 §4.5 与“真正开发并行网关时”同步为同一存储、迁移和阶段边界。未改产品代码。
  - 验证：`git diff --check -- docs/workflow/workflow-design-plan-2026-08-17.md docs/workflow/workflow-database-design-review-2026-08-24.md docs/workflow/m3a2-vue-task-plan-2026-09.md` 通过；交叉检索三文档确认 `parallelArms`、`ForkId`、`WfParallelArm`、`WaitingJoin`、`PendingArmCount`、T18/T19/T20 和四库边界互相引用一致。
  - 已知限制：T18–T23 均未实现；本任务没有运行后端、前端或数据库测试，也未宣称 SQLite、MySQL、PostgreSQL、SQL Server 四库契约通过。
- **T18（2026-09-09）**：实现 `parallelArms[]` schema、`WfParallelArm` CodeFirst 实体及 `(ForkId, ArmId)` 复合主键、父 token/状态索引、`WfModelIndex` 并行臂归属索引和发布校验；`WfToken` 的 `ParentTokenId/ForkId/PendingArmCount` 为可空升级列，旧串行行保持 `null`。发布允许至少两臂和空臂，拒绝空/重复臂 Id、嵌套并行和跨并行臂/外部 `rejectToNode`；未改运行时 fork/join、Vue 组件或 React 工作流页面。
  - 验证：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfParallelPublishValidationTests|FullyQualifiedName~WfParallelPersistenceContractTests'` → 6 passed / 0 failed / 0 skipped；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → 0 warning / 0 error；`cd web && npm run test -- --run src/workflow/model.spec.ts` → 22 passed；`npm run typecheck`、`npm run lint`、`git diff --check` 均通过。
  - 已知限制：四数据库（SQLite、MySQL、PostgreSQL、SQL Server）实际连接验证留待 T23/总验收；fork/join 执行、并发恢复和 Vue 并行设计器留待 T19–T23。
- **T19（2026-09-09）**：在现有 Agenda 中为并行臂绑定具体 token，新增 fork/arm-complete/join 操作；父 token 进入 `WaitingJoin`，非空臂创建 `Active` child token，空臂直接完成；各臂末端按 child token、`WfParallelArm` 和父 token 的版本/CAS 顺序推进，最后一臂恢复父 token 后沿 `parallel.next` 继续。人工审批、Webhook execution 和汇合后审批复用现有链；新增 `ParallelFork/ParallelArmCompleted/ParallelJoined` 历史事件及 `WfParallelRuntimeTests`。未实现 T20 的多副本竞争/崩溃恢复矩阵、取消/退回生命周期扩展、Vue 并行设计器或 React 工作流页面。
  - 验证：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfParallelRuntimeTests|FullyQualifiedName~WfParallelPublishValidationTests|FullyQualifiedName~WfParallelPersistenceContractTests|FullyQualifiedName~WfNodeExecutionEntryTests|FullyQualifiedName~WfNodeExecutionDispatcherTests|FullyQualifiedName~WfNodeVisitIdTests|FullyQualifiedName~WfVersionCasTests'` → 64 passed / 0 failed / 0 skipped；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → 0 warning / 0 error；`node scripts/check-contract-drift.mjs` 真实生成两套 schema，退出码 1 仅因相对未提交 HEAD 存在预期 API diff，两套文件字节一致；`cd web && npm run typecheck`、`cd web-react && npm run build`、`git diff --check` 均通过。
  - 已知限制：T21–T23 仍需完成 Vue 并行设计器、实例多 Token 回放和四数据库端到端验收；当前环境仍未做 MySQL、PostgreSQL、SQL Server 实际连接验证。
- **T20（2026-09-09）**：新增 `WfParallelRuntimeTests` 竞争与恢复矩阵，覆盖重复发起只生成一个 fork、并发臂完成及失败者新事务重试、取消/终止拒绝收敛全部父子 token/arm/task/execution、并行退回恢复唯一 parked parent、重提生成新 `ForkId`、迟到 Webhook owner、Webhook retry、AI shadow manual fallback 和 tx2 崩溃后的租约恢复。运行时新增并行控制收敛：取消/终止拒绝取消整个 fork；退回取消未完成臂并恢复父 token；重提拒绝仍活跃的多 token fork。未开发 Vue 并行设计器或 React 工作流页面。
  - 验证：`dotnet test backend/TenonAdmin.slnx -c Release --no-restore --filter 'FullyQualifiedName~WfParallelRuntimeTests'` → 10 passed / 0 failed / 0 skipped；并行与 execution recovery 联合回归 → 59 passed / 0 failed / 0 skipped；取消/拒绝/完结时间/任务 actor 回归 → 17 passed / 0 failed / 0 skipped；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → 0 warning / 0 error；`git diff --check` 通过。
  - 已知限制：四数据库实际连接验证留待 T23；T21–T23 尚未实现 Vue 并行设计器、实例多 Token 回放和四库端到端验收。
- **T21（2026-09-10）**：在现有 Vue 树设计器中接入并行节点工厂、并行臂工厂与增删/改名/臂头插入 helper；新增 parallel 节点菜单、卡片摘要/图标、并行容器和递归臂链。parallel 节点的 `next` 继续作为汇合后继；空臂可保留，parallel 臂内隐藏再次添加 parallel 的入口；配置抽屉只编辑节点名称并保留臂及汇合后继。沿用 `validateModel` 阻止少于两臂、空/重复 Id、嵌套 parallel 和跨臂拒绝目标，未引入画布或拖拽依赖。
  - 验证：`cd web && npm run test -- --run` → 33 files / 171 tests passed；`npm run typecheck`、`npm run lint`、`npm run build`、`npm run test:e2e -- e2e/workflow-m3a2.spec.ts --reporter=line` → 1 passed；`git diff --check` 通过。新增模型/配置回归覆盖 parallel 默认两臂、空臂、臂内插入、join 后继、命名配置和非法模型提示。
  - 已知限制：T22 仍需实现实例详情的多 Token 回放与任务动作边界；四数据库实际连接验证留待 T23。
- **T22（2026-09-10）**：实例详情接入历史事件 API，并展示当前全部 task/token/`NodeVisitId`、各 parallel fork 的并行臂、当前子节点、空臂、完成/取消原因和汇合状态；保留旧 `currentTaskId`/旧历史序号兼容字段。催办、拿回和审批动作均从明确目标 task 生成请求，多臂时可选择目标，移除实例级首 task 兜底；历史按 `Sequence → CreateTime → Id` 回放，旧序号为 0 的记录仍按后续字段排序。两套 schema 由真实 contract drift 生成，未开发 React 工作流页面。
  - 验证：`WfParallelRuntimeTests` → 12 passed / 0 failed / 0 skipped；Vue 详情测试 → 5 passed；Vue 全量测试 → 33 files / 173 tests passed；`cd web && npm run typecheck && npm run lint && npm run build`、`cd web-react && npm run typecheck && npm run build`、`dotnet build backend/TenonAdmin.slnx -c Release --no-restore`（0 warning / 0 error）和 `git diff --check` 均通过；`node scripts/check-contract-drift.mjs` 真实生成两套 schema，字节一致，退出码 1 仅因相对未提交 HEAD 存在预期 schema diff。
  - 已知限制：SQLite 之外的 MySQL、PostgreSQL、SQL Server 实际连接及代表流程端到端验收留待 T23；两套 schema 的预期未提交 diff 需在最终基线提交后由 contract drift 清零。
- **T23（2026-09-10）**：用包含并行人工待办、Webhook execution、AI shadow/manual fallback、汇合后审批、tx2 崩溃后租约恢复和详情历史回放的代表流程完成四库验收；SQL Server 并发臂曾暴露 actor 扫描与 `HistorySeq` 的反向锁环，已改为先取 actor 主键快照再写历史并在真实 SQL Server 上复验通过。
  - 验证：SQLite `WfParallelRuntimeTests|WfNodeExecutionProductionE2ETests` → 29 passed / 0 failed / 0 skipped；MySQL 同一过滤器 → 29 / 0 / 0；PostgreSQL 同一过滤器 → 29 / 0 / 0；SQL Server `WfParallelRuntimeTests` → 12 / 0 / 0，`WfNodeExecutionProductionE2ETests` → 17 / 0 / 0；`git diff --check` 通过。
  - 结论：四库同一代表流程共 `29/29` 通过，最终状态机、并发恢复和历史回放证据收口；未把初始 SQL Server 凭据错误或修复前死锁结果计入通过数。
- **T23A（2026-09-10）**：使用 CI 矩阵的 MySQL、PostgreSQL、SQL Server 连接参数，三种外部方言均完成 T23 同一代表流程补跑；服务端口 3306、5432、1433 均真实可用，未依赖静态配置替代数据库验证。
  - 验证：MySQL `WfParallelRuntimeTests|WfNodeExecutionProductionE2ETests` → 29 passed / 0 failed / 0 skipped；PostgreSQL 同一过滤器 → 29 / 0 / 0；SQL Server 拆分为并行运行 `12 / 0 / 0` 与 execution recovery `17 / 0 / 0`，合计 29 / 0 / 0。
  - 后续：进入 T24；当前工作区仍有本阶段预期未提交 API/schema diff，须由 T24 真实 contract drift 和总验收收口。
- **T24（2026-09-10）**：修复发布校验中 AI 节点携带 `allPassRatio` 时的错误优先级，使其稳定返回 `aiPropsUnsupported`；完成后端、两套前端、契约生成和代码卫生总验收。未开发 React 工作流页面。
  - 验证：后端聚焦过滤器 → `242 passed / 0 failed / 0 skipped`；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → `0 warning / 0 error`；Vue 全量 → `33 files / 173 tests passed`，`typecheck`、`lint`、`build` 和 `e2e/workflow-m3a2.spec.ts` → `1 passed`；React `typecheck`、`build` 通过；`git diff --check` 通过。
  - 契约：`node scripts/check-contract-drift.mjs` 真实启动 MinimalHost 并生成 `web/src/api/schema.d.ts`、`web-react/src/api/schema.d.ts`，两文件字节一致；退出码 `1` 仅表示相对未提交 HEAD 存在本阶段预期 schema/API diff，未手工编辑生成文件。`web-react` 工作区改动仅为生成 schema。
  - 卫生：生产工作流代码和本阶段测试未发现 TODO/stub/未实现分支、Vue 工作流 skip/only 或 xUnit Skip；`WorkflowReplaceabilityTests` 中既有 `NotSupportedException` 仅为替换能力测试 double，保留。
- **T25（2026-09-13，完成）**：修复拿回资格方向错误：调用者按同一主 token 上最近一次本人 `Approve`、下游动作窗口和 task/token/instance CAS 判定，不再要求调用者是下游待办 actor；详情投影从当前用户全部 pending task 解析 `MyTakeBackTaskId`。修复 Vue 内置附件表单移除和日期/时间清空的显式 `null` 语义，并修复普通用户提交 `CreateUserId` 未知或不匹配的新附件时的所有权校验绕过。Vue 实例详情和内置表单按全部适用权限以 `hidden > readonly > editable` 合并。长期委托 Add/Update/Delete 在规则图读取前于同一事务按机构 scope 获取数据库写锁，避免 A→B 与 B→A 并发写入绕过环检测；receipt/委托仅在确认唯一键冲突时重试查询，其他基础设施异常原样传播。同步设计规划与数据库评审中的表单历史权限、委托通知提交后失败、并行退回目标 `NodeId` 恢复和 runtime 安全投影语义。
  - 验证：T25 相关后端回归（拿回、委托、表单、runtime projection、并行、receipt、持久化、签名和比例票签）→ `85 passed / 0 failed / 0 skipped`；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → `0 warning / 0 error`；Vue 全量 → `33 files / 178 tests passed`；Vue `typecheck`、`lint`、`build` 通过；新增日期清空组件回归 → `6 passed`；工作流 Playwright → `1 passed`；React `typecheck`、`build` 通过；`git diff --check` 通过。
  - 契约：`node scripts/check-contract-drift.mjs` 真实启动 MinimalHost 并重新生成两套 `schema.d.ts`，两文件字节一致；退出码 `1` 仅表示相对未提交 HEAD 存在本阶段预期 API/schema 差异，未手工编辑生成文件。
  - E2E 边界：Vue 全量 Playwright 为 `14 passed / 2 failed`；两个失败分别是既有 MFA 绑定的 `input[readonly]` 定位超时和 RBAC 用例对重复 `.n-message` 的严格定位，新增 `e2e/workflow-m3a2.spec.ts` 在修复后再次 `1/1` 通过。Vue build 输出依赖注释和大 chunk 提示，但退出码为 0。
  - 审查：此前的 `code-reviewer`/`architect` 结果只覆盖 T25 初始范围，不覆盖随后追加的 T25A；拿回资格、Vue 联合类型、附件未知所有者和并发环竞态 blocker 均已修复并有回归证据，T25A 负责最终复审。
  - 阶段边界：仅声明 **M3a-2 Vue 阶段完成**；`web-react/` 只有真实 contract drift 生成文件变化，React 工作流 port、Task 8c outbox consumer/transport、AI 自动放行和 M3+ 留待后续。
- [x] **T25A（2026-09-13，完成）**：最终审查发现并修复 Transfer/Delegate/Return payload hash 参数冲突、Vue 表单联合类型、runtime projection 完整模型强转和历史 payload 脱敏问题；补充修复通用 payload hash 的 null 输入、旧回执 `PayloadHash=null` 兼容、回填取消令牌，以及回填 HostedService 的可替换性、生命周期顺序和重复注册。最终证据为既有后端聚焦 `122/122`、最终修复后回执/回填/identity 聚焦 `33/33`、Vue `33 files/179 tests`、Vue typecheck/lint/build、React typecheck/build、工作流 Playwright `1 passed`、完整 Vue Playwright `14 passed/2 failed`；两个失败是既有、范围外且已接受限制：`mfa-bind.spec.ts:9` 的 `input[readonly]` 定位超时，`rbac-permission.spec.ts:133` 的重复 `.n-message` 严格定位。`code-reviewer` 返回 `APPROVE`，`architect` 返回 `CLEAR`。

- **T25A 复审跟进（2026-09-14）**：针对阶段收口后的 review 反馈完成最小修复：内置表单用户/附件 ID 保持后端 `long` 雪花协议，同时兼容 JSON number 与十进制 string；重提内置表单恢复可编辑态、校验并回写最新变量；非法变量 JSON 或非对象根节点显示错误并阻止提交；加签/减签在任务 CAS 前复用实例级 CAS，与撤销共享竞争边界；OpenAPI 将 `WfFormField.props` 和 `WfAssignee.params` 生成为自由 JSON。AI Decision 只增加只读展示文案和 runtime 类型，不加入当前 Vue 设计器可插入集合，继续遵守 M3b-0 shadow-only 与本阶段范围。
  - 验证：Vue 全量 `33 files / 181 tests passed`；Vue/React typecheck、lint、build 通过；后端相关聚焦 `48 passed / 0 failed / 0 skipped`；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` 为 `0 warning / 0 error`；`git diff --check HEAD` 通过。真实 MinimalHost 输出确认 `WfFormField.props`、`WfAssignee.params` 为 `type=object` 且 `additionalProperties={}`，两套 schema 已重新生成且相应类型为 `[key: string]: unknown`。
  - 已知限制：`node scripts/check-contract-drift.mjs` 已完成真实 Host 启动和两套生成，但按脚本设计在未提交工作区相对 `HEAD` 存在预期 API/schema diff 时退出 1；提交本批次后可重新运行清零。完整 Vue Playwright 的既有 MFA/RBAC 两个范围外失败仍未纳入本阶段修复。

- **功能测试收口（2026-09-14）**：本轮只收口现有 Vue/后端功能测试，不开发 React workflow port。以代码和测试为事实源修复三处真实失败，未删除断言、未 skip/only、未放宽 strict。
  - MFA：`mfa-bind.spec.ts:9` 的 `input[readonly]` 超时根因是 Playwright 宿主默认 `Totp:Enabled=false`，`bind/start` 被 `NoPermission` 挡在第 1 步。不能把 Totp 做成整个 E2E 宿主地板，否则用户/角色高危写会触发 40024 再认证。改为建用户后打开运行时 `sys.security.totp.enabled`，用例结束后密码再认证再关闭；种子框使用 `.mfa-seed` 只读契约。
  - RBAC：`rbac-permission.spec.ts:133` 的重复 `.n-message` 是连续两次「用户授权已保存」堆叠。产品在保存前 `destroyAll()` 只保留本动作结果；测试按该文案断言恰好一条，并要求角色行带「更多」、保存后抽屉关闭，避免与 UserPicker 用户行撞名。
  - 历史投影：`WfAdjacentDedupTests` 发现 `DuplicateApproverSkipped` 的 `userIds` 数组被 runtime 白名单丢掉。补齐 `nodeId`/`userIds` 与原始值数组投影，秘密字段和嵌套对象仍拒绝。
  - 验证矩阵（全部退出码 0）：`cd web && npm run test -- --run` → 34 files / 183 passed；`cd web && npm run test:e2e` → 16 passed；`cd web && npm run typecheck` / `lint` / `build` 通过；`dotnet test backend/TenonAdmin.slnx -c Release` → 1513 passed / 0 failed / 0 skipped；`dotnet build backend/TenonAdmin.slnx -c Release --no-restore` → 0 warning / 0 error；`node scripts/check-contract-drift.mjs` → contract in sync；`cd web-react && npm run typecheck` / `build` 通过；`git diff --check HEAD` 通过。未改 API/DTO/XML 注释，未手工编辑 `schema.d.ts`；`web-react/` 无工作流产品代码变化。
  - 边界：表单用户/附件 ID 仍是后端 `long` 雪花；非法变量仍阻止提交；`hidden/readonly/editable`、RequestId/receipt、task/token/instance CAS、NodeVisitId、execution/lease/fence 和 AI shadow-only 未改。React 工作流页面、Task 8c、AI 自动放行和 M3+ 仍未开始；React port 只在本轮功能测试无失败之后才允许启动。
